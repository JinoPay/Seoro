using System.Diagnostics;
using System.Runtime.InteropServices;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cominomi.Desktop.Services;

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly IOptionsMonitor<AppSettings> _appSettings;
    private bool _initialized;

    public NotificationService(ILogger<NotificationService> logger, IOptionsMonitor<AppSettings> appSettings)
    {
        _logger = logger;
        _appSettings = appSettings;
    }

    public Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        _logger.LogInformation("Notifications initialized");
        return Task.CompletedTask;
    }

    public async Task SendAsync(string title, string body, NotificationType type = NotificationType.Info)
    {
        var settings = _appSettings.CurrentValue;
        if (!settings.NotificationsEnabled) return;

        if (!_initialized)
            await InitializeAsync();

        var playSound = settings.NotificationSound;
        var soundName = settings.NotificationSoundName;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                SendWindowsNotification(title, body, playSound);
            }
            else if (OperatingSystem.IsMacOS())
            {
                SendMacNotification(title, body, playSound, soundName);
            }
            else
            {
                _logger.LogDebug("Notification (no platform): {Title} - {Body}", title, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification");
        }
    }

    private void SendWindowsNotification(string title, string body, bool playSound)
    {
        var escapedTitle = title.Replace("'", "''").Replace("\"", "`\"");
        var escapedBody = body.Replace("'", "''").Replace("\"", "`\"");

        var audioSnippet = playSound
            ? ""
            : "$audio = $xml.CreateElement('audio'); $audio.SetAttribute('silent','true'); " +
              "$xml.DocumentElement.AppendChild($audio) | Out-Null; ";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"" +
                $"[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; " +
                $"$xml = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                $"$text = $xml.GetElementsByTagName('text'); " +
                $"$text[0].AppendChild($xml.CreateTextNode('{escapedTitle}')) | Out-Null; " +
                $"$text[1].AppendChild($xml.CreateTextNode('{escapedBody}')) | Out-Null; " +
                $"{audioSnippet}" +
                $"$toast = [Windows.UI.Notifications.ToastNotification]::new($xml); " +
                $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Cominomi').Show($toast)\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        _logger.LogDebug("Windows notification sent: {Title} - {Body}", title, body);
    }

    private void SendMacNotification(string title, string body, bool playSound, string soundName)
    {
        var escapedTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var escapedBody = body.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var script = $"display notification \"{escapedBody}\" with title \"{escapedTitle}\"";
        if (playSound)
            script += $" sound name \"{soundName}\"";

        // Try in-process NSAppleScript first (notifications attributed to Cominomi app bundle)
        try
        {
            ExecuteAppleScriptInProcess(script);
            _logger.LogDebug("macOS notification sent via NSAppleScript: {Title} - {Body} (sound: {Sound})",
                title, body, playSound ? soundName : "off");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NSAppleScript failed, falling back to osascript");
        }

        // Fallback: osascript subprocess (shows as "Script Editor")
        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi);
        _logger.LogDebug("macOS notification sent via osascript fallback: {Title} - {Body} (sound: {Sound})",
            title, body, playSound ? soundName : "off");
    }

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

    /// <summary>
    /// Execute AppleScript in-process via NSAppleScript so that macOS attributes
    /// the notification to the Cominomi app bundle instead of Script Editor.
    /// </summary>
    private static void ExecuteAppleScriptInProcess(string source)
    {
        var selAlloc = SelRegisterName("alloc");
        var selRelease = SelRegisterName("release");

        // NSString *nsSource = [[NSString alloc] initWithUTF8String:source]
        var nsStringClass = ObjcGetClass("NSString");
        var nsStringAlloc = ObjcMsgSend(nsStringClass, selAlloc);
        var nsSource = ObjcMsgSendStr(nsStringAlloc, SelRegisterName("initWithUTF8String:"), source);

        try
        {
            // NSAppleScript *script = [[NSAppleScript alloc] initWithSource:nsSource]
            var nsAppleScriptClass = ObjcGetClass("NSAppleScript");
            var scriptAlloc = ObjcMsgSend(nsAppleScriptClass, selAlloc);
            var script = ObjcMsgSendIntPtr(scriptAlloc, SelRegisterName("initWithSource:"), nsSource);

            try
            {
                // [script executeAndReturnError:nil]
                ObjcMsgSendIntPtr(script, SelRegisterName("executeAndReturnError:"), 0);
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
}
