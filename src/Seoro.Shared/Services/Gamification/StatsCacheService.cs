using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Gamification;

/// <summary>
///     Reads ~/.claude/stats-cache.json (Claude CLI 외부 인덱서가 작성)
///     as-is — 글리픽과 동일하게 파일을 수정하지 않고 그대로 읽습니다.
///     토큰/비용 데이터: stats-cache.json (Claude CLI 신뢰)
///     활동 데이터: history.jsonl (ComputeLiveActivityAsync)
/// </summary>
public class StatsCacheService(ILogger<StatsCacheService> logger) : IStatsCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "history.jsonl");

    private static readonly string StatsCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "stats-cache.json");

    /// <summary>글리픽과 동일하게 stats-cache.json을 수정하지 않으므로 no-op.</summary>
    public Task ForceRefreshAsync() => Task.CompletedTask;

    /// <summary>글리픽과 동일하게 stats-cache.json을 수정하지 않으므로 no-op.</summary>
    public Task<bool> RefreshIfStaleAsync() => Task.FromResult(false);

    public async Task<LiveActivityStats?> ComputeLiveActivityAsync()
    {
        if (!File.Exists(HistoryPath))
            return null;

        return await Task.Run(async () =>
        {
            var messagesByDate = new Dictionary<string, int>();
            var sessionsByDate = new Dictionary<string, HashSet<string>>();
            var hourCounts = new Dictionary<string, int>();
            var allSessions = new HashSet<string>();
            var totalMessages = 0;
            string firstDate = "", lastDate = "";

            try
            {
                using var fs = new FileStream(HistoryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        // Extract timestamp (Unix ms number)
                        if (!root.TryGetProperty("timestamp", out var tsProp)) continue;
                        double tsMs = 0;
                        if (tsProp.ValueKind == JsonValueKind.Number)
                            tsMs = tsProp.GetDouble();
                        else if (tsProp.ValueKind == JsonValueKind.String &&
                                 double.TryParse(tsProp.GetString(), out var parsed))
                            tsMs = parsed;
                        if (tsMs <= 0) continue;

                        var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)tsMs).ToLocalTime();
                        var date = dto.ToString("yyyy-MM-dd");
                        var hour = dto.Hour.ToString();

                        var sessionId = root.TryGetProperty("sessionId", out var sidProp)
                            ? sidProp.GetString() ?? ""
                            : "";

                        messagesByDate[date] = messagesByDate.GetValueOrDefault(date) + 1;
                        if (!sessionsByDate.TryGetValue(date, out var sessions))
                            sessionsByDate[date] = sessions = new HashSet<string>();
                        sessions.Add(sessionId);
                        hourCounts[hour] = hourCounts.GetValueOrDefault(hour) + 1;
                        allSessions.Add(sessionId);
                        totalMessages++;

                        if (firstDate == "" || string.Compare(date, firstDate, StringComparison.Ordinal) < 0)
                            firstDate = date;
                        if (lastDate == "" || string.Compare(date, lastDate, StringComparison.Ordinal) > 0)
                            lastDate = date;
                    }
                    catch
                    {
                        /* skip unparseable lines */
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "라이브 활동 통계 계산 실패");
                return null;
            }

            // Build daily activity sorted by date
            var dates = messagesByDate.Keys.OrderBy(d => d).ToList();

            // Merge tool call counts from stats-cache.json dailyActivity (if available)
            var cache = await ReadStatsCacheAsync();
            var cachedToolCalls = new Dictionary<string, int>();
            if (cache?.DailyActivity != null)
                foreach (var d in cache.DailyActivity)
                    cachedToolCalls[d.Date] = d.ToolCallCount;

            var dailyActivity = dates.Select(date => new LiveDailyActivity
            {
                Date = date,
                MessageCount = messagesByDate.GetValueOrDefault(date),
                SessionCount = sessionsByDate.TryGetValue(date, out var s) ? s.Count : 0,
                ToolCallCount = cachedToolCalls.GetValueOrDefault(date)
            }).ToList();

            return new LiveActivityStats
            {
                DailyActivity = dailyActivity,
                TotalSessions = allSessions.Count,
                TotalMessages = totalMessages,
                FirstSessionDate = firstDate,
                LastSessionDate = lastDate,
                HourCounts = hourCounts
            };
        });
    }

    public async Task<UsageStats> GetMergedStatsAsync(int? days = null)
    {
        var cache = await ReadStatsCacheAsync();
        if (cache == null)
            return new UsageStats();

        return BuildStatsCore(cache, days);
    }

    private static UsageStats BuildStatsCore(StatsCache cache, int? days)
    {
        var cutoff = days.HasValue
            ? DateTime.UtcNow.Date.AddDays(-(days.Value - 1)).ToString("yyyy-MM-dd")
            : "0000-01-01";

        var stats = new UsageStats();

        // ── 1. Model usage ──
        var modelMap = new Dictionary<string, ModelUsage>();
        if (days.HasValue)
            // Period-filtered: aggregate directly from DailyModelTokens breakdown
            foreach (var day in cache.DailyModelTokens)
            {
                if (string.Compare(day.Date, cutoff, StringComparison.Ordinal) < 0) continue;
                foreach (var (model, bd) in day.TokensByModel)
                {
                    var normalized = ModelDefinitions.NormalizeModelId(model);
                    if (!modelMap.TryGetValue(normalized, out var mu))
                        modelMap[normalized] = mu = new ModelUsage { Model = normalized };
                    mu.InputTokens += bd.InputTokens;
                    mu.OutputTokens += bd.OutputTokens;
                    mu.CacheCreationTokens += bd.CacheCreationInputTokens;
                    mu.CacheReadTokens += bd.CacheReadInputTokens;
                }
            }
        else
            // All-time: use ModelUsage directly (already has full breakdown)
            foreach (var (modelId, usage) in cache.ModelUsage)
            {
                var normalized = ModelDefinitions.NormalizeModelId(modelId);
                if (!modelMap.TryGetValue(normalized, out var mu))
                    modelMap[normalized] = mu = new ModelUsage { Model = normalized };
                mu.InputTokens += usage.InputTokens;
                mu.OutputTokens += usage.OutputTokens;
                mu.CacheCreationTokens += usage.CacheCreationInputTokens;
                mu.CacheReadTokens += usage.CacheReadInputTokens;
            }

        // Calculate costs per model
        foreach (var mu in modelMap.Values)
        {
            mu.TotalTokens = mu.InputTokens + mu.OutputTokens + mu.CacheCreationTokens + mu.CacheReadTokens;
            var pricing = ModelDefinitions.GetPricing(mu.Model);
            if (pricing != null)
                mu.TotalCost = ModelDefinitions.CalculateTieredCost(
                    pricing, mu.InputTokens, mu.OutputTokens,
                    mu.CacheCreationTokens, mu.CacheReadTokens);
        }

        var grandTotal = modelMap.Values.Sum(m => m.TotalTokens);
        foreach (var m in modelMap.Values)
            m.Percentage = grandTotal > 0 ? (double)m.TotalTokens / grandTotal * 100 : 0;

        stats.ByModel = modelMap.Values.OrderByDescending(m => m.TotalCost).ToList();

        // ── 2. Aggregate totals ──
        stats.TotalInputTokens = modelMap.Values.Sum(m => m.InputTokens);
        stats.TotalOutputTokens = modelMap.Values.Sum(m => m.OutputTokens);
        stats.TotalCacheCreationTokens = modelMap.Values.Sum(m => m.CacheCreationTokens);
        stats.TotalCacheReadTokens = modelMap.Values.Sum(m => m.CacheReadTokens);
        stats.TotalTokens = stats.TotalInputTokens + stats.TotalOutputTokens
                            + stats.TotalCacheCreationTokens + stats.TotalCacheReadTokens;
        stats.TotalCost = modelMap.Values.Sum(m => m.TotalCost);
        stats.TotalSessions = cache.TotalSessions;
        stats.TotalMessages = cache.TotalMessages;

        // ── 3. Daily token trend ──
        var dailyMap = new Dictionary<string, DailyTokenTrend>();
        foreach (var day in cache.DailyModelTokens)
        {
            if (string.Compare(day.Date, cutoff, StringComparison.Ordinal) < 0) continue;

            var dtt = new DailyTokenTrend { Date = day.Date };
            foreach (var (model, bd) in day.TokensByModel)
            {
                var normalized = ModelDefinitions.NormalizeModelId(model);
                dtt.TotalTokens += bd.Total;
                dtt.TokensByModel[normalized] = dtt.TokensByModel.GetValueOrDefault(normalized) + bd.Total;
            }

            dailyMap[day.Date] = dtt;
        }

        stats.DailyTokenTrend = dailyMap.Values.OrderBy(d => d.Date).ToList();

        // ── 3.5 Compute DailyCost on each trend entry ──
        foreach (var dtt in stats.DailyTokenTrend)
        {
            // Find the raw day data to compute per-model cost
            var rawDay = cache.DailyModelTokens.FirstOrDefault(d => d.Date == dtt.Date);
            if (rawDay != null)
            {
                decimal dayCost = 0;
                foreach (var (model, bd) in rawDay.TokensByModel)
                {
                    var normalized = ModelDefinitions.NormalizeModelId(model);
                    var pricing = ModelDefinitions.GetPricing(normalized);
                    if (pricing != null)
                        dayCost += ModelDefinitions.CalculateTieredCost(
                            pricing, bd.InputTokens, bd.OutputTokens,
                            bd.CacheCreationInputTokens, bd.CacheReadInputTokens);
                }

                dtt.DailyCost = dayCost;
            }
        }

        // ── 4. Hour counts ──
        stats.HourCounts = new int[24];
        foreach (var (hourStr, count) in cache.HourCounts)
            if (int.TryParse(hourStr, out var h) && h is >= 0 and < 24)
                stats.HourCounts[h] += count;

        // ── 5. First session date ──
        stats.FirstSessionDate = cache.FirstSessionDate;

        // ── 6. Longest session ──
        if (cache.LongestSession != null)
            stats.LongestSession = new LongestSessionInfo
            {
                SessionId = cache.LongestSession.SessionId,
                DurationMs = cache.LongestSession.Duration,
                MessageCount = cache.LongestSession.MessageCount
            };

        // ── 7. DailyActivity from cache ──
        if (cache.DailyActivity.Count > 0)
            stats.DailyActivity = cache.DailyActivity
                .Where(d => string.Compare(d.Date, cutoff, StringComparison.Ordinal) >= 0)
                .Select(d => new DailyActivityEntry
                {
                    Date = d.Date,
                    MessageCount = d.MessageCount,
                    SessionCount = d.SessionCount,
                    ToolCallCount = d.ToolCallCount
                })
                .OrderBy(d => d.Date)
                .ToList();

        return stats;
    }

    private async Task<StatsCache?> ReadStatsCacheAsync()
    {
        try
        {
            if (!File.Exists(StatsCachePath))
                return null;

            var json = await File.ReadAllTextAsync(StatsCachePath);
            return JsonSerializer.Deserialize<StatsCache>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "통계 캐시 파일 읽기 실패");
            return null;
        }
    }
}