namespace Cominomi.Shared.Models;

public class UserLevel
{
    public int Level { get; set; } = 1;
    public string Name { get; set; } = "새내기";
    public int Xp { get; set; }
    public int XpForNext { get; set; }
    public double Progress { get; set; }

    // 15 levels (was 10)
    public static readonly int[] Thresholds =
        [0, 100, 300, 600, 1000, 1500, 2500, 4000, 6000, 10000, 15000, 22000, 32000, 50000, 80000];
    public static readonly string[] Names =
        ["새내기", "탐험가", "건축가", "설계자", "전문가",
         "숙련자", "달인", "거장", "현자", "초월자",
         "전설", "신화", "불멸자", "태초의 자", "조물주"];
}

public class StreakInfo
{
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateTime? LastActiveDate { get; set; }
}

public class Achievement
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public required AchievementCategory Category { get; init; }
    public AchievementRarity Rarity { get; init; } = AchievementRarity.Common;
    public bool Unlocked { get; set; }
}

public enum AchievementCategory { Config, Usage, Streak, Mastery, Explorer, Efficiency, Time }

public enum AchievementRarity { Common, Rare, Epic, Legendary }

public class DailyActivityEntry
{
    public string Date { get; set; } = "";
    public int MessageCount { get; set; }
    public int SessionCount { get; set; }
    public int ToolCallCount { get; set; }
}

public class CostSummary
{
    public decimal Today { get; set; }
    public decimal ThisMonth { get; set; }
    public decimal? DailyLimit { get; set; }
    public decimal? MonthlyLimit { get; set; }
    public bool DailyExceeded { get; set; }
    public bool MonthlyExceeded { get; set; }
    public decimal MonthlyProjection { get; set; }
}

public class DashboardStats
{
    public int TotalSessions { get; set; }
    public int TotalMessages { get; set; }
    public int TotalToolCalls { get; set; }
    public int DaysActive { get; set; }
    public int TotalProjects { get; set; }
    public UserLevel Level { get; set; } = new();
    public StreakInfo Streak { get; set; } = new();
    public List<Achievement> Achievements { get; set; } = [];
    public double ConfigCompleteness { get; set; }
    public List<DailyActivityEntry> DailyActivity { get; set; } = [];
    public CostSummary Cost { get; set; } = new();

    // Hourly distribution (24-element array, from all sessions)
    public int[] HourCounts { get; set; } = new int[24];

    // Extended stats for achievements
    public int NightSessionCount { get; set; }    // 22:00-04:00
    public int MorningSessionCount { get; set; }  // 05:00-09:00
    public long LongestSessionMs { get; set; }
    public double CacheHitRate { get; set; }
    public decimal EstimatedCacheSavings { get; set; }
}
