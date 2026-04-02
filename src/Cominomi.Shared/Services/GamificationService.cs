using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class GamificationService(
    IStatsCacheService statsCacheService,
    ISessionReplayService replayService,
    IClaudeSettingsService claudeSettings,
    ILogger<GamificationService> logger)
    : IGamificationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private DashboardStats? _cachedStats;
    private DateTime _cachedAt;

    public async Task<DashboardStats> ForceRefreshDashboardAsync()
    {
        _cachedStats = null;
        return await GetDashboardStatsAsync();
    }

    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        if (_cachedStats != null && DateTime.UtcNow - _cachedAt < CacheTtl)
            return _cachedStats;
        var stats = new DashboardStats();

        try
        {
            // Ensure session index is up-to-date, then aggregate from index (no list allocation)
            await replayService.RefreshSessionIndexAsync();
            var indexStats = await replayService.GetIndexStatsAsync();

            stats.DailyActivity = indexStats.DailyActivity;
            stats.TotalSessions = indexStats.TotalSessions;
            stats.TotalMessages = indexStats.TotalMessages;
            stats.TotalToolCalls = indexStats.TotalToolCalls;
            stats.DaysActive = indexStats.DaysActive;
            stats.TotalProjects = indexStats.TotalProjects;
            stats.HourCounts = indexStats.HourCounts;
            stats.NightSessionCount = indexStats.NightSessions;
            stats.MorningSessionCount = indexStats.MorningSessions;
            stats.LongestSessionMs = indexStats.LongestSessionMs;

            // Cache stats from stats-cache.json
            var usageStats = await statsCacheService.GetMergedStatsAsync();
            var cacheTotal = usageStats.TotalCacheCreationTokens + usageStats.TotalCacheReadTokens;
            stats.CacheHitRate = cacheTotal > 0
                ? (double)usageStats.TotalCacheReadTokens / cacheTotal * 100
                : 0;

            // Cache savings
            decimal savings = 0;
            foreach (var m in usageStats.ByModel)
            {
                var pricing = ModelDefinitions.GetPricing(m.Model);
                if (pricing != null)
                    savings += (decimal)m.CacheReadTokens / 1_000_000m * (pricing.Input - pricing.CacheRead);
            }

            stats.EstimatedCacheSavings = savings;

            // Streak
            var activeDates = indexStats.DailyActivity
                .Where(d => d.MessageCount > 0)
                .Select(d => DateOnly.Parse(d.Date)).ToList();
            stats.Streak = CalculateStreak(activeDates);

            // XP and level
            var settings = await claudeSettings.ReadAsync(ClaudeSettingsScope.Global);
            var xp = CalculateXp(settings, stats);
            stats.Level = CalculateLevel(xp);

            // Config completeness
            var (completeness, configItems) = CalculateConfigCompleteness(settings);
            stats.ConfigCompleteness = completeness;
            stats.ConfigItems = configItems;

            // Achievements
            stats.Achievements = EvaluateAchievements(stats, settings);

            // Cost summary
            stats.Cost = await CalculateCostSummaryAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error computing dashboard stats");
        }

        _cachedStats = stats;
        _cachedAt = DateTime.UtcNow;
        return stats;
    }

    private static (double completeness, List<ConfigItem> items) CalculateConfigCompleteness(ClaudeSettings settings)
    {
        var items = new List<ConfigItem>
        {
            new() { Name = "모델", IsConfigured = settings.Model != null },
            new() { Name = "성능 수준", IsConfigured = settings.EffortLevel != null },
            new() { Name = "기본 모드", IsConfigured = settings.DefaultMode != null },
            new() { Name = "권한", IsConfigured = settings.Permissions is { Allow.Count: > 0 } or { Deny.Count: > 0 } },
            new() { Name = "훅", IsConfigured = settings.Hooks is { Count: > 0 } },
            new() { Name = "MCP 서버", IsConfigured = settings.McpServers is { Count: > 0 } }
        };
        var filled = items.Count(i => i.IsConfigured);
        return ((double)filled / items.Count, items);
    }

    private static Achievement A(string id, string name, string desc, string icon,
        AchievementCategory cat, AchievementRarity rarity, bool unlocked)
    {
        return new Achievement
        {
            Id = id, Name = name, Description = desc, Icon = icon,
            Category = cat, Rarity = rarity, Unlocked = unlocked
        };
    }

    /// <summary>
    ///     Expanded XP sources:
    ///     - Config: model/effort set (+50), hooks (+20/hook), MCP servers (+30/server)
    ///     - Activity: days active (+5/day), messages (+1 per 10, cap 500), tool calls (+1 per 20, cap 500)
    ///     - Sessions: +2/session (cap 300)
    ///     - Streaks: current streak * 10, longest streak * 5
    ///     - Projects: +15/project
    /// </summary>
    private static int CalculateXp(ClaudeSettings settings, DashboardStats stats)
    {
        var xp = 0;

        // Config XP
        if (settings.Model != null || settings.EffortLevel != null) xp += 50;
        if (settings.Hooks != null)
            xp += settings.Hooks.Values.Sum(configs => configs.Sum(c => c.Hooks.Count)) * 20;
        if (settings.McpServers != null) xp += settings.McpServers.Count * 30;
        if (settings.Env is { Count: > 0 }) xp += 25;
        if (settings.Permissions is { Allow.Count: > 0 } or { Deny.Count: > 0 }) xp += 25;

        // Activity XP
        xp += stats.DaysActive * 5;
        xp += Math.Min(stats.TotalMessages / 10, 500);
        xp += Math.Min(stats.TotalToolCalls / 20, 500);
        xp += Math.Min(stats.TotalSessions * 2, 300);

        // Streak XP
        xp += stats.Streak.CurrentStreak * 10;
        xp += stats.Streak.LongestStreak * 5;

        // Project diversity XP
        xp += stats.TotalProjects * 15;

        return xp;
    }

    private static int GetHookCount(ClaudeSettings settings)
    {
        return settings.Hooks?.Values.Sum(configs => configs.Sum(c => c.Hooks.Count)) ?? 0;
    }

    // ═══════════════════════════════════════════
    //  Achievements — 7 categories, 42 total
    // ═══════════════════════════════════════════

    private static List<Achievement> EvaluateAchievements(DashboardStats stats, ClaudeSettings settings)
    {
        var hookCount = GetHookCount(settings);
        var mcpCount = settings.McpServers?.Count ?? 0;

        return
        [
            // ── 설정 (Config) ── 8개
            A("cfg-configured", "설정 완료", "기본 설정(모델/성능) 완료", "tune",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.Model != null || settings.EffortLevel != null),
            A("cfg-hooks", "훅 마스터", "5개 이상 훅 이벤트 등록", "bolt",
                AchievementCategory.Config, AchievementRarity.Rare,
                hookCount >= 5),
            A("cfg-mcp", "첫 연결", "MCP 서버 1개 연결", "hub",
                AchievementCategory.Config, AchievementRarity.Common,
                mcpCount > 0),
            A("cfg-mcp-network", "네트워크 구축", "MCP 서버 3개 이상 연결", "device_hub",
                AchievementCategory.Config, AchievementRarity.Rare,
                mcpCount >= 3),
            A("cfg-mcp-kingdom", "MCP 왕국", "MCP 서버 5개 이상 연결", "cloud",
                AchievementCategory.Config, AchievementRarity.Epic,
                mcpCount >= 5),
            A("cfg-permissions", "문지기", "권한 규칙 설정", "security",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.Permissions is { Allow.Count: > 0 } or { Deny.Count: > 0 }),
            A("cfg-env", "환경 설정자", "환경변수 설정", "terminal",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.Env is { Count: > 0 }),
            A("cfg-complete", "풀 세팅", "설정 완성도 100% 달성", "verified",
                AchievementCategory.Config, AchievementRarity.Epic,
                stats.ConfigCompleteness >= 1.0),

            // ── 사용량 (Usage) ── 10개
            A("use-first", "첫 대화", "첫 메시지 전송", "waving_hand",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalMessages >= 1),
            A("use-100", "첫 발걸음", "100 메시지 달성", "chat",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalMessages >= 100),
            A("use-1k", "수다쟁이", "1,000 메시지 달성", "forum",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalMessages >= 1000),
            A("use-10k", "파워 유저", "10,000 메시지 달성", "whatshot",
                AchievementCategory.Usage, AchievementRarity.Epic,
                stats.TotalMessages >= 10000),
            A("use-100k", "멈출 수 없는", "100,000 메시지 달성", "rocket_launch",
                AchievementCategory.Usage, AchievementRarity.Legendary,
                stats.TotalMessages >= 100000),
            A("use-sessions-50", "세션 수집가", "50 세션 달성", "collections",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalSessions >= 50),
            A("use-sessions-100", "백전노장", "100 세션 달성", "layers",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalSessions >= 100),
            A("use-sessions-500", "세션 군주", "500 세션 달성", "workspace_premium",
                AchievementCategory.Usage, AchievementRarity.Epic,
                stats.TotalSessions >= 500),
            A("use-tools-1k", "도구 입문자", "1,000 도구 호출 달성", "handyman",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalToolCalls >= 1000),
            A("use-tools-10k", "도구의 달인", "10,000 도구 호출 달성", "build",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalToolCalls >= 10000),

            // ── 연속 기록 (Streak) ── 7개
            A("streak-3", "해트트릭", "3일 연속 사용", "local_fire_department",
                AchievementCategory.Streak, AchievementRarity.Common,
                stats.Streak.LongestStreak >= 3),
            A("streak-7", "주간 전사", "7일 연속 사용", "military_tech",
                AchievementCategory.Streak, AchievementRarity.Rare,
                stats.Streak.LongestStreak >= 7),
            A("streak-14", "2주의 힘", "14일 연속 사용", "shield",
                AchievementCategory.Streak, AchievementRarity.Rare,
                stats.Streak.LongestStreak >= 14),
            A("streak-30", "월간 마스터", "30일 연속 사용", "emoji_events",
                AchievementCategory.Streak, AchievementRarity.Epic,
                stats.Streak.LongestStreak >= 30),
            A("streak-60", "철인", "60일 연속 사용", "diamond",
                AchievementCategory.Streak, AchievementRarity.Epic,
                stats.Streak.LongestStreak >= 60),
            A("streak-90", "분기의 왕", "90일 연속 사용", "crown",
                AchievementCategory.Streak, AchievementRarity.Legendary,
                stats.Streak.LongestStreak >= 90),
            A("streak-365", "365일 전설", "1년 연속 사용", "auto_awesome",
                AchievementCategory.Streak, AchievementRarity.Legendary,
                stats.Streak.LongestStreak >= 365),

            // ── 숙련 (Mastery) ── 5개
            A("mastery-10d", "열정가", "10일 활동", "favorite",
                AchievementCategory.Mastery, AchievementRarity.Common,
                stats.DaysActive >= 10),
            A("mastery-30d", "헌신자", "30일 활동", "star",
                AchievementCategory.Mastery, AchievementRarity.Rare,
                stats.DaysActive >= 30),
            A("mastery-100d", "백일장", "100일 활동", "emoji_events",
                AchievementCategory.Mastery, AchievementRarity.Epic,
                stats.DaysActive >= 100),
            A("mastery-365d", "1년의 여정", "365일 활동", "cake",
                AchievementCategory.Mastery, AchievementRarity.Legendary,
                stats.DaysActive >= 365),
            A("mastery-hook", "자동화 장인", "첫 훅 등록", "settings",
                AchievementCategory.Mastery, AchievementRarity.Common,
                hookCount >= 1),

            // ── 탐험 (Explorer) ── 4개
            A("exp-3proj", "탐험가", "3개 프로젝트에서 작업", "explore",
                AchievementCategory.Explorer, AchievementRarity.Common,
                stats.TotalProjects >= 3),
            A("exp-5proj", "멀티태스커", "5개 프로젝트에서 작업", "account_tree",
                AchievementCategory.Explorer, AchievementRarity.Rare,
                stats.TotalProjects >= 5),
            A("exp-10proj", "만능 개발자", "10개 프로젝트에서 작업", "public",
                AchievementCategory.Explorer, AchievementRarity.Epic,
                stats.TotalProjects >= 10),
            A("exp-20proj", "세계 정복자", "20개 프로젝트에서 작업", "travel_explore",
                AchievementCategory.Explorer, AchievementRarity.Legendary,
                stats.TotalProjects >= 20),

            // ── 효율 (Efficiency) ── 3개
            A("eff-cache50", "캐시 입문", "캐시 히트율 50% 이상", "speed",
                AchievementCategory.Efficiency, AchievementRarity.Common,
                stats.CacheHitRate >= 50),
            A("eff-cache80", "최적화 달인", "캐시 히트율 80% 이상", "bolt",
                AchievementCategory.Efficiency, AchievementRarity.Epic,
                stats.CacheHitRate >= 80),
            A("eff-savings", "절약왕", "캐시로 $1 이상 절감", "savings",
                AchievementCategory.Efficiency, AchievementRarity.Rare,
                stats.EstimatedCacheSavings >= 1),

            // ── 시간 (Time) ── 5개
            A("time-owl", "올빼미", "야간(22-04시) 50회 이상 작업", "dark_mode",
                AchievementCategory.Time, AchievementRarity.Rare,
                stats.NightSessionCount >= 50),
            A("time-morning", "아침형 인간", "새벽(05-09시) 50회 이상 작업", "wb_sunny",
                AchievementCategory.Time, AchievementRarity.Rare,
                stats.MorningSessionCount >= 50),
            A("time-marathon", "마라톤", "2시간 이상 단일 세션", "timer",
                AchievementCategory.Time, AchievementRarity.Rare,
                stats.LongestSessionMs >= 2 * 3600 * 1000),
            A("time-ultramarathon", "울트라마라톤", "8시간 이상 단일 세션", "hourglass_top",
                AchievementCategory.Time, AchievementRarity.Epic,
                stats.LongestSessionMs >= 8 * 3600 * 1000),
            A("time-allnighter", "밤샘 전사", "야간 100회 이상 작업", "nightlight",
                AchievementCategory.Time, AchievementRarity.Epic,
                stats.NightSessionCount >= 100)
        ];
    }

    private static StreakInfo CalculateStreak(List<DateOnly> activeDates)
    {
        if (activeDates.Count == 0) return new StreakInfo();

        var sorted = activeDates.OrderDescending().ToList();
        var today = DateOnly.FromDateTime(DateTime.Now);

        var current = 0;
        var checkDate = today;
        foreach (var date in sorted)
            if (date == checkDate || date == checkDate.AddDays(-1))
            {
                current++;
                checkDate = date;
            }
            else if (date < checkDate.AddDays(-1))
            {
                break;
            }

        // Longest streak
        int longest = 0, streak = 1;
        for (var i = 1; i < sorted.Count; i++)
            if (sorted[i - 1].DayNumber - sorted[i].DayNumber == 1)
            {
                streak++;
            }
            else
            {
                longest = Math.Max(longest, streak);
                streak = 1;
            }

        longest = Math.Max(longest, streak);

        return new StreakInfo
        {
            CurrentStreak = current,
            LongestStreak = longest,
            LastActiveDate = sorted.Count > 0 ? sorted[0].ToDateTime(TimeOnly.MinValue) : null
        };
    }

    private static UserLevel CalculateLevel(int xp)
    {
        var level = new UserLevel { Xp = xp };
        for (var i = UserLevel.Thresholds.Length - 1; i >= 0; i--)
            if (xp >= UserLevel.Thresholds[i])
            {
                level.Level = i + 1;
                level.Name = UserLevel.Names[Math.Min(i, UserLevel.Names.Length - 1)];
                level.XpForNext = i < UserLevel.Thresholds.Length - 1
                    ? UserLevel.Thresholds[i + 1]
                    : UserLevel.Thresholds[^1];
                level.Progress = level.XpForNext > UserLevel.Thresholds[i]
                    ? (double)(xp - UserLevel.Thresholds[i]) / (level.XpForNext - UserLevel.Thresholds[i])
                    : 1.0;
                break;
            }

        return level;
    }

    private async Task<CostSummary> CalculateCostSummaryAsync()
    {
        var now = DateTime.UtcNow;
        var dayOfMonth = now.Day;

        var todayStats = await statsCacheService.GetMergedStatsAsync(1);
        var weekStats = await statsCacheService.GetMergedStatsAsync(7);
        var monthStats = await statsCacheService.GetMergedStatsAsync(dayOfMonth);

        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var projection = dayOfMonth > 0 ? monthStats.TotalCost / dayOfMonth * daysInMonth : 0m;

        var last7 = weekStats.DailyTokenTrend
            .OrderBy(d => d.Date)
            .Select(d => d.DailyCost)
            .ToList();

        return new CostSummary
        {
            Today = todayStats.TotalCost,
            ThisMonth = monthStats.TotalCost,
            MonthlyProjection = projection,
            Last7Days = last7
        };
    }
}