namespace Cominomi.Shared.Models;

public enum ContentPartType
{
    Text,
    ToolCall,
    Thinking
}

public class ContentPart
{
    public ContentPartType Type { get; init; }
    public string? Text { get; set; }
    public ToolCall? ToolCall { get; init; }
}

public class ChatMessage
{
    public bool IsStreaming { get; set; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public DateTime? StreamingFinishedAt { get; set; }
    public DateTime? StreamingStartedAt { get; set; }
    public List<ContentPart> Parts { get; init; } = [];
    public List<FileAttachment> Attachments { get; init; } = [];
    public List<ToolCall> ToolCalls { get; init; } = [];
    public MessageRole Role { get; init; }
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;

    public TimeSpan? Duration => StreamingStartedAt.HasValue && StreamingFinishedAt.HasValue
        ? StreamingFinishedAt.Value - StreamingStartedAt.Value
        : null;

    /// <summary>
    ///     Migrates old messages (Text + ToolCalls) to the Parts list for interleaved rendering.
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