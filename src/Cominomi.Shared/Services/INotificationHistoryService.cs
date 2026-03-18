using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface INotificationHistoryService
{
    IReadOnlyList<NotificationRecord> Entries { get; }
    int UnreadCount { get; }

    void Record(string title, string body, NotificationType type, string? sessionId = null);
    void MarkAsRead(string id);
    void MarkAllAsRead();

    event Action? OnChange;
}
