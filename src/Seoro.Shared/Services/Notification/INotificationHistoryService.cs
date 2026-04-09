
namespace Seoro.Shared.Services.Notification;

public interface INotificationHistoryService
{
    int UnreadCount { get; }
    IReadOnlyList<NotificationRecord> Entries { get; }

    event Action? OnChange;
    void MarkAllAsRead();
    void MarkAsRead(string id);
    void MarkSessionAsRead(string sessionId);

    void Record(string title, string body, NotificationType type, string? sessionId = null, bool isRead = false);
}