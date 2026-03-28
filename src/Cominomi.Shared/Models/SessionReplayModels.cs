namespace Cominomi.Shared.Models;

public class SessionReplaySummary
{
    public required string Id { get; init; }
    public required string FilePath { get; init; }
    public string ProjectHash { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public int EntryCount { get; set; }
    public int MessageCount { get; set; }
    public int ToolCallCount { get; set; }
    public DateTime? FirstTimestamp { get; set; }
    public DateTime? LastTimestamp { get; set; }
    public string? FirstMessage { get; set; }
    public double ModifiedAtUnix { get; set; }
    public bool IsLive { get; set; }

    public TimeSpan? Duration => FirstTimestamp != null && LastTimestamp != null
        ? LastTimestamp.Value - FirstTimestamp.Value
        : null;
}

public class SessionListResult
{
    public List<SessionReplaySummary> Sessions { get; set; } = [];
    public int Total { get; set; }
    public bool HasMore { get; set; }
}

public class SessionReplayEvent
{
    public required string Type { get; init; }
    public DateTime? Timestamp { get; set; }
    public string Content { get; set; } = "";
    public string? ToolName { get; set; }
    public List<ToolCallInfo>? ToolCalls { get; set; }
    public bool IsError { get; set; }
}

public class ToolCallInfo
{
    public string Name { get; set; } = "";
    public string InputPreview { get; set; } = "";
}

public class SessionLoadResult
{
    public List<SessionReplayEvent> Events { get; set; } = [];
    public int Total { get; set; }
    public bool HasMore { get; set; }
}

public class SessionSearchResult
{
    public string SessionId { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Snippet { get; set; } = "";
    public DateTime? Timestamp { get; set; }
    public string EventType { get; set; } = "";
}

public class SessionTagsData
{
    public Dictionary<string, List<string>> Tags { get; set; } = new();
    public Dictionary<string, string> Notes { get; set; } = new();
}

public class LiveSessionInfo
{
    public string FilePath { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public long ModifiedSecondsAgo { get; set; }
}
