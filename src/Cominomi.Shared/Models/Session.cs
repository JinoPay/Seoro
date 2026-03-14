namespace Cominomi.Shared.Models;

public class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Chat";
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public List<ChatMessage> Messages { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
