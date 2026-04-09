namespace Seoro.Shared.Services.Notification;

public enum NotificationType
{
    Info,
    Question,
    PlanReview,
    Success,
    Error
}

public enum NotificationBackend
{
    Native,
    Script,
    Unavailable
}

public interface INotificationService
{
    Task InitializeAsync();
    Task SendAsync(string title, string body, NotificationType type = NotificationType.Info);
    NotificationBackend CurrentBackend { get; }
}