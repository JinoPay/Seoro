namespace Cominomi.Shared.Services;

public enum NotificationType
{
    Info,
    Question,
    PlanReview,
    Success,
    Error
}

public interface INotificationService
{
    Task InitializeAsync();
    Task SendAsync(string title, string body, NotificationType type = NotificationType.Info);
}
