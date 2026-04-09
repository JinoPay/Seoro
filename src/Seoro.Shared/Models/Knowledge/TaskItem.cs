using System.Text.Json.Serialization;

namespace Seoro.Shared.Models.Knowledge;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskStatus
{
    Pending,
    InProgress,
    Completed,
    Blocked
}

public class TaskItem
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int SchemaVersion { get; set; } = 1;
    public List<string> BlockedBy { get; set; } = [];
    public List<string> Blocks { get; set; } = [];
    public string Description { get; set; } = string.Empty;
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public TaskStatus Status { get; set; } = TaskStatus.Pending;
}