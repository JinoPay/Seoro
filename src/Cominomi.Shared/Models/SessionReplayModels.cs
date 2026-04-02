using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

public class SessionReplaySummary
{
    public bool IsLive { get; set; }
    public DateTime? FirstTimestamp { get; set; }
    public DateTime? LastTimestamp { get; set; }
    public double ModifiedAtUnix { get; set; }
    public int EntryCount { get; set; }
    public int MessageCount { get; set; }
    public int ToolCallCount { get; set; }
    public required string FilePath { get; init; }
    public required string Id { get; init; }
    public string ProjectHash { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string? FirstMessage { get; set; }

    public TimeSpan? Duration => FirstTimestamp != null && LastTimestamp != null
        ? LastTimestamp.Value - FirstTimestamp.Value
        : null;
}

public class SessionListResult
{
    public bool HasMore { get; set; }
    public int Total { get; set; }
    public List<SessionReplaySummary> Sessions { get; set; } = [];
}

public class SessionReplayEvent
{
    public bool IsError { get; set; }
    public DateTime? Timestamp { get; set; }
    public List<ToolCallInfo>? ToolCalls { get; set; }
    public string Content { get; set; } = "";
    public required string Type { get; init; }
    public string? ToolName { get; set; }
}

public class ToolCallInfo
{
    public string InputPreview { get; set; } = "";
    public string Name { get; set; } = "";
}

public class SessionLoadResult
{
    public bool HasMore { get; set; }
    public int Total { get; set; }
    public List<SessionReplayEvent> Events { get; set; } = [];
}

public class SessionSearchResult
{
    public DateTime? Timestamp { get; set; }
    public string EventType { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Snippet { get; set; } = "";
}

public class SessionTagsData
{
    public Dictionary<string, List<string>> Tags { get; set; } = new();
    public Dictionary<string, string> Notes { get; set; } = new();
}

public class LiveSessionInfo
{
    public long ModifiedSecondsAgo { get; set; }
    public string FilePath { get; set; } = "";
    public string ProjectPath { get; set; } = "";
}

public class SessionIndex
{
    [JsonPropertyName("entries")] public Dictionary<string, SessionIndexEntry> Entries { get; set; } = new();

    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    [JsonPropertyName("lastComputedDate")] public string LastComputedDate { get; set; } = "";
}

public class SessionIndexEntry
{
    [JsonPropertyName("isEstimated")] public bool IsEstimated { get; set; }

    [JsonPropertyName("fileLastWriteUtc")] public DateTime FileLastWriteUtc { get; set; }

    [JsonPropertyName("firstTimestamp")] public DateTime? FirstTimestamp { get; set; }

    [JsonPropertyName("lastTimestamp")] public DateTime? LastTimestamp { get; set; }

    [JsonPropertyName("entryCount")] public int EntryCount { get; set; }

    [JsonPropertyName("toolCallCount")] public int ToolCallCount { get; set; }

    [JsonPropertyName("userMessageCount")] public int UserMessageCount { get; set; }

    [JsonPropertyName("fileSizeBytes")] public long FileSizeBytes { get; set; }

    [JsonPropertyName("filePath")] public string FilePath { get; set; } = "";

    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("projectHash")] public string ProjectHash { get; set; } = "";

    [JsonPropertyName("projectPath")] public string ProjectPath { get; set; } = "";

    [JsonPropertyName("firstMessage")] public string? FirstMessage { get; set; }
}