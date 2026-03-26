using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface INotificationHistoryService
{
    IReadOnlyList<NotificationRecord> Entries { get; }
    int UnreadCount { get; }

    void Record(string title, string body, NotificationType type, string? sessionId = null, bool isRead = false);
    void MarkAsRead(string id);
    void MarkAllAsRead();
    void MarkSessionAsRead(string sessionId);

    event Action? OnChange;
}
