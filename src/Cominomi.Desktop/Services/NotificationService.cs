using System.Diagnostics;
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

        try
        {
            if (OperatingSystem.IsWindows())
            {
                SendWindowsNotification(title, body);
            }
            else if (OperatingSystem.IsMacOS())
            {
                SendMacNotification(title, body);
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

    private void SendWindowsNotification(string title, string body)
    {
        // Use PowerShell to show a Windows toast notification
        var escapedTitle = title.Replace("'", "''").Replace("\"", "`\"");
        var escapedBody = body.Replace("'", "''").Replace("\"", "`\"");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"" +
                $"[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; " +
                $"$xml = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                $"$text = $xml.GetElementsByTagName('text'); " +
                $"$text[0].AppendChild($xml.CreateTextNode('{escapedTitle}')) | Out-Null; " +
                $"$text[1].AppendChild($xml.CreateTextNode('{escapedBody}')) | Out-Null; " +
                $"$toast = [Windows.UI.Notifications.ToastNotification]::new($xml); " +
                $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Cominomi').Show($toast)\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(psi);
        _logger.LogDebug("Windows notification sent: {Title} - {Body}", title, body);
    }

    private void SendMacNotification(string title, string body)
    {
        var escapedTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var escapedBody = body.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            Arguments = $"-e 'display notification \"{escapedBody}\" with title \"{escapedTitle}\"'",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(psi);
        _logger.LogDebug("macOS notification sent: {Title} - {Body}", title, body);
    }
}
