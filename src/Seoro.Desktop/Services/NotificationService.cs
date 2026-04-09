using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Seoro.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seoro.Desktop.Services;

public class NotificationService(ILogger<NotificationService> logger, IOptionsMonitor<AppSettings> appSettings)
    : INotificationService
{
    private bool _initialized;
    private bool _nativeNotificationsAvailable;
    private static bool _notificationAuthorized;

    public NotificationBackend CurrentBackend
    {
        get
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
                return NotificationBackend.Unavailable;
            if (OperatingSystem.IsWindows())
                return NotificationBackend.Native;
            // macOS
            if (_nativeNotificationsAvailable && _notificationAuthorized)
                return NotificationBackend.Native;
            return NotificationBackend.Script;
        }
    }

    public Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;

        if (OperatingSystem.IsMacOS())
        {
            try
            {
                EnsureBundleIdentifier();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "macOS 번들 식별자 설정 실패");
            }

            _nativeNotificationsAvailable = HasAppBundle();

            if (_nativeNotificationsAvailable)
                try
                {
                    RequestNotificationAuthorization();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "알림 권한 요청 실패");
                }
            else
                logger.LogInformation(
                    ".app 번들에서 실행되지 않음 — UNUserNotificationCenter 건너뜀, AppleScript 폴백 사용");
        }

        _initialized = true;
        logger.LogInformation("알림이 초기화됨");
        return Task.CompletedTask;
    }

    public async Task SendAsync(string title, string body, NotificationType type = NotificationType.Info)
    {
        var settings = appSettings.CurrentValue;
        if (!settings.NotificationsEnabled) return;

        if (!_initialized)
            await InitializeAsync();

        var playSound = settings.NotificationSound;
        var soundName = settings.NotificationSoundName;

        try
        {
            if (OperatingSystem.IsWindows())
                SendWindowsNotification(title, body, playSound);
            else if (OperatingSystem.IsMacOS())
                SendMacNotification(title, body, playSound, soundName);
            else
                logger.LogDebug("Notification (no platform): {Title} - {Body}", title, body);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "알림 전송 실패");
        }
    }

    #region macOS AppleScript Fallback

    private static void ExecuteAppleScriptInProcess(string source)
    {
        var selAlloc = SelRegisterName("alloc");
        var selRelease = SelRegisterName("release");

        var nsSource = CreateNSString(source);

        try
        {
            var nsAppleScriptClass = ObjcGetClass("NSAppleScript");
            var scriptAlloc = ObjcMsgSend(nsAppleScriptClass, selAlloc);
            var script = ObjcMsgSendIntPtr(scriptAlloc, SelRegisterName("initWithSource:"), nsSource);

            try
            {
                var result = ObjcMsgSendIntPtr(script, SelRegisterName("executeAndReturnError:"), 0);
                if (result == 0)
                    throw new InvalidOperationException("NSAppleScript executeAndReturnError: returned nil");
            }
            finally
            {
                ObjcMsgSend(script, selRelease);
            }
        }
        finally
        {
            ObjcMsgSend(nsSource, selRelease);
        }
    }

    #endregion

    private void SendMacNotification(string title, string body, bool playSound, string soundName)
    {
        // Try UNUserNotificationCenter first (proper native API, attributed to Seoro)
        // Only available when running inside a .app bundle with notification authorization granted.
        // Without authorization (e.g. unsigned app), the API silently drops notifications.
        if (_nativeNotificationsAvailable && _notificationAuthorized)
            try
            {
                SendMacNotificationNative(title, body, playSound, soundName);
                logger.LogDebug(
                    "macOS notification sent via UNUserNotificationCenter: {Title} - {Body} (sound: {Sound})",
                    title, body, playSound ? soundName : "off");
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "UNUserNotificationCenter failed, falling back to AppleScript");
            }

        var escapedTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var escapedBody = body.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var script = $"display notification \"{escapedBody}\" with title \"{escapedTitle}\"";
        if (playSound)
            script += $" sound name \"{soundName}\"";

        // In-process NSAppleScript: only try inside .app bundle — without one,
        // macOS silently drops the notification (no app to attribute it to)
        if (_nativeNotificationsAvailable)
            try
            {
                ExecuteAppleScriptInProcess(script);
                logger.LogDebug("macOS notification sent via NSAppleScript: {Title} - {Body}", title, body);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "NSAppleScript failed, falling back to osascript");
            }

        // osascript subprocess — works without .app bundle (attributed to Script Editor)
        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi);
        if (process != null)
        {
            process.WaitForExit(5000);
            if (process.ExitCode != 0)
            {
                var stderr = process.StandardError.ReadToEnd();
                logger.LogWarning("osascript exited with code {Code}: {Error}", process.ExitCode, stderr);
            }
            else
            {
                logger.LogDebug("macOS notification sent via osascript fallback: {Title} - {Body}", title, body);
            }
        }
    }

    private void SendWindowsNotification(string title, string body, bool playSound)
    {
        var escapedTitle = SecurityElement.Escape(title);
        var escapedBody = SecurityElement.Escape(body);

        var audioElement = playSound ? "" : "<audio silent=\"true\"/>";

        var toastXml =
            "<toast>" +
            "<visual>" +
            "<binding template=\"ToastGeneric\">" +
            $"<text>{escapedTitle}</text>" +
            $"<text>{escapedBody}</text>" +
            "</binding>" +
            "</visual>" +
            audioElement +
            "</toast>";

        var psXmlLiteral = toastXml.Replace("'", "''");

        var script =
            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; " +
            "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType = WindowsRuntime] | Out-Null; " +
            "$xml = [Windows.Data.Xml.Dom.XmlDocument]::new(); " +
            $"$xml.LoadXml('{psXmlLiteral}'); " +
            "$toast = [Windows.UI.Notifications.ToastNotification]::new($xml); " +
            "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Seoro').Show($toast)";

        var scriptBytes = Encoding.Unicode.GetBytes(script);
        var encodedCommand = Convert.ToBase64String(scriptBytes);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -EncodedCommand {encodedCommand}",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        logger.LogDebug("Windows notification sent: {Title} - {Body}", title, body);
    }

    #region macOS UNUserNotificationCenter

    private void SendMacNotificationNative(string title, string body, bool playSound, string soundName)
    {
        var selAlloc = SelRegisterName("alloc");

        // UNMutableNotificationContent *content = [[UNMutableNotificationContent alloc] init]
        var content = ObjcMsgSend(
            ObjcMsgSend(ObjcGetClass("UNMutableNotificationContent"), selAlloc),
            SelRegisterName("init"));

        var nsTitle = CreateNSString(title);
        var nsBody = CreateNSString(body);

        try
        {
            // content.title = title; content.body = body
            ObjcMsgSendIntPtr(content, SelRegisterName("setTitle:"), nsTitle);
            ObjcMsgSendIntPtr(content, SelRegisterName("setBody:"), nsBody);

            // content.sound = ...
            if (playSound)
            {
                nint sound;
                if (soundName == "default")
                {
                    sound = ObjcMsgSend(ObjcGetClass("UNNotificationSound"),
                        SelRegisterName("defaultSound"));
                }
                else
                {
                    var nsSoundName = CreateNSString(soundName);
                    sound = ObjcMsgSendIntPtr(ObjcGetClass("UNNotificationSound"),
                        SelRegisterName("soundNamed:"), nsSoundName);
                    ObjcMsgSend(nsSoundName, SelRegisterName("release"));
                }

                ObjcMsgSendIntPtr(content, SelRegisterName("setSound:"), sound);
            }

            // NSString *identifier = [[NSUUID UUID] UUIDString]
            var uuid = ObjcMsgSend(ObjcGetClass("NSUUID"), SelRegisterName("UUID"));
            var identifier = ObjcMsgSend(uuid, SelRegisterName("UUIDString"));

            // UNNotificationRequest *request = [UNNotificationRequest requestWithIdentifier:content:trigger:]
            var request = ObjcMsgSendThreeIntPtr(ObjcGetClass("UNNotificationRequest"),
                SelRegisterName("requestWithIdentifier:content:trigger:"),
                identifier, content, 0);

            // [[UNUserNotificationCenter currentNotificationCenter] addNotificationRequest:request withCompletionHandler:nil]
            var center = ObjcMsgSend(ObjcGetClass("UNUserNotificationCenter"),
                SelRegisterName("currentNotificationCenter"));
            ObjcMsgSendVoidTwoIntPtr(center,
                SelRegisterName("addNotificationRequest:withCompletionHandler:"),
                request, 0);
        }
        finally
        {
            ObjcMsgSend(content, SelRegisterName("release"));
            ObjcMsgSend(nsBody, SelRegisterName("release"));
            ObjcMsgSend(nsTitle, SelRegisterName("release"));
        }
    }

    private void RequestNotificationAuthorization()
    {
        // Load UserNotifications framework
        DlOpen("/System/Library/Frameworks/UserNotifications.framework/UserNotifications", 1);

        var center = ObjcMsgSend(ObjcGetClass("UNUserNotificationCenter"),
            SelRegisterName("currentNotificationCenter"));
        if (center == 0)
        {
            logger.LogWarning("UNUserNotificationCenter.currentNotificationCenter returned nil");
            return;
        }

        // Create ObjC block for the completion handler: void (^)(BOOL granted, NSError *error)
        var block = CreateAuthorizationBlock();

        // UNAuthorizationOptionAlert (1<<2) | UNAuthorizationOptionSound (1<<1) = 6
        ObjcMsgSendVoidNUIntIntPtr(center,
            SelRegisterName("requestAuthorizationWithOptions:completionHandler:"),
            6, block);

        logger.LogInformation("macOS 알림 권한 요청됨");

        // Check current authorization status synchronously
        // UNAuthorizationStatus: 0=notDetermined, 1=denied, 2=authorized, 3=provisional
        CheckNotificationAuthorizationStatus(center);
    }

    private void CheckNotificationAuthorizationStatus(nint center)
    {
        try
        {
            // [center getNotificationSettingsWithCompletionHandler:] is async,
            // so we poll the settings via a blocking approach using a semaphore-like ObjC block.
            // Simpler approach: just try sending a test and see if it works.
            // For now, give the authorization request a moment then check via settings.
            var settingsBlock = CreateSettingsBlock();
            ObjcMsgSendVoidIntPtr(center,
                SelRegisterName("getNotificationSettingsWithCompletionHandler:"),
                settingsBlock);

            // The callback sets _notificationAuthorized; give it a brief moment
            Thread.Sleep(500);

            logger.LogInformation("macOS notification authorization status: {Authorized}", _notificationAuthorized);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check notification authorization status");
        }
    }

    #endregion

    #region macOS ObjC Runtime Interop

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern nint ObjcGetClass(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern nint SelRegisterName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint ObjcMsgSend(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint ObjcMsgSendIntPtr(nint receiver, nint selector, nint arg1);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint ObjcMsgSendStr(nint receiver, nint selector,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string arg1);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint ObjcMsgSendThreeIntPtr(nint receiver, nint selector, nint arg1, nint arg2, nint arg3);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendVoidTwoIntPtr(nint receiver, nint selector, nint arg1, nint arg2);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendVoidIntPtr(nint receiver, nint selector, nint arg1);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendVoidNUIntIntPtr(nint receiver, nint selector, nuint arg1, nint arg2);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint ObjcMsgSendNInt(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "class_getInstanceMethod")]
    private static extern nint ClassGetInstanceMethod(nint cls, nint sel);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "method_setImplementation")]
    private static extern nint MethodSetImplementation(nint method, nint imp);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "object_getClass")]
    private static extern nint ObjectGetClass(nint obj);

    [DllImport("libSystem.dylib", EntryPoint = "dlopen")]
    private static extern nint DlOpen(string path, int mode);

    [DllImport("libSystem.dylib", EntryPoint = "dlsym")]
    private static extern nint DlSym(nint handle, string symbol);

    private static nint CreateNSString(string str)
    {
        return ObjcMsgSendStr(
            ObjcMsgSend(ObjcGetClass("NSString"), SelRegisterName("alloc")),
            SelRegisterName("initWithUTF8String:"), str);
    }

    #endregion

    #region macOS Bundle Identifier Swizzle

    private bool HasAppBundle()
    {
        var mainBundle = ObjcMsgSend(ObjcGetClass("NSBundle"), SelRegisterName("mainBundle"));
        var bundlePath = ObjcMsgSend(mainBundle, SelRegisterName("bundlePath"));
        var utf8Ptr = ObjcMsgSend(bundlePath, SelRegisterName("UTF8String"));
        var path = Marshal.PtrToStringUTF8(utf8Ptr);
        return path?.EndsWith(".app") == true;
    }

    private delegate nint ObjcMethodImp(nint self, nint sel);

    private static nint _bundleIdString;
    private static ObjcMethodImp? _bundleIdImp;

    private static nint BundleIdentifierOverride(nint self, nint sel)
    {
        return _bundleIdString;
    }

    private void EnsureBundleIdentifier()
    {
        var mainBundle = ObjcMsgSend(ObjcGetClass("NSBundle"), SelRegisterName("mainBundle"));
        var existingId = ObjcMsgSend(mainBundle, SelRegisterName("bundleIdentifier"));
        if (existingId != 0)
        {
            logger.LogDebug("macOS bundle identifier already set");
            return;
        }

        _bundleIdString = CreateNSString("com.seoro.app");

        _bundleIdImp = BundleIdentifierOverride;
        var bundleClass = ObjectGetClass(mainBundle);
        var method = ClassGetInstanceMethod(bundleClass, SelRegisterName("bundleIdentifier"));
        MethodSetImplementation(method, Marshal.GetFunctionPointerForDelegate(_bundleIdImp));

        logger.LogInformation("Swizzled macOS bundle identifier to com.seoro.app");
    }

    #endregion

    #region macOS ObjC Block Support

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public nint Isa;
        public int Flags;
        public int Reserved;
        public nint Invoke;
        public nint Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AuthCallbackDelegate(nint block, byte granted, nint error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SettingsCallbackDelegate(nint block, nint settings);

    // prevent GC collection of the delegate and block memory
    private static AuthCallbackDelegate? _authCallback;
    private static nint _authBlockPtr;
    private static nint _authDescriptorPtr;

    private static SettingsCallbackDelegate? _settingsCallback;
    private static nint _settingsBlockPtr;
    private static nint _settingsDescriptorPtr;

    private nint CreateSettingsBlock()
    {
        if (_settingsBlockPtr != 0) return _settingsBlockPtr;

        _settingsCallback = (block, settings) =>
        {
            try
            {
                // [settings authorizationStatus] returns NSInteger
                // 0=notDetermined, 1=denied, 2=authorized, 3=provisional, 4=ephemeral
                var status = ObjcMsgSendNInt(settings, SelRegisterName("authorizationStatus"));
                _notificationAuthorized = status >= 2; // authorized, provisional, or ephemeral
            }
            catch
            {
                // ignored
            }
        };

        var isa = DlSym(-2, "_NSConcreteGlobalBlock");

        _settingsDescriptorPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
        Marshal.StructureToPtr(new BlockDescriptor
        {
            Reserved = 0,
            Size = (nuint)Marshal.SizeOf<BlockLiteral>()
        }, _settingsDescriptorPtr, false);

        _settingsBlockPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BlockLiteral>());
        Marshal.StructureToPtr(new BlockLiteral
        {
            Isa = isa,
            Flags = 1 << 28,
            Reserved = 0,
            Invoke = Marshal.GetFunctionPointerForDelegate(_settingsCallback),
            Descriptor = _settingsDescriptorPtr
        }, _settingsBlockPtr, false);

        return _settingsBlockPtr;
    }

    private static nint CreateAuthorizationBlock()
    {
        if (_authBlockPtr != 0) return _authBlockPtr;

        _authCallback = static (block, granted, error) => { };

        var isa = DlSym(-2, "_NSConcreteGlobalBlock"); // RTLD_DEFAULT = -2

        _authDescriptorPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
        Marshal.StructureToPtr(new BlockDescriptor
        {
            Reserved = 0,
            Size = (nuint)Marshal.SizeOf<BlockLiteral>()
        }, _authDescriptorPtr, false);

        _authBlockPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BlockLiteral>());
        Marshal.StructureToPtr(new BlockLiteral
        {
            Isa = isa,
            Flags = 1 << 28, // BLOCK_IS_GLOBAL
            Reserved = 0,
            Invoke = Marshal.GetFunctionPointerForDelegate(_authCallback),
            Descriptor = _authDescriptorPtr
        }, _authBlockPtr, false);

        return _authBlockPtr;
    }

    #endregion
}