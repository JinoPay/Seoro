using Seoro.Shared.Resources;

namespace Seoro.Shared.Models.Gamification;

public class UserLevel
{
    // 15 레벨 (이전: 10)
    public static readonly int[] Thresholds =
        [0, 100, 300, 600, 1000, 1500, 2500, 4000, 6000, 10000, 15000, 22000, 32000, 50000, 80000];

    public double Progress { get; set; }
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public int XpForNext { get; set; }
    public string Name { get; set; } = Strings.GetLevelName(1);
}

public class StreakInfo
{
    public DateTime? LastActiveDate { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
}

public class Achievement
{
    public required AchievementCategory Category { get; init; }
    public AchievementRarity Rarity { get; init; } = AchievementRarity.Common;
    public bool Unlocked { get; set; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public enum AchievementCategory
{
    Config,
    Usage,
    Streak,
    Mastery,
    Explorer,
    Efficiency,
    Time,
    Economy,
    Pattern
}

public enum AchievementRarity
{
    Common,
    Rare,
    Epic,
    Legendary
}

public class DailyActivityEntry
{
    public int MessageCount { get; set; }
    public int SessionCount { get; set; }
    public int ToolCallCount { get; set; }
    public string Date { get; set; } = "";
}

public class CostSummary
{
    public bool DailyExceeded { get; set; }
    public bool MonthlyExceeded { get; set; }
    public decimal MonthlyProjection { get; set; }
    public decimal ThisMonth { get; set; }
    public decimal Today { get; set; }
    public decimal? DailyLimit { get; set; }
    public decimal? MonthlyLimit { get; set; }
    public List<decimal> Last7Days { get; set; } = [];
}

public class ConfigItem
{
    public bool IsConfigured { get; set; }
    public string Name { get; set; } = "";
}

public record SessionIndexStats(
    int TotalSessions,
    int TotalMessages,
    int TotalToolCalls,
    int DaysActive,
    int TotalProjects,
    List<DailyActivityEntry> DailyActivity,
    int[] HourCounts,
    int NightSessions,
    int MorningSessions,
    long LongestSessionMs);

public class DashboardStats
{
    public CostSummary Cost { get; set; } = new();
    public decimal EstimatedCacheSavings { get; set; }
    public double CacheHitRate { get; set; }
    public double ConfigCompleteness { get; set; }
    public int DaysActive { get; set; }
    public int MorningSessionCount { get; set; } // 05:00-09:00

    // Extended stats for achievements
    public int NightSessionCount { get; set; } // 22:00-04:00
    public int TotalMessages { get; set; }
    public int TotalProjects { get; set; }
    public int TotalSessions { get; set; }
    public int TotalToolCalls { get; set; }

    // Hourly distribution (24-element array, from all sessions)
    public int[] HourCounts { get; set; } = new int[24];

    // Economy stats (from UsageStats via StatsCacheService)
    public long TotalTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public decimal TotalCostUsd { get; set; }
    public int DistinctModelsUsed { get; set; }

    // Pattern stats (derived from DailyActivity/HourCounts)
    public int WeekendDaysActive { get; set; }
    public int PeakDayMessages { get; set; }
    public int ActiveHoursCount { get; set; }

    public List<Achievement> Achievements { get; set; } = [];
    public List<ConfigItem> ConfigItems { get; set; } = [];
    public List<DailyActivityEntry> DailyActivity { get; set; } = [];
    public long LongestSessionMs { get; set; }
    public StreakInfo Streak { get; set; } = new();
    public UserLevel Level { get; set; } = new();
}