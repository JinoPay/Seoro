using Cominomi.Shared.Models;
using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#if MACCATALYST
using Foundation;
using UserNotifications;
#elif WINDOWS
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
#endif

namespace Cominomi.Services;

#if MACCATALYST
internal sealed class ForegroundNotificationDelegate : NSObject, IUNUserNotificationCenterDelegate
{
    [Export("userNotificationCenter:willPresentNotification:withCompletionHandler:")]
    public void WillPresentNotification(
        UNUserNotificationCenter center,
        UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler)
    {
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound);
    }
}
#endif

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly IOptionsMonitor<AppSettings> _appSettings;
    private bool _initialized;

#if MACCATALYST
    private ForegroundNotificationDelegate? _delegate;
#endif

    public NotificationService(ILogger<NotificationService> logger, IOptionsMonitor<AppSettings> appSettings)
    {
        _logger = logger;
        _appSettings = appSettings;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

#if MACCATALYST
        try
        {
            var center = UNUserNotificationCenter.Current;
            _delegate = new ForegroundNotificationDelegate();
            center.Delegate = _delegate;
            var (granted, error) = await center.RequestAuthorizationAsync(
                UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound);

            if (granted)
            {
                _logger.LogInformation("macOS notification permission granted");
                _initialized = true;
            }
            else
            {
                _logger.LogWarning("macOS notification permission denied: {Error}", error?.LocalizedDescription);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize macOS notifications");
        }
#elif WINDOWS
        try
        {
            // NotificationInvoked must be registered BEFORE Register()
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _initialized = true;
            _logger.LogInformation("Windows notifications initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Windows notifications");
        }
#else
        await Task.CompletedTask;
#endif
    }

    public async Task SendAsync(string title, string body, NotificationType type = NotificationType.Info)
    {
        var settings = _appSettings.CurrentValue;
        if (!settings.NotificationsEnabled) return;

        if (!_initialized)
            await InitializeAsync();

#if MACCATALYST
        try
        {
            var content = new UNMutableNotificationContent
            {
                Title = title,
                Body = body,
                Sound = settings.NotificationSound ? UNNotificationSound.Default : null
            };

            var requestId = Guid.NewGuid().ToString();
            var request = UNNotificationRequest.FromIdentifier(requestId, content, null);

            await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request);
            _logger.LogDebug("macOS notification sent: {Title} - {Body}", title, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send macOS notification");
        }
#elif WINDOWS
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body);

            if (settings.NotificationSound)
            {
                builder.SetAudioUri(new Uri("ms-winsoundevent:Notification.Default"));
            }

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
            _logger.LogDebug("Windows notification sent: {Title} - {Body}", title, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Windows notification");
        }
#else
        _logger.LogDebug("Notification (no platform): {Title} - {Body}", title, body);
        await Task.CompletedTask;
#endif
    }

#if WINDOWS
    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        _logger.LogInformation("Notification clicked: {Arguments}", args.Argument);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var window = Application.Current?.Windows.FirstOrDefault();
                if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
                    Cominomi.WinUI.WindowHelper.BringToForeground(nativeWindow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to foreground window on notification click");
            }
        });
    }
#endif
}
