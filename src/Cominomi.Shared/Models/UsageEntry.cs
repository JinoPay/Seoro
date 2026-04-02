namespace Cominomi.Shared.Models;

public class UsageStats
{
    public DateTime? FirstSessionDate { get; set; }
    public decimal TotalCost { get; set; }
    public int TotalMessages { get; set; }
    public int TotalSessions { get; set; }
    public int[] HourCounts { get; set; } = new int[24];
    public List<DailyActivityEntry> DailyActivity { get; set; } = [];
    public List<DailyTokenTrend> DailyTokenTrend { get; set; } = [];
    public List<DailyUsage> ByDate { get; set; } = [];
    public List<ModelUsage> ByModel { get; set; } = [];
    public List<ProjectUsage> ByProject { get; set; } = [];
    public long TotalCacheCreationTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalTokens { get; set; }
    public LongestSessionInfo? LongestSession { get; set; }
}

public class ModelUsage
{
    public decimal TotalCost { get; set; }
    public double Percentage { get; set; }
    public int SessionCount { get; set; }
    public long CacheCreationTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens { get; set; }
    public string Model { get; set; } = string.Empty;
}

public class DailyTokenTrend
{
    public decimal DailyCost { get; set; }
    public Dictionary<string, long> TokensByModel { get; set; } = new();
    public long TotalTokens { get; set; }
    public string Date { get; set; } = string.Empty;
}

public class LongestSessionInfo
{
    public DateTime Timestamp { get; set; }
    public int MessageCount { get; set; }
    public long DurationMs { get; set; }
    public string SessionId { get; set; } = string.Empty;
}

public class DailyUsage
{
    public decimal TotalCost { get; set; }
    public List<string> ModelsUsed { get; set; } = [];
    public long TotalTokens { get; set; }
    public string Date { get; set; } = string.Empty;
}

public class ProjectUsage
{
    public decimal TotalCost { get; set; }
    public int SessionCount { get; set; }
    public long TotalTokens { get; set; }
    public string LastUsed { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
}