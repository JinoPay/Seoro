using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class GamificationService : IGamificationService
{
    private readonly IStatsCacheService _statsCacheService;
    private readonly ISessionService _sessionService;
    private readonly ISessionReplayService _replayService;
    private readonly IClaudeSettingsService _claudeSettings;
    private readonly ILogger<GamificationService> _logger;

    public GamificationService(
        IStatsCacheService statsCacheService,
        ISessionService sessionService,
        ISessionReplayService replayService,
        IClaudeSettingsService claudeSettings,
        ILogger<GamificationService> logger)
    {
        _statsCacheService = statsCacheService;
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
            // Gather ALL replay sessions for daily activity breakdown
            var replaySessions = new List<SessionReplaySummary>();
            int offset = 0;
            const int batchSize = 50;
            SessionListResult batch;
            do
            {
                batch = await _replayService.ListSessionsAsync(limit: batchSize, offset: offset);
                replaySessions.AddRange(batch.Sessions);
                offset += batchSize;
            } while (batch.HasMore);

            // Build daily activity from replay sessions
            var dailyMap = new Dictionary<string, DailyActivityEntry>();
            var projectPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int nightSessions = 0, morningSessions = 0;
            long longestMs = 0;

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

                // Track projects
                if (!string.IsNullOrEmpty(s.ProjectPath))
                    projectPaths.Add(s.ProjectPath);

                // Time-of-day tracking
                if (s.FirstTimestamp.HasValue)
                {
                    var hour = s.FirstTimestamp.Value.Hour;
                    stats.HourCounts[hour]++;
                    if (hour >= 22 || hour < 4) nightSessions++;
                    if (hour >= 5 && hour < 9) morningSessions++;
                }

                // Longest session
                if (s.Duration.HasValue)
                {
                    var ms = (long)s.Duration.Value.TotalMilliseconds;
                    if (ms > longestMs) longestMs = ms;
                }
            }

            stats.DailyActivity = dailyMap.Values.OrderBy(d => d.Date).ToList();
            stats.TotalSessions = replaySessions.Count;
            stats.TotalMessages = replaySessions.Sum(s => s.MessageCount);
            stats.TotalToolCalls = replaySessions.Sum(s => s.ToolCallCount);
            stats.DaysActive = dailyMap.Count;
            stats.TotalProjects = projectPaths.Count;
            stats.NightSessionCount = nightSessions;
            stats.MorningSessionCount = morningSessions;
            stats.LongestSessionMs = longestMs;

            // Cache stats from stats-cache.json
            var usageStats = await _statsCacheService.GetMergedStatsAsync();
            var cacheTotal = usageStats.TotalCacheCreationTokens + usageStats.TotalCacheReadTokens;
            stats.CacheHitRate = cacheTotal > 0
                ? (double)usageStats.TotalCacheReadTokens / cacheTotal * 100 : 0;

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
            var activeDates = dailyMap.Keys
                .Where(k => dailyMap[k].MessageCount > 0)
                .Select(DateOnly.Parse).ToList();
            stats.Streak = CalculateStreak(activeDates);

            // XP and level
            var settings = await _claudeSettings.ReadAsync(ClaudeSettingsScope.Global);
            int xp = CalculateXp(settings, stats);
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
        var todayStr = now.ToString("yyyy-MM-dd");
        var monthStr = new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");

        // Use merged stats (stats-cache.json + fallback)
        var allStats = await _statsCacheService.GetMergedStatsAsync();

        var todayCost = allStats.DailyTokenTrend
            .Where(d => d.Date == todayStr)
            .Sum(d =>
            {
                decimal cost = 0;
                foreach (var (model, tokens) in d.TokensByModel)
                {
                    var pricing = ModelDefinitions.GetPricing(model);
                    if (pricing != null)
                        cost += (decimal)tokens / 1_000_000m * pricing.Input;
                }
                return cost;
            });

        var monthCost = allStats.DailyTokenTrend
            .Where(d => string.Compare(d.Date, monthStr, StringComparison.Ordinal) >= 0)
            .Sum(d =>
            {
                decimal cost = 0;
                foreach (var (model, tokens) in d.TokensByModel)
                {
                    var pricing = ModelDefinitions.GetPricing(model);
                    if (pricing != null)
                        cost += (decimal)tokens / 1_000_000m * pricing.Input;
                }
                return cost;
            });

        var dayOfMonth = now.Day;
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var projection = dayOfMonth > 0 ? monthCost / dayOfMonth * daysInMonth : 0m;

        return new CostSummary
        {
            Today = todayCost,
            ThisMonth = monthCost,
            MonthlyProjection = projection,
        };
    }

    /// <summary>
    /// Expanded XP sources:
    /// - Config: model/effort set (+50), hooks (+20/hook), MCP servers (+30/server)
    /// - Activity: days active (+5/day), messages (+1 per 10, cap 500), tool calls (+1 per 20, cap 500)
    /// - Sessions: +2/session (cap 300)
    /// - Streaks: current streak * 10, longest streak * 5
    /// - Projects: +15/project
    /// </summary>
    private static int CalculateXp(ClaudeSettings settings, DashboardStats stats)
    {
        int xp = 0;

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
                stats.NightSessionCount >= 100),
        ];
    }

    private static Achievement A(string id, string name, string desc, string icon,
        AchievementCategory cat, AchievementRarity rarity, bool unlocked) => new()
    {
        Id = id, Name = name, Description = desc, Icon = icon,
        Category = cat, Rarity = rarity, Unlocked = unlocked
    };
}
