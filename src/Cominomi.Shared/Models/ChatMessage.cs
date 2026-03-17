namespace Cominomi.Shared.Models;

public enum ContentPartType
{
    Text,
    ToolCall,
    Thinking
}

public class ContentPart
{
    public ContentPartType Type { get; set; }
    public string? Text { get; set; }
    public ToolCall? ToolCall { get; set; }
}

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public MessageRole Role { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<ToolCall> ToolCalls { get; set; } = [];
    public List<ContentPart> Parts { get; set; } = [];
    public List<FileAttachment> Attachments { get; set; } = [];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsStreaming { get; set; }
    public DateTime? StreamingStartedAt { get; set; }
    public DateTime? StreamingFinishedAt { get; set; }
    public TimeSpan? Duration => StreamingStartedAt.HasValue && StreamingFinishedAt.HasValue
        ? StreamingFinishedAt.Value - StreamingStartedAt.Value
        : null;

    /// <summary>
    /// Migrates old messages (Text + ToolCalls) to the Parts list for interleaved rendering.
    /// </summary>
    public void MigrateToParts()
    {
        if (Parts.Count > 0) return;

        foreach (var tc in ToolCalls)
            Parts.Add(new ContentPart { Type = ContentPartType.ToolCall, ToolCall = tc });

        if (!string.IsNullOrEmpty(Text))
            Parts.Add(new ContentPart { Type = ContentPartType.Text, Text = Text });
    }
}

public enum MessageRole
{
    User,
    Assistant,
    System
}
