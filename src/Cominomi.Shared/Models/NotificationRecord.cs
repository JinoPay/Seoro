using Cominomi.Shared.Services;

namespace Cominomi.Shared.Models;

public class NotificationRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public NotificationType Type { get; init; }
    public string? SessionId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}
