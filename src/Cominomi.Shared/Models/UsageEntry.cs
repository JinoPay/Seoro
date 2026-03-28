namespace Cominomi.Shared.Models;

public class UsageStats
{
    public decimal TotalCost { get; set; }
    public long TotalTokens { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheCreationTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public int TotalSessions { get; set; }
    public int TotalMessages { get; set; }
    public int[] HourCounts { get; set; } = new int[24];
    public List<DailyTokenTrend> DailyTokenTrend { get; set; } = [];
    public LongestSessionInfo? LongestSession { get; set; }
    public DateTime? FirstSessionDate { get; set; }
    public List<ModelUsage> ByModel { get; set; } = [];
    public List<DailyUsage> ByDate { get; set; } = [];
    public List<ProjectUsage> ByProject { get; set; } = [];
}

public class ModelUsage
{
    public string Model { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public long TotalTokens { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public int SessionCount { get; set; }
    public double Percentage { get; set; }
}

public class DailyTokenTrend
{
    public string Date { get; set; } = string.Empty;
    public long TotalTokens { get; set; }
    public Dictionary<string, long> TokensByModel { get; set; } = new();
}

public class LongestSessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public int MessageCount { get; set; }
    public DateTime Timestamp { get; set; }
}

public class DailyUsage
{
    public string Date { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public long TotalTokens { get; set; }
    public List<string> ModelsUsed { get; set; } = [];
}

public class ProjectUsage
{
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public long TotalTokens { get; set; }
    public int SessionCount { get; set; }
    public string LastUsed { get; set; } = string.Empty;
}
