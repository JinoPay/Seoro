using Microsoft.Extensions.Logging;

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
            A("cfg-configured", "설정 완료", "기본 설정(모델/성능) 완료", "tune",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.Model != null || settings.EffortLevel != null),
            A("cfg-thinking", "깊은 사색", "확장 사고 모드 활성화", "psychology",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.AlwaysThinkingEnabled == true),
            A("cfg-memory", "기억의 궁전", "자동 메모리 활성화", "memory",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.AutoMemoryEnabled == true),
            A("cfg-mode", "나만의 스타일", "기본 모드 설정", "rule",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.DefaultMode != null),
            A("cfg-hooks", "훅 마스터", "5개 이상 훅 이벤트 등록", "bolt",
                AchievementCategory.Config, AchievementRarity.Rare,
                hookCount >= 5),
            A("cfg-hooks10", "자동화 제국", "10개 이상 훅 등록", "precision_manufacturing",
                AchievementCategory.Config, AchievementRarity.Epic,
                hookCount >= 10),
            A("cfg-hooks20", "자동화의 신", "20개 이상 훅 등록", "webhook",
                AchievementCategory.Config, AchievementRarity.Legendary,
                hookCount >= 20),
            A("cfg-mcp", "첫 연결", "MCP 서버 1개 연결", "hub",
                AchievementCategory.Config, AchievementRarity.Common,
                mcpCount > 0),
            A("cfg-mcp-network", "네트워크 구축", "MCP 서버 3개 이상 연결", "device_hub",
                AchievementCategory.Config, AchievementRarity.Rare,
                mcpCount >= 3),
            A("cfg-mcp-kingdom", "MCP 왕국", "MCP 서버 5개 이상 연결", "cloud",
                AchievementCategory.Config, AchievementRarity.Epic,
                mcpCount >= 5),
            A("cfg-mcp10", "MCP 제국", "MCP 서버 10개 이상 연결", "dns",
                AchievementCategory.Config, AchievementRarity.Legendary,
                mcpCount >= 10),
            A("cfg-permissions", "문지기", "권한 규칙 설정", "security",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.Permissions is { Allow.Count: > 0 } or { Deny.Count: > 0 }),
            A("cfg-env", "환경 설정자", "환경변수 설정", "terminal",
                AchievementCategory.Config, AchievementRarity.Common,
                settings.Env is { Count: > 0 }),
            A("cfg-complete", "풀 세팅", "설정 완성도 100% 달성", "verified",
                AchievementCategory.Config, AchievementRarity.Epic,
                stats.ConfigCompleteness >= 1.0),

            // ── 사용량 (Usage) ── 20개
            A("use-first", "첫 대화", "첫 메시지 전송", "waving_hand",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalMessages >= 1),
            A("use-100", "첫 발걸음", "100 메시지 달성", "chat",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalMessages >= 100),
            A("use-500", "대화의 물결", "500 메시지 달성", "mark_chat_read",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalMessages >= 500),
            A("use-1k", "수다쟁이", "1,000 메시지 달성", "forum",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalMessages >= 1000),
            A("use-5k", "토크쇼 호스트", "5,000 메시지 달성", "question_answer",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalMessages >= 5000),
            A("use-10k", "파워 유저", "10,000 메시지 달성", "whatshot",
                AchievementCategory.Usage, AchievementRarity.Epic,
                stats.TotalMessages >= 10000),
            A("use-50k", "디지털 소설가", "50,000 메시지 달성", "sms",
                AchievementCategory.Usage, AchievementRarity.Epic,
                stats.TotalMessages >= 50000),
            A("use-100k", "멈출 수 없는", "100,000 메시지 달성", "rocket_launch",
                AchievementCategory.Usage, AchievementRarity.Legendary,
                stats.TotalMessages >= 100000),
            A("use-sessions-10", "세션 입문", "10 세션 달성", "play_circle",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalSessions >= 10),
            A("use-sessions-50", "세션 수집가", "50 세션 달성", "collections",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalSessions >= 50),
            A("use-sessions-100", "백전노장", "100 세션 달성", "layers",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalSessions >= 100),
            A("use-sessions-200", "세션 전문가", "200 세션 달성", "video_library",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalSessions >= 200),
            A("use-sessions-500", "세션 군주", "500 세션 달성", "workspace_premium",
                AchievementCategory.Usage, AchievementRarity.Epic,
                stats.TotalSessions >= 500),
            A("use-sessions-1k", "세션 제왕", "1,000 세션 달성", "all_inclusive",
                AchievementCategory.Usage, AchievementRarity.Legendary,
                stats.TotalSessions >= 1000),
            A("use-tools-100", "도구 첫 걸음", "100 도구 호출 달성", "construction",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalToolCalls >= 100),
            A("use-tools-1k", "도구 입문자", "1,000 도구 호출 달성", "handyman",
                AchievementCategory.Usage, AchievementRarity.Common,
                stats.TotalToolCalls >= 1000),
            A("use-tools-5k", "도구 장인", "5,000 도구 호출 달성", "hardware",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalToolCalls >= 5000),
            A("use-tools-10k", "도구의 달인", "10,000 도구 호출 달성", "build",
                AchievementCategory.Usage, AchievementRarity.Rare,
                stats.TotalToolCalls >= 10000),
            A("use-tools-50k", "도구 마에스트로", "50,000 도구 호출 달성", "engineering",
                AchievementCategory.Usage, AchievementRarity.Epic,
                stats.TotalToolCalls >= 50000),
            A("use-tools-100k", "도구의 신", "100,000 도구 호출 달성", "factory",
                AchievementCategory.Usage, AchievementRarity.Legendary,
                stats.TotalToolCalls >= 100000),

            // ── 연속 기록 (Streak) ── 10개
            A("streak-3", "해트트릭", "3일 연속 사용", "local_fire_department",
                AchievementCategory.Streak, AchievementRarity.Common,
                stats.Streak.LongestStreak >= 3),
            A("streak-5", "파이브 데이즈", "5일 연속 사용", "whatshot",
                AchievementCategory.Streak, AchievementRarity.Common,
                stats.Streak.LongestStreak >= 5),
            A("streak-7", "주간 전사", "7일 연속 사용", "military_tech",
                AchievementCategory.Streak, AchievementRarity.Rare,
                stats.Streak.LongestStreak >= 7),
            A("streak-14", "2주의 힘", "14일 연속 사용", "shield",
                AchievementCategory.Streak, AchievementRarity.Rare,
                stats.Streak.LongestStreak >= 14),
            A("streak-21", "습관 형성", "21일 연속 사용", "psychology",
                AchievementCategory.Streak, AchievementRarity.Rare,
                stats.Streak.LongestStreak >= 21),
            A("streak-30", "월간 마스터", "30일 연속 사용", "emoji_events",
                AchievementCategory.Streak, AchievementRarity.Epic,
                stats.Streak.LongestStreak >= 30),
            A("streak-60", "철인", "60일 연속 사용", "diamond",
                AchievementCategory.Streak, AchievementRarity.Epic,
                stats.Streak.LongestStreak >= 60),
            A("streak-90", "분기의 왕", "90일 연속 사용", "crown",
                AchievementCategory.Streak, AchievementRarity.Legendary,
                stats.Streak.LongestStreak >= 90),
            A("streak-180", "반년의 의지", "180일 연속 사용", "fitness_center",
                AchievementCategory.Streak, AchievementRarity.Legendary,
                stats.Streak.LongestStreak >= 180),
            A("streak-365", "365일 전설", "1년 연속 사용", "auto_awesome",
                AchievementCategory.Streak, AchievementRarity.Legendary,
                stats.Streak.LongestStreak >= 365),

            // ── 숙련 (Mastery) ── 10개
            A("mastery-3d", "시작이 반", "3일 활동", "thumb_up",
                AchievementCategory.Mastery, AchievementRarity.Common,
                stats.DaysActive >= 3),
            A("mastery-10d", "열정가", "10일 활동", "favorite",
                AchievementCategory.Mastery, AchievementRarity.Common,
                stats.DaysActive >= 10),
            A("mastery-30d", "헌신자", "30일 활동", "star",
                AchievementCategory.Mastery, AchievementRarity.Rare,
                stats.DaysActive >= 30),
            A("mastery-50d", "절반의 성공", "50일 활동", "military_tech",
                AchievementCategory.Mastery, AchievementRarity.Rare,
                stats.DaysActive >= 50),
            A("mastery-100d", "백일장", "100일 활동", "emoji_events",
                AchievementCategory.Mastery, AchievementRarity.Epic,
                stats.DaysActive >= 100),
            A("mastery-200d", "200일의 여정", "200일 활동", "workspace_premium",
                AchievementCategory.Mastery, AchievementRarity.Epic,
                stats.DaysActive >= 200),
            A("mastery-365d", "1년의 여정", "365일 활동", "cake",
                AchievementCategory.Mastery, AchievementRarity.Legendary,
                stats.DaysActive >= 365),
            A("mastery-500d", "500일 레전드", "500일 활동", "auto_awesome",
                AchievementCategory.Mastery, AchievementRarity.Legendary,
                stats.DaysActive >= 500),
            A("mastery-hook", "자동화 장인", "첫 훅 등록", "settings",
                AchievementCategory.Mastery, AchievementRarity.Common,
                hookCount >= 1),
            A("mastery-hooks3", "자동화 중급", "훅 3개 이상 등록", "smart_toy",
                AchievementCategory.Mastery, AchievementRarity.Rare,
                hookCount >= 3),

            // ── 탐험 (Explorer) ── 8개
            A("exp-1proj", "첫 프로젝트", "1개 프로젝트에서 작업", "folder_open",
                AchievementCategory.Explorer, AchievementRarity.Common,
                stats.TotalProjects >= 1),
            A("exp-3proj", "탐험가", "3개 프로젝트에서 작업", "explore",
                AchievementCategory.Explorer, AchievementRarity.Common,
                stats.TotalProjects >= 3),
            A("exp-5proj", "멀티태스커", "5개 프로젝트에서 작업", "account_tree",
                AchievementCategory.Explorer, AchievementRarity.Rare,
                stats.TotalProjects >= 5),
            A("exp-7proj", "활발한 탐험가", "7개 프로젝트에서 작업", "source",
                AchievementCategory.Explorer, AchievementRarity.Rare,
                stats.TotalProjects >= 7),
            A("exp-10proj", "만능 개발자", "10개 프로젝트에서 작업", "public",
                AchievementCategory.Explorer, AchievementRarity.Epic,
                stats.TotalProjects >= 10),
            A("exp-15proj", "프로젝트 수집가", "15개 프로젝트에서 작업", "inventory",
                AchievementCategory.Explorer, AchievementRarity.Epic,
                stats.TotalProjects >= 15),
            A("exp-20proj", "세계 정복자", "20개 프로젝트에서 작업", "travel_explore",
                AchievementCategory.Explorer, AchievementRarity.Legendary,
                stats.TotalProjects >= 20),
            A("exp-50proj", "프로젝트 제왕", "50개 프로젝트에서 작업", "language",
                AchievementCategory.Explorer, AchievementRarity.Legendary,
                stats.TotalProjects >= 50),

            // ── 효율 (Efficiency) ── 9개
            A("eff-cache30", "캐시 시작", "캐시 히트율 30% 이상", "trending_up",
                AchievementCategory.Efficiency, AchievementRarity.Common,
                stats.CacheHitRate >= 30),
            A("eff-cache50", "캐시 입문", "캐시 히트율 50% 이상", "speed",
                AchievementCategory.Efficiency, AchievementRarity.Common,
                stats.CacheHitRate >= 50),
            A("eff-cache70", "캐시 프로", "캐시 히트율 70% 이상", "flash_on",
                AchievementCategory.Efficiency, AchievementRarity.Rare,
                stats.CacheHitRate >= 70),
            A("eff-cache80", "최적화 달인", "캐시 히트율 80% 이상", "bolt",
                AchievementCategory.Efficiency, AchievementRarity.Epic,
                stats.CacheHitRate >= 80),
            A("eff-cache90", "캐시 마스터", "캐시 히트율 90% 이상", "electric_bolt",
                AchievementCategory.Efficiency, AchievementRarity.Legendary,
                stats.CacheHitRate >= 90),
            A("eff-savings", "절약왕", "캐시로 $1 이상 절감", "savings",
                AchievementCategory.Efficiency, AchievementRarity.Rare,
                stats.EstimatedCacheSavings >= 1),
            A("eff-save5", "절약 달인", "캐시로 $5 이상 절감", "account_balance_wallet",
                AchievementCategory.Efficiency, AchievementRarity.Rare,
                stats.EstimatedCacheSavings >= 5),
            A("eff-save10", "절약 전문가", "캐시로 $10 이상 절감", "paid",
                AchievementCategory.Efficiency, AchievementRarity.Epic,
                stats.EstimatedCacheSavings >= 10),
            A("eff-save50", "절약 마스터", "캐시로 $50 이상 절감", "monetization_on",
                AchievementCategory.Efficiency, AchievementRarity.Legendary,
                stats.EstimatedCacheSavings >= 50),

            // ── 시간 (Time) ── 12개
            A("time-sprint", "스프린트", "30분 이상 단일 세션", "directions_run",
                AchievementCategory.Time, AchievementRarity.Common,
                stats.LongestSessionMs >= 30L * 60 * 1000),
            A("time-owl", "올빼미", "야간(22-04시) 50회 이상 작업", "dark_mode",
                AchievementCategory.Time, AchievementRarity.Rare,
                stats.NightSessionCount >= 50),
            A("time-morning", "아침형 인간", "새벽(05-09시) 50회 이상 작업", "wb_sunny",
                AchievementCategory.Time, AchievementRarity.Rare,
                stats.MorningSessionCount >= 50),
            A("time-marathon", "마라톤", "2시간 이상 단일 세션", "timer",
                AchievementCategory.Time, AchievementRarity.Rare,
                stats.LongestSessionMs >= 2L * 3600 * 1000),
            A("time-half", "하프 마라톤", "4시간 이상 단일 세션", "sprint",
                AchievementCategory.Time, AchievementRarity.Rare,
                stats.LongestSessionMs >= 4L * 3600 * 1000),
            A("time-ultramarathon", "울트라마라톤", "8시간 이상 단일 세션", "hourglass_top",
                AchievementCategory.Time, AchievementRarity.Epic,
                stats.LongestSessionMs >= 8L * 3600 * 1000),
            A("time-allnighter", "밤샘 전사", "야간 100회 이상 작업", "nightlight",
                AchievementCategory.Time, AchievementRarity.Epic,
                stats.NightSessionCount >= 100),
            A("time-owl200", "올빼미 마스터", "야간 200회 이상 작업", "bedtime",
                AchievementCategory.Time, AchievementRarity.Epic,
                stats.NightSessionCount >= 200),
            A("time-morning200", "새벽의 전사", "새벽 200회 이상 작업", "wb_twilight",
                AchievementCategory.Time, AchievementRarity.Epic,
                stats.MorningSessionCount >= 200),
            A("time-owl500", "밤의 제왕", "야간 500회 이상 작업", "mode_night",
                AchievementCategory.Time, AchievementRarity.Legendary,
                stats.NightSessionCount >= 500),
            A("time-morning500", "새벽의 왕", "새벽 500회 이상 작업", "light_mode",
                AchievementCategory.Time, AchievementRarity.Legendary,
                stats.MorningSessionCount >= 500),
            A("time-ironman", "아이언맨", "24시간 이상 단일 세션", "fitness_center",
                AchievementCategory.Time, AchievementRarity.Legendary,
                stats.LongestSessionMs >= 24L * 3600 * 1000),

            // ── 경제 (Economy) ── 12개
            A("eco-token-1m", "백만 토큰", "총 100만 토큰 사용", "token",
                AchievementCategory.Economy, AchievementRarity.Common,
                stats.TotalTokens >= 1_000_000),
            A("eco-token-10m", "천만 토큰", "총 1,000만 토큰 사용", "generating_tokens",
                AchievementCategory.Economy, AchievementRarity.Rare,
                stats.TotalTokens >= 10_000_000),
            A("eco-token-100m", "억 토큰", "총 1억 토큰 사용", "data_usage",
                AchievementCategory.Economy, AchievementRarity.Epic,
                stats.TotalTokens >= 100_000_000),
            A("eco-token-1b", "십억 토큰", "총 10억 토큰 사용", "all_inclusive",
                AchievementCategory.Economy, AchievementRarity.Legendary,
                stats.TotalTokens >= 1_000_000_000),
            A("eco-cost-1", "첫 투자", "총 비용 $1 달성", "attach_money",
                AchievementCategory.Economy, AchievementRarity.Common,
                stats.TotalCostUsd >= 1),
            A("eco-cost-10", "후원자", "총 비용 $10 달성", "payments",
                AchievementCategory.Economy, AchievementRarity.Common,
                stats.TotalCostUsd >= 10),
            A("eco-cost-100", "큰 손", "총 비용 $100 달성", "account_balance",
                AchievementCategory.Economy, AchievementRarity.Rare,
                stats.TotalCostUsd >= 100),
            A("eco-cost-500", "고액 투자자", "총 비용 $500 달성", "currency_exchange",
                AchievementCategory.Economy, AchievementRarity.Epic,
                stats.TotalCostUsd >= 500),
            A("eco-cost-1k", "천 달러 클럽", "총 비용 $1,000 달성", "diamond",
                AchievementCategory.Economy, AchievementRarity.Legendary,
                stats.TotalCostUsd >= 1000),
            A("eco-model-1", "첫 모델", "1개 모델 사용", "smart_toy",
                AchievementCategory.Economy, AchievementRarity.Common,
                stats.DistinctModelsUsed >= 1),
            A("eco-model-3", "모델 탐험가", "3개 이상 모델 사용", "psychology",
                AchievementCategory.Economy, AchievementRarity.Rare,
                stats.DistinctModelsUsed >= 3),
            A("eco-model-5", "모델 수집가", "5개 이상 모델 사용", "diversity_3",
                AchievementCategory.Economy, AchievementRarity.Epic,
                stats.DistinctModelsUsed >= 5),

            // ── 패턴 (Pattern) ── 10개
            A("pat-weekend20", "주말 전사", "주말 20일 이상 활동", "weekend",
                AchievementCategory.Pattern, AchievementRarity.Common,
                stats.WeekendDaysActive >= 20),
            A("pat-weekend50", "주말의 왕", "주말 50일 이상 활동", "event_available",
                AchievementCategory.Pattern, AchievementRarity.Rare,
                stats.WeekendDaysActive >= 50),
            A("pat-allhours", "24시간 정복", "24시간 모든 시간대 활동", "schedule",
                AchievementCategory.Pattern, AchievementRarity.Epic,
                stats.ActiveHoursCount >= 24),
            A("pat-burst50", "집중 폭발", "하루 50 메시지 이상 달성", "local_fire_department",
                AchievementCategory.Pattern, AchievementRarity.Common,
                stats.PeakDayMessages >= 50),
            A("pat-burst100", "초집중", "하루 100 메시지 이상 달성", "whatshot",
                AchievementCategory.Pattern, AchievementRarity.Rare,
                stats.PeakDayMessages >= 100),
            A("pat-burst200", "폭풍 코딩", "하루 200 메시지 이상 달성", "bolt",
                AchievementCategory.Pattern, AchievementRarity.Epic,
                stats.PeakDayMessages >= 200),
            A("pat-burst500", "전설의 하루", "하루 500 메시지 이상 달성", "flash_on",
                AchievementCategory.Pattern, AchievementRarity.Legendary,
                stats.PeakDayMessages >= 500),
            A("pat-lunch", "런치 코더", "점심시간(11-13시) 활동 기록", "lunch_dining",
                AchievementCategory.Pattern, AchievementRarity.Common,
                stats.HourCounts[11] + stats.HourCounts[12] > 0),
            A("pat-diverse12", "시간 다양성", "12시간대 이상에서 활동", "access_time",
                AchievementCategory.Pattern, AchievementRarity.Rare,
                stats.ActiveHoursCount >= 12),
            A("pat-nocturn", "낮밤 전사", "야간+새벽 모두 100회 이상", "contrast",
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