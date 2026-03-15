using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

public class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Chat";
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Model { get; set; } = ModelDefinitions.Default.Id;
    public string WorkspaceId { get; set; } = "default";
    public string PermissionMode { get; set; } = "default";
    public List<ChatMessage> Messages { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string? ResolvedModel { get; set; }
}
