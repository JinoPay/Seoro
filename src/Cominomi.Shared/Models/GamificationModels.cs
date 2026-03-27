namespace Cominomi.Shared.Models;

public class UserLevel
{
    public int Level { get; set; } = 1;
    public string Name { get; set; } = "Newcomer";
    public int Xp { get; set; }
    public int XpForNext { get; set; }
    public double Progress { get; set; }

    public static readonly int[] Thresholds = [0, 100, 300, 600, 1000, 1500, 2500, 4000, 6000, 10000];
    public static readonly string[] Names =
        ["Newcomer", "Explorer", "Builder", "Architect", "Specialist",
         "Expert", "Master", "Virtuoso", "Luminary", "Transcendent"];
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
    public bool Unlocked { get; set; }
}

public enum AchievementCategory { Config, Usage, Streak, Mastery }

public class DashboardStats
{
    public int TotalSessions { get; set; }
    public int TotalMessages { get; set; }
    public int TotalToolCalls { get; set; }
    public int DaysActive { get; set; }
    public UserLevel Level { get; set; } = new();
    public StreakInfo Streak { get; set; } = new();
    public List<Achievement> Achievements { get; set; } = [];
    public double ConfigCompleteness { get; set; }
    public Dictionary<string, int> DailyActivity { get; set; } = new(); // "yyyy-MM-dd" -> count
}
