namespace Cominomi.Shared.Models;

public class ActionTimelineEntry
{
    public string ToolCallId { get; init; } = "";
    public string ToolName { get; init; } = "";
    public string HeaderLabel { get; init; } = "";
    public string? ResultHint { get; init; }
    public bool IsError { get; init; }
    public DateTime Timestamp { get; init; }
    public string SessionId { get; init; } = "";
    public string SessionTitle { get; init; } = "";
    public string WorkspaceId { get; init; } = "";
    public string WorkspaceName { get; init; } = "";
}

public class ActionDateGroup
{
    public string Label { get; init; } = "";
    public List<ActionTimelineEntry> Entries { get; init; } = [];
}

public record ActionTimelineFilter
{
    public string? WorkspaceId { get; init; }
    public string? SessionId { get; init; }
    public int DaysBack { get; init; } = 7;
    public int MaxEntries { get; init; } = 200;
    public DateTime? Before { get; init; }
}

public class ActionTimelineResult
{
    public List<ActionDateGroup> Groups { get; init; } = [];
    public int TotalEntriesLoaded { get; init; }
    public DateTime? OldestTimestamp { get; init; }
    public bool HasMore { get; init; }
}
