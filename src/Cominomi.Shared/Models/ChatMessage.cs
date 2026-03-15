namespace Cominomi.Shared.Models;

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public MessageRole Role { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<ToolCall> ToolCalls { get; set; } = [];
    public List<FileAttachment> Attachments { get; set; } = [];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsStreaming { get; set; }
}

public enum MessageRole
{
    User,
    Assistant
}
