namespace Cominomi.Shared.Models;

public class SessionReplaySummary
{
    public required string Id { get; init; }
    public required string FilePath { get; init; }
    public string? ProjectPath { get; set; }
    public int EntryCount { get; set; }
    public int MessageCount { get; set; }
    public int ToolCallCount { get; set; }
    public DateTime? FirstTimestamp { get; set; }
    public DateTime? LastTimestamp { get; set; }
    public string? FirstMessage { get; set; }
}

public class SessionReplayEvent
{
    public required string Type { get; init; } // "user", "assistant", "tool_use", "tool_result"
    public DateTime? Timestamp { get; set; }
    public string Content { get; set; } = "";
    public string? ToolName { get; set; }
}
