using System.Text.Json.Serialization;

namespace Seoro.Shared.Models.Gamification;

/// <summary>
///     ~/.claude/stats-cache.json을 위한 역직렬화 모델
///     (Claude CLI의 외부 통계 인덱서에 의해 생성됨).
/// </summary>
public class StatsCache
{
    [JsonPropertyName("firstSessionDate")] public DateTime? FirstSessionDate { get; set; }

    [JsonPropertyName("hourCounts")] public Dictionary<string, int> HourCounts { get; set; } = new();

    [JsonPropertyName("modelUsage")] public Dictionary<string, StatsCacheModelUsage> ModelUsage { get; set; } = new();

    [JsonPropertyName("totalMessages")] public int TotalMessages { get; set; }

    [JsonPropertyName("totalSessions")] public int TotalSessions { get; set; }

    [JsonPropertyName("version")] public int Version { get; set; }

    [JsonPropertyName("dailyActivity")] public List<StatsCacheDailyActivity> DailyActivity { get; set; } = [];

    [JsonPropertyName("dailyModelTokens")] public List<StatsCacheDailyModelTokens> DailyModelTokens { get; set; } = [];

    [JsonPropertyName("longestSession")] public StatsCacheLongestSession? LongestSession { get; set; }

    [JsonPropertyName("lastComputedDate")] public string LastComputedDate { get; set; } = "";
}

public class StatsCacheDailyActivity
{
    [JsonPropertyName("messageCount")] public int MessageCount { get; set; }

    [JsonPropertyName("sessionCount")] public int SessionCount { get; set; }

    [JsonPropertyName("toolCallCount")] public int ToolCallCount { get; set; }

    [JsonPropertyName("date")] public string Date { get; set; } = "";
}

public class StatsCacheDailyModelTokens
{
    [JsonPropertyName("tokensByModel")]
    public Dictionary<string, DailyModelTokenBreakdown> TokensByModel { get; set; } = new();

    [JsonPropertyName("date")] public string Date { get; set; } = "";
}

public class DailyModelTokenBreakdown
{
    [JsonPropertyName("cacheCreation")] public long CacheCreationInputTokens { get; set; }

    [JsonPropertyName("cacheRead")] public long CacheReadInputTokens { get; set; }

    [JsonPropertyName("input")] public long InputTokens { get; set; }

    [JsonPropertyName("output")] public long OutputTokens { get; set; }

    [JsonIgnore] public long Total => InputTokens + OutputTokens + CacheReadInputTokens + CacheCreationInputTokens;
}

public class StatsCacheModelUsage
{
    [JsonPropertyName("cacheCreationInputTokens")]
    public long CacheCreationInputTokens { get; set; }

    [JsonPropertyName("cacheReadInputTokens")]
    public long CacheReadInputTokens { get; set; }

    [JsonPropertyName("inputTokens")] public long InputTokens { get; set; }

    [JsonPropertyName("outputTokens")] public long OutputTokens { get; set; }
}

/// <summary>
///     Live activity stats computed from ~/.claude/history.jsonl
/// </summary>
public class LiveActivityStats
{
    public Dictionary<string, int> HourCounts { get; set; } = new();
    public int TotalMessages { get; set; }
    public int TotalSessions { get; set; }
    public List<LiveDailyActivity> DailyActivity { get; set; } = [];
    public string FirstSessionDate { get; set; } = "";
    public string LastSessionDate { get; set; } = "";
}

public class LiveDailyActivity
{
    public int MessageCount { get; set; }
    public int SessionCount { get; set; }
    public int ToolCallCount { get; set; }
    public string Date { get; set; } = "";
}

public class StatsCacheLongestSession
{
    [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }

    [JsonPropertyName("messageCount")] public int MessageCount { get; set; }

    [JsonPropertyName("duration")] public long Duration { get; set; }

    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
}