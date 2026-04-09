using System.Text.Json.Serialization;

namespace Seoro.Shared.Models.Knowledge;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MemoryType
{
    User,
    Feedback,
    Project,
    Reference
}

public class MemoryEntry
{
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int SchemaVersion { get; init; } = 1;
    public MemoryType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? WorkspaceId { get; set; }
}