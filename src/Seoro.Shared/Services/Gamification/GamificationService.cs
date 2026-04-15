using Microsoft.Extensions.Logging;
using Seoro.Shared.Resources;

namespace Seoro.Shared.Services.Gamification;

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

            // Economy fields
            stats.TotalTokens = usageStats.TotalTokens;
            stats.TotalOutputTokens = usageStats.TotalOutputTokens;
            stats.TotalCostUsd = usageStats.TotalCost;
            stats.DistinctModelsUsed = usageStats.ByModel.Count;

            // Pattern fields
            stats.WeekendDaysActive = indexStats.DailyActivity
                .Count(d => DateOnly.TryParse(d.Date, out var dt)
                            && dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                            && d.MessageCount > 0);
            stats.PeakDayMessages = indexStats.DailyActivity.Count > 0
                ? indexStats.DailyActivity.Max(d => d.MessageCount)
                : 0;
            stats.ActiveHoursCount = indexStats.HourCounts.Count(h => h > 0);

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
            logger.LogWarning(ex, "대시보드 통계 계산 오류");
        }

        _cachedStats = stats;
        _cachedAt = DateTime.UtcNow;
        return stats;
    }

    private static (double completeness, List<ConfigItem> items) CalculateConfigCompleteness(ClaudeSettings settings)
    {
        var items = new List<ConfigItem>
        {
            new() { Name = Strings.Gamification_Config_Model, IsConfigured = settings.Model != null },
            new() { Name = Strings.Gamification_Config_EffortLevel, IsConfigured = settings.EffortLevel != null },
            new() { Name = Strings.Gamification_Config_DefaultMode, IsConfigured = settings.DefaultMode != null },
            new() { Name = Strings.Gamification_Config_Permissions, IsConfigured = settings.Permissions is { Allow.Count: > 0 } or { Deny.Count: > 0 } },
            new() { Name = Strings.Gamification_Config_Hooks, IsConfigured = settings.Hooks is { Count: > 0 } },
            new() { Name = Strings.Gamification_Config_McpServers, IsConfigured = settings.McpServers is { Count: > 0 } }
        };
        var filled = items.Count(i => i.IsConfigured);
        return ((double)filled / items.Count, items);
    }

    private static Achievement A(string id, string icon,
        AchievementCategory cat, AchievementRarity rarity, bool unlocked)
    {
        return new Achievement
        {
            Id = id, Name = Strings.GetAchievementName(id), Description = Strings.GetAchievementDesc(id), Icon = icon,
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

        // Economy XP
        xp += Math.Min((int)(stats.TotalTokens / 1_000_000), 200);
        xp += Math.Min((int)stats.TotalCostUsd, 100);

        // Pattern XP
        xp += stats.WeekendDaysActive * 2;
        xp += stats.ActiveHoursCount * 3;

        return xp;
    }

    private static int GetHookCount(ClaudeSettings settings)
    {
        return settings.Hooks?.Values.Sum(configs => configs.Sum(c => c.Hooks.Count)) ?? 0;
    }

    // ═══════════════════════════════════════════
    //  Achievements — 9 categories, 105 total
    // ═══════════════════════════════════════════

    private static List<Achievement> EvaluateAchievements(DashboardStats stats, ClaudeSettings settings)
    {
        var hookCount = GetHookCount(settings);
        var mcpCount = settings.McpServers?.Count ?? 0;

        return
        [
            // ── 설정 (Config) ── 14개
            A("cfg-configured", "tune",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.Model != null || settings.EffortLevel != null),
            A("cfg-thinking", "psychology",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.AlwaysThinkingEnabled == true),
            A("cfg-memory", "memory",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.AutoMemoryEnabled == true),
            A("cfg-mode", "rule",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.DefaultMode != null),
            A("cfg-hooks", "bolt",
                AchievementCategory.Config, AchievementRarity.Rare,
                hookCount >= 5),
            A("cfg-hooks10", "precision_manufacturing",
                AchievementCategory.Config, AchievementRarity.Epic,
                hookCount >= 10),
            A("cfg-hooks20", "webhook",
                AchievementCategory.Config, AchievementRarity.Legendary,
                hookCount >= 20),
            A("cfg-mcp", "hub",
                AchievementCategory.Config, AchievementRarity.Common,
                mcpCount > 0),
            A("cfg-mcp-network", "device_hub",
                AchievementCategory.Config, AchievementRarity.Rare,
                mcpCount >= 3),
            A("cfg-mcp-kingdom", "cloud",
                AchievementCategory.Config, AchievementRarity.Epic,
                mcpCount >= 5),
            A("cfg-mcp10", "dns",
                AchievementCategory.Config, AchievementRarity.Legendary,
                mcpCount >= 10),
            A("cfg-permissions", "security",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.Permissions is { Allow.Count: > 0 } or { Deny.Count: > 0 }),
            A("cfg-env", "terminal",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.Env is { Count: > 0 }),
            A("cfg-complete", "verified",
                AchievementCategory.Config, AchievementRarity.Epic,
                stats.ConfigCompleteness >= 1.0),

            // ── 사용량 (Usage) ── 20개
            A("use-first", "waving_hand",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalMessages >= 1),
            A("use-100", "chat",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalMessages >= 100),
            A("use-500", "mark_chat_read",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalMessages >= 500),
            A("use-1k", "forum",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalMessages >= 1000),
            A("use-5k", "question_answer",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalMessages >= 5000),
            A("use-10k", "whatshot",
                AchievementCategory.Usage, AchievementRarity.Epic,
                stats.TotalMessages >= 10000),
            A("use-50k", "sms",
                AchievementCategory.Usage, AchievementRarity.Epic,
                stats.TotalMessages >= 50000),
            A("use-100k", "rocket_launch",
                AchievementCategory.Usage, AchievementRarity.Legendary,
                stats.TotalMessages >= 100000),
            A("use-sessions-10", "play_circle",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalSessions >= 10),
            A("use-sessions-50", "collections",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalSessions >= 50),
            A("use-sessions-100", "layers",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalSessions >= 100),
            A("use-sessions-200", "video_library",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalSessions >= 200),
            A("use-sessions-500", "workspace_premium",
                AchievementCategory.Usage, AchievementRarity.Epic,
                stats.TotalSessions >= 500),
            A("use-sessions-1k", "all_inclusive",
                AchievementCategory.Usage, AchievementRarity.Legendary,
                stats.TotalSessions >= 1000),
            A("use-tools-100", "construction",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalToolCalls >= 100),
            A("use-tools-1k", "handyman",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalToolCalls >= 1000),
            A("use-tools-5k", "hardware",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalToolCalls >= 5000),
            A("use-tools-10k", "build",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalToolCalls >= 10000),
            A("use-tools-50k", "engineering",
                AchievementCategory.Usage, AchievementRarity.Epic,
                stats.TotalToolCalls >= 50000),
            A("use-tools-100k", "factory",
                AchievementCategory.Usage, AchievementRarity.Legendary,
                stats.TotalToolCalls >= 100000),

            // ── 연속 기록 (Streak) ── 10개
            A("streak-3", "local_fire_department",
                AchievementCategory.Streak, AchievementRarity.Common,
                stats.Streak.LongestStreak >= 3),
            A("streak-5", "whatshot",
                AchievementCategory.Streak, AchievementRarity.Common,
                stats.Streak.LongestStreak >= 5),
            A("streak-7", "military_tech",
                AchievementCategory.Streak, AchievementRarity.Rare,
                stats.Streak.LongestStreak >= 7),
            A("streak-14", "shield",
                AchievementCategory.Streak, AchievementRarity.Rare,
                stats.Streak.LongestStreak >= 14),
            A("streak-21", "psychology",
                AchievementCategory.Streak, AchievementRarity.Rare,
                stats.Streak.LongestStreak >= 21),
            A("streak-30", "emoji_events",
                AchievementCategory.Streak, AchievementRarity.Epic,
                stats.Streak.LongestStreak >= 30),
            A("streak-60", "diamond",
                AchievementCategory.Streak, AchievementRarity.Epic,
                stats.Streak.LongestStreak >= 60),
            A("streak-90", "crown",
                AchievementCategory.Streak, AchievementRarity.Legendary,
                stats.Streak.LongestStreak >= 90),
            A("streak-180", "fitness_center",
                AchievementCategory.Streak, AchievementRarity.Legendary,
                stats.Streak.LongestStreak >= 180),
            A("streak-365", "auto_awesome",
                AchievementCategory.Streak, AchievementRarity.Legendary,
                stats.Streak.LongestStreak >= 365),

            // ── 숙련 (Mastery) ── 10개
            A("mastery-3d", "thumb_up",
                AchievementCategory.Mastery, AchievementRarity.Common,
                stats.DaysActive >= 3),
            A("mastery-10d", "favorite",
                AchievementCategory.Mastery, AchievementRarity.Common,
                stats.DaysActive >= 10),
            A("mastery-30d", "star",
                AchievementCategory.Mastery, AchievementRarity.Rare,
                stats.DaysActive >= 30),
            A("mastery-50d", "military_tech",
                AchievementCategory.Mastery, AchievementRarity.Rare,
                stats.DaysActive >= 50),
            A("mastery-100d", "emoji_events",
                AchievementCategory.Mastery, AchievementRarity.Epic,
                stats.DaysActive >= 100),
            A("mastery-200d", "workspace_premium",
                AchievementCategory.Mastery, AchievementRarity.Epic,
                stats.DaysActive >= 200),
            A("mastery-365d", "cake",
                AchievementCategory.Mastery, AchievementRarity.Legendary,
                stats.DaysActive >= 365),
            A("mastery-500d", "auto_awesome",
                AchievementCategory.Mastery, AchievementRarity.Legendary,
                stats.DaysActive >= 500),
            A("mastery-hook", "settings",
                AchievementCategory.Mastery, AchievementRarity.Common,
                hookCount >= 1),
            A("mastery-hooks3", "smart_toy",
                AchievementCategory.Mastery, AchievementRarity.Rare,
                hookCount >= 3),

            // ── 탐험 (Explorer) ── 8개
            A("exp-1proj", "folder_open",
                AchievementCategory.Explorer, AchievementRarity.Common,
                stats.TotalProjects >= 1),
            A("exp-3proj", "explore",
                AchievementCategory.Explorer, AchievementRarity.Common,
                stats.TotalProjects >= 3),
            A("exp-5proj", "account_tree",
                AchievementCategory.Explorer, AchievementRarity.Rare,
                stats.TotalProjects >= 5),
            A("exp-7proj", "source",
                AchievementCategory.Explorer, AchievementRarity.Rare,
                stats.TotalProjects >= 7),
            A("exp-10proj", "public",
                AchievementCategory.Explorer, AchievementRarity.Epic,
                stats.TotalProjects >= 10),
            A("exp-15proj", "inventory",
                AchievementCategory.Explorer, AchievementRarity.Epic,
                stats.TotalProjects >= 15),
            A("exp-20proj", "travel_explore",
                AchievementCategory.Explorer, AchievementRarity.Legendary,
                stats.TotalProjects >= 20),
            A("exp-50proj", "language",
                AchievementCategory.Explorer, AchievementRarity.Legendary,
                stats.TotalProjects >= 50),

            // ── 효율 (Efficiency) ── 9개
            A("eff-cache30", "trending_up",
                AchievementCategory.Efficiency, AchievementRarity.Common,
                stats.CacheHitRate >= 30),
            A("eff-cache50", "speed",
                AchievementCategory.Efficiency, AchievementRarity.Common,
                stats.CacheHitRate >= 50),
            A("eff-cache70", "flash_on",
                AchievementCategory.Efficiency, AchievementRarity.Rare,
                stats.CacheHitRate >= 70),
            A("eff-cache80", "bolt",
                AchievementCategory.Efficiency, AchievementRarity.Epic,
                stats.CacheHitRate >= 80),
            A("eff-cache90", "electric_bolt",
                AchievementCategory.Efficiency, AchievementRarity.Legendary,
                stats.CacheHitRate >= 90),
            A("eff-savings", "savings",
                AchievementCategory.Efficiency, AchievementRarity.Rare,
                stats.EstimatedCacheSavings >= 1),
            A("eff-save5", "account_balance_wallet",
                AchievementCategory.Efficiency, AchievementRarity.Rare,
                stats.EstimatedCacheSavings >= 5),
            A("eff-save10", "paid",
                AchievementCategory.Efficiency, AchievementRarity.Epic,
                stats.EstimatedCacheSavings >= 10),
            A("eff-save50", "monetization_on",
                AchievementCategory.Efficiency, AchievementRarity.Legendary,
                stats.EstimatedCacheSavings >= 50),

            // ── 시간 (Time) ── 12개
            A("time-sprint", "directions_run",
                AchievementCategory.Time, AchievementRarity.Common,
                stats.LongestSessionMs >= 30L * 60 * 1000),
            A("time-owl", "dark_mode",
                AchievementCategory.Time, AchievementRarity.Rare,
                stats.NightSessionCount >= 50),
            A("time-morning", "wb_sunny",
                AchievementCategory.Time, AchievementRarity.Rare,
                stats.MorningSessionCount >= 50),
            A("time-marathon", "timer",
                AchievementCategory.Time, AchievementRarity.Rare,
                stats.LongestSessionMs >= 2L * 3600 * 1000),
            A("time-half", "sprint",
                AchievementCategory.Time, AchievementRarity.Rare,
                stats.LongestSessionMs >= 4L * 3600 * 1000),
            A("time-ultramarathon", "hourglass_top",
                AchievementCategory.Time, AchievementRarity.Epic,
                stats.LongestSessionMs >= 8L * 3600 * 1000),
            A("time-allnighter", "nightlight",
                AchievementCategory.Time, AchievementRarity.Epic,
                stats.NightSessionCount >= 100),
            A("time-owl200", "bedtime",
                AchievementCategory.Time, AchievementRarity.Epic,
                stats.NightSessionCount >= 200),
            A("time-morning200", "wb_twilight",
                AchievementCategory.Time, AchievementRarity.Epic,
                stats.MorningSessionCount >= 200),
            A("time-owl500", "mode_night",
                AchievementCategory.Time, AchievementRarity.Legendary,
                stats.NightSessionCount >= 500),
            A("time-morning500", "light_mode",
                AchievementCategory.Time, AchievementRarity.Legendary,
                stats.MorningSessionCount >= 500),
            A("time-ironman", "fitness_center",
                AchievementCategory.Time, AchievementRarity.Legendary,
                stats.LongestSessionMs >= 24L * 3600 * 1000),

            // ── 경제 (Economy) ── 12개
            A("eco-token-1m", "token",
                AchievementCategory.Economy, AchievementRarity.Common,
                stats.TotalTokens >= 1_000_000),
            A("eco-token-10m", "generating_tokens",
                AchievementCategory.Economy, AchievementRarity.Rare,
                stats.TotalTokens >= 10_000_000),
            A("eco-token-100m", "data_usage",
                AchievementCategory.Economy, AchievementRarity.Epic,
                stats.TotalTokens >= 100_000_000),
            A("eco-token-1b", "all_inclusive",
                AchievementCategory.Economy, AchievementRarity.Legendary,
                stats.TotalTokens >= 1_000_000_000),
            A("eco-cost-1", "attach_money",
                AchievementCategory.Economy, AchievementRarity.Common,
                stats.TotalCostUsd >= 1),
            A("eco-cost-10", "payments",
                AchievementCategory.Economy, AchievementRarity.Common,
                stats.TotalCostUsd >= 10),
            A("eco-cost-100", "account_balance",
                AchievementCategory.Economy, AchievementRarity.Rare,
                stats.TotalCostUsd >= 100),
            A("eco-cost-500", "currency_exchange",
                AchievementCategory.Economy, AchievementRarity.Epic,
                stats.TotalCostUsd >= 500),
            A("eco-cost-1k", "diamond",
                AchievementCategory.Economy, AchievementRarity.Legendary,
                stats.TotalCostUsd >= 1000),
            A("eco-model-1", "smart_toy",
                AchievementCategory.Economy, AchievementRarity.Common,
                stats.DistinctModelsUsed >= 1),
            A("eco-model-3", "psychology",
                AchievementCategory.Economy, AchievementRarity.Rare,
                stats.DistinctModelsUsed >= 3),
            A("eco-model-5", "diversity_3",
                AchievementCategory.Economy, AchievementRarity.Epic,
                stats.DistinctModelsUsed >= 5),

            // ── 패턴 (Pattern) ── 10개
            A("pat-weekend20", "weekend",
                AchievementCategory.Pattern, AchievementRarity.Common,
                stats.WeekendDaysActive >= 20),
            A("pat-weekend50", "event_available",
                AchievementCategory.Pattern, AchievementRarity.Rare,
                stats.WeekendDaysActive >= 50),
            A("pat-allhours", "schedule",
                AchievementCategory.Pattern, AchievementRarity.Epic,
                stats.ActiveHoursCount >= 24),
            A("pat-burst50", "local_fire_department",
                AchievementCategory.Pattern, AchievementRarity.Common,
                stats.PeakDayMessages >= 50),
            A("pat-burst100", "whatshot",
                AchievementCategory.Pattern, AchievementRarity.Rare,
                stats.PeakDayMessages >= 100),
            A("pat-burst200", "bolt",
                AchievementCategory.Pattern, AchievementRarity.Epic,
                stats.PeakDayMessages >= 200),
            A("pat-burst500", "flash_on",
                AchievementCategory.Pattern, AchievementRarity.Legendary,
                stats.PeakDayMessages >= 500),
            A("pat-lunch", "lunch_dining",
                AchievementCategory.Pattern, AchievementRarity.Common,
                stats.HourCounts[11] + stats.HourCounts[12] > 0),
            A("pat-diverse12", "access_time",
                AchievementCategory.Pattern, AchievementRarity.Rare,
                stats.ActiveHoursCount >= 12),
            A("pat-nocturn", "contrast",
                AchievementCategory.Pattern, AchievementRarity.Epic,
                stats.NightSessionCount >= 100 && stats.MorningSessionCount >= 100)
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
                level.Name = Strings.GetLevelName(i + 1);
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