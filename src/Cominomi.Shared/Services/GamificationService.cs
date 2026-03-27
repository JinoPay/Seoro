using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class GamificationService : IGamificationService
{
    private readonly IUsageService _usageService;
    private readonly ISessionService _sessionService;
    private readonly ISessionReplayService _replayService;
    private readonly IClaudeSettingsService _claudeSettings;
    private readonly ILogger<GamificationService> _logger;

    public GamificationService(
        IUsageService usageService,
        ISessionService sessionService,
        ISessionReplayService replayService,
        IClaudeSettingsService claudeSettings,
        ILogger<GamificationService> logger)
    {
        _usageService = usageService;
        _sessionService = sessionService;
        _replayService = replayService;
        _claudeSettings = claudeSettings;
        _logger = logger;
    }

    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        var stats = new DashboardStats();

        try
        {
            // Gather replay sessions for daily activity breakdown
            var replaySessions = await _replayService.ListSessionsAsync();

            // Build daily activity from replay sessions
            var dailyMap = new Dictionary<string, DailyActivityEntry>();
            foreach (var s in replaySessions)
            {
                var dateKey = s.FirstTimestamp?.ToString("yyyy-MM-dd") ?? "";
                if (string.IsNullOrEmpty(dateKey)) continue;

                if (!dailyMap.TryGetValue(dateKey, out var entry))
                {
                    entry = new DailyActivityEntry { Date = dateKey };
                    dailyMap[dateKey] = entry;
                }
                entry.MessageCount += s.MessageCount;
                entry.SessionCount++;
                entry.ToolCallCount += s.ToolCallCount;
            }
            stats.DailyActivity = dailyMap.Values.OrderBy(d => d.Date).ToList();
            stats.TotalSessions = replaySessions.Count;
            stats.TotalMessages = replaySessions.Sum(s => s.MessageCount);
            stats.TotalToolCalls = replaySessions.Sum(s => s.ToolCallCount);
            stats.DaysActive = dailyMap.Count;

            // Calculate streak
            var activeDates = dailyMap.Keys
                .Where(k => dailyMap[k].MessageCount > 0)
                .Select(DateOnly.Parse).ToList();
            stats.Streak = CalculateStreak(activeDates);

            // Calculate XP and level
            var settings = await _claudeSettings.ReadAsync(ClaudeSettingsScope.Global);
            int xp = CalculateXp(settings, stats.DaysActive);
            stats.Level = CalculateLevel(xp);

            // Config completeness
            stats.ConfigCompleteness = CalculateConfigCompleteness(settings);

            // Achievements
            stats.Achievements = EvaluateAchievements(stats, settings);

            // Cost summary
            stats.Cost = await CalculateCostSummaryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error computing dashboard stats");
        }

        return stats;
    }

    private async Task<CostSummary> CalculateCostSummaryAsync()
    {
        var now = DateTime.UtcNow;
        var todayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var todayStats = await _usageService.GetStatsByDateRangeAsync(todayStart, now);
        var monthStats = await _usageService.GetStatsByDateRangeAsync(monthStart, now);

        var dayOfMonth = now.Day;
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var projection = dayOfMonth > 0
            ? monthStats.TotalCost / dayOfMonth * daysInMonth
            : 0m;

        return new CostSummary
        {
            Today = todayStats.TotalCost,
            ThisMonth = monthStats.TotalCost,
            MonthlyProjection = projection,
        };
    }

    private static int CalculateXp(ClaudeSettings settings, int activeDays)
    {
        int xp = 0;
        if (settings.Model != null || settings.EffortLevel != null) xp += 50;
        if (settings.Hooks != null)
            xp += settings.Hooks.Values.Sum(configs => configs.Sum(c => c.Hooks.Count)) * 20;
        if (settings.McpServers != null) xp += settings.McpServers.Count * 30;
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

    private static int GetHookCount(ClaudeSettings settings)
        => settings.Hooks?.Values.Sum(configs => configs.Sum(c => c.Hooks.Count)) ?? 0;

    private static List<Achievement> EvaluateAchievements(DashboardStats stats, ClaudeSettings settings)
    {
        var hookCount = GetHookCount(settings);
        var mcpCount = settings.McpServers?.Count ?? 0;

        return
        [
            // 설정
            A("cfg-configured", "설정 완료", "기본 설정 완료", "tune", AchievementCategory.Config,
                settings.Model != null || settings.EffortLevel != null),
            A("cfg-hooks", "훅 마스터", "5개 이상 훅 이벤트 등록", "bolt", AchievementCategory.Config,
                hookCount >= 5),
            A("cfg-mcp", "첫 연결", "MCP 서버 연결", "hub", AchievementCategory.Config,
                mcpCount > 0),
            A("cfg-mcp-network", "네트워크 구축", "MCP 서버 3개 이상 연결", "device_hub", AchievementCategory.Config,
                mcpCount >= 3),
            A("cfg-permissions", "문지기", "권한 규칙 설정", "security", AchievementCategory.Config,
                settings.Permissions is { Allow.Count: > 0 } or { Deny.Count: > 0 }),
            A("cfg-env", "환경 설정자", "환경변수 설정", "terminal", AchievementCategory.Config,
                settings.Env is { Count: > 0 }),

            // 사용량
            A("use-100", "첫 발걸음", "100 메시지 달성", "chat", AchievementCategory.Usage,
                stats.TotalMessages >= 100),
            A("use-1k", "수다쟁이", "1,000 메시지 달성", "forum", AchievementCategory.Usage,
                stats.TotalMessages >= 1000),
            A("use-10k", "파워 유저", "10,000 메시지 달성", "whatshot", AchievementCategory.Usage,
                stats.TotalMessages >= 10000),
            A("use-100k", "멈출 수 없는", "100,000 메시지 달성", "rocket_launch", AchievementCategory.Usage,
                stats.TotalMessages >= 100000),
            A("use-sessions", "백전노장", "100 세션 달성", "layers", AchievementCategory.Usage,
                stats.TotalSessions >= 100),
            A("use-tools", "도구의 달인", "10,000 도구 호출 달성", "build", AchievementCategory.Usage,
                stats.TotalToolCalls >= 10000),

            // 연속 기록
            A("streak-3", "해트트릭", "3일 연속 사용", "local_fire_department", AchievementCategory.Streak,
                stats.Streak.LongestStreak >= 3),
            A("streak-7", "주간 전사", "7일 연속 사용", "military_tech", AchievementCategory.Streak,
                stats.Streak.LongestStreak >= 7),
            A("streak-14", "2주의 힘", "14일 연속 사용", "shield", AchievementCategory.Streak,
                stats.Streak.LongestStreak >= 14),
            A("streak-30", "월간 마스터", "30일 연속 사용", "emoji_events", AchievementCategory.Streak,
                stats.Streak.LongestStreak >= 30),

            // 숙련
            A("mastery-10d", "열정가", "10일 활동", "favorite", AchievementCategory.Mastery,
                stats.DaysActive >= 10),
            A("mastery-30d", "헌신자", "30일 활동", "star", AchievementCategory.Mastery,
                stats.DaysActive >= 30),
            A("mastery-hook", "자동화 장인", "첫 훅 등록", "settings", AchievementCategory.Mastery,
                hookCount >= 1),
        ];
    }

    private static Achievement A(string id, string name, string desc, string icon,
        AchievementCategory cat, bool unlocked) => new()
    {
        Id = id, Name = name, Description = desc, Icon = icon,
        Category = cat, Unlocked = unlocked
    };
}
