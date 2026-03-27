using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class GamificationService : IGamificationService
{
    private readonly IUsageService _usageService;
    private readonly ISessionService _sessionService;
    private readonly IClaudeSettingsService _claudeSettings;
    private readonly ILogger<GamificationService> _logger;

    public GamificationService(
        IUsageService usageService,
        ISessionService sessionService,
        IClaudeSettingsService claudeSettings,
        ILogger<GamificationService> logger)
    {
        _usageService = usageService;
        _sessionService = sessionService;
        _claudeSettings = claudeSettings;
        _logger = logger;
    }

    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        var stats = new DashboardStats();

        try
        {
            // Gather usage stats (all time)
            var usageStats = await _usageService.GetStatsAsync(null);
            stats.TotalSessions = usageStats.TotalSessions;

            // Gather session data for messages count
            var sessions = await _sessionService.GetSessionsAsync();
            stats.TotalMessages = sessions.Sum(s => s.Messages.Count);

            // Build daily activity from ByDate
            var dailyCounts = new Dictionary<string, int>();
            foreach (var day in usageStats.ByDate)
            {
                dailyCounts[day.Date] = (int)(day.TotalTokens / 1000); // approximate activity level
            }
            stats.DailyActivity = dailyCounts;
            stats.DaysActive = dailyCounts.Count;

            // Calculate streak
            stats.Streak = CalculateStreak(dailyCounts.Keys.Select(DateOnly.Parse).ToList());

            // Calculate XP and level
            var settings = await _claudeSettings.ReadAsync(ClaudeSettingsScope.Global);
            int xp = CalculateXp(settings, dailyCounts.Count);
            stats.Level = CalculateLevel(xp);

            // Config completeness
            stats.ConfigCompleteness = CalculateConfigCompleteness(settings);

            // Achievements
            stats.Achievements = EvaluateAchievements(stats, settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error computing dashboard stats");
        }

        return stats;
    }

    private static int CalculateXp(ClaudeSettings settings, int activeDays)
    {
        int xp = 0;
        // Settings configured
        if (settings.Model != null || settings.EffortLevel != null) xp += 50;
        // Hooks
        if (settings.Hooks != null)
            xp += settings.Hooks.Values.Sum(configs => configs.Sum(c => c.Hooks.Count)) * 20;
        // MCP servers
        if (settings.McpServers != null) xp += settings.McpServers.Count * 30;
        // Active days
        xp += activeDays * 5;
        return xp;
    }

    private static UserLevel CalculateLevel(int xp)
    {
        var level = new UserLevel { Xp = xp };
        for (int i = UserLevel.Thresholds.Length - 1; i >= 0; i--)
        {
            if (xp >= UserLevel.Thresholds[i])
            {
                level.Level = i + 1;
                level.Name = UserLevel.Names[Math.Min(i, UserLevel.Names.Length - 1)];
                level.XpForNext = i < UserLevel.Thresholds.Length - 1
                    ? UserLevel.Thresholds[i + 1] : UserLevel.Thresholds[^1];
                level.Progress = level.XpForNext > UserLevel.Thresholds[i]
                    ? (double)(xp - UserLevel.Thresholds[i]) / (level.XpForNext - UserLevel.Thresholds[i])
                    : 1.0;
                break;
            }
        }
        return level;
    }

    private static StreakInfo CalculateStreak(List<DateOnly> activeDates)
    {
        if (activeDates.Count == 0) return new StreakInfo();

        var sorted = activeDates.OrderDescending().ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        int current = 0;
        var checkDate = today;
        foreach (var date in sorted)
        {
            if (date == checkDate || date == checkDate.AddDays(-1))
            {
                current++;
                checkDate = date;
            }
            else if (date < checkDate.AddDays(-1)) break;
        }

        // Longest streak
        int longest = 0, streak = 1;
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i - 1].DayNumber - sorted[i].DayNumber == 1)
                streak++;
            else
            {
                longest = Math.Max(longest, streak);
                streak = 1;
            }
        }
        longest = Math.Max(longest, streak);

        return new StreakInfo
        {
            CurrentStreak = current,
            LongestStreak = longest,
            LastActiveDate = sorted.Count > 0 ? sorted[0].ToDateTime(TimeOnly.MinValue) : null
        };
    }

    private static double CalculateConfigCompleteness(ClaudeSettings settings)
    {
        int filled = 0, total = 6;
        if (settings.Model != null) filled++;
        if (settings.EffortLevel != null) filled++;
        if (settings.DefaultMode != null) filled++;
        if (settings.Permissions is { Allow.Count: > 0 } or { Deny.Count: > 0 }) filled++;
        if (settings.Hooks is { Count: > 0 }) filled++;
        if (settings.McpServers is { Count: > 0 }) filled++;
        return (double)filled / total;
    }

    private static List<Achievement> EvaluateAchievements(DashboardStats stats, ClaudeSettings settings)
    {
        var achievements = new List<Achievement>
        {
            // Config
            A("cfg-configured", "Configured", "기본 설정 완료", "tune", AchievementCategory.Config,
                settings.Model != null || settings.EffortLevel != null),
            A("cfg-hooks", "Hook Master", "첫 훅 등록", "bolt", AchievementCategory.Config,
                settings.Hooks is { Count: > 0 }),
            A("cfg-mcp", "Connected", "MCP 서버 연결", "hub", AchievementCategory.Config,
                settings.McpServers is { Count: > 0 }),
            A("cfg-permissions", "Gatekeeper", "권한 규칙 설정", "security", AchievementCategory.Config,
                settings.Permissions is { Allow.Count: > 0 } or { Deny.Count: > 0 }),
            A("cfg-env", "Environmentalist", "환경변수 설정", "terminal", AchievementCategory.Config,
                settings.Env is { Count: > 0 }),

            // Usage
            A("use-100", "Getting Started", "100 메시지 달성", "chat", AchievementCategory.Usage,
                stats.TotalMessages >= 100),
            A("use-1k", "Conversationalist", "1,000 메시지 달성", "forum", AchievementCategory.Usage,
                stats.TotalMessages >= 1000),
            A("use-10k", "Power User", "10,000 메시지 달성", "whatshot", AchievementCategory.Usage,
                stats.TotalMessages >= 10000),
            A("use-sessions", "Centurion", "100 세션 달성", "layers", AchievementCategory.Usage,
                stats.TotalSessions >= 100),

            // Streak
            A("streak-3", "Hat Trick", "3일 연속 사용", "local_fire_department", AchievementCategory.Streak,
                stats.Streak.LongestStreak >= 3),
            A("streak-7", "Week Warrior", "7일 연속 사용", "military_tech", AchievementCategory.Streak,
                stats.Streak.LongestStreak >= 7),
            A("streak-14", "Fortnight Force", "14일 연속 사용", "shield", AchievementCategory.Streak,
                stats.Streak.LongestStreak >= 14),
            A("streak-30", "Monthly Master", "30일 연속 사용", "emoji_events", AchievementCategory.Streak,
                stats.Streak.LongestStreak >= 30),

            // Mastery
            A("mastery-10d", "Devoted", "10일 활동", "favorite", AchievementCategory.Mastery,
                stats.DaysActive >= 10),
            A("mastery-30d", "Dedicated", "30일 활동", "star", AchievementCategory.Mastery,
                stats.DaysActive >= 30),
        };
        return achievements;
    }

    private static Achievement A(string id, string name, string desc, string icon,
        AchievementCategory cat, bool unlocked) => new()
    {
        Id = id, Name = name, Description = desc, Icon = icon,
        Category = cat, Unlocked = unlocked
    };
}
