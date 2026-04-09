using System.Text.Json;
using Seoro.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services;

/// <summary>
///     Reads ~/.claude/stats-cache.json (Claude CLI external indexer)
///     to provide complete historical usage stats.
///     When the cache is stale, refreshes by scanning session JSONL files directly.
/// </summary>
public class StatsCacheService(ILogger<StatsCacheService> logger) : IStatsCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "history.jsonl");

    private static readonly string ProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects");

    private static readonly string StatsCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "stats-cache.json");

    public async Task ForceRefreshAsync()
    {
        await RefreshLock.WaitAsync();
        try
        {
            if (!Directory.Exists(ProjectsDir))
                return;

            var cache = await ReadStatsCacheAsync();
            await RefreshFromSessionsAsync(cache);
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    public async Task<bool> RefreshIfStaleAsync()
    {
        if (!await RefreshLock.WaitAsync(0))
            return false; // Another refresh is already running

        try
        {
            var cache = await ReadStatsCacheAsync();
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            if (cache != null && cache.Version >= 3 && cache.LastComputedDate == today
                && cache.DailyModelTokens.Count > 0 && cache.ModelUsage.Count > 0)
            {
                // Date matches today, but check if any session file is newer than the cache
                if (File.Exists(StatsCachePath) && Directory.Exists(ProjectsDir))
                {
                    var cacheLastWrite = File.GetLastWriteTimeUtc(StatsCachePath);
                    var anyNewer = Directory.EnumerateFiles(ProjectsDir, "*.jsonl", SearchOption.AllDirectories)
                        .Any(f => File.GetLastWriteTimeUtc(f) > cacheLastWrite);
                    if (!anyNewer)
                        return false; // Cache is truly fresh
                }
                else
                {
                    return false;
                }
            }

            if (!Directory.Exists(ProjectsDir))
                return false;

            await RefreshFromSessionsAsync(cache);
            return true;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

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
        stats.TotalTokens = stats.TotalInputTokens + stats.TotalOutputTokens;
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

    /// <summary>
    ///     Scans all session JSONL files to rebuild dailyModelTokens and modelUsage,
    ///     preserving other fields from the existing cache.
    /// </summary>
    private async Task RefreshFromSessionsAsync(StatsCache? existingCache)
    {
        var dailyModelTokens = new Dictionary<string, Dictionary<string, DailyModelTokenBreakdown>>();
        var modelUsage = new Dictionary<string, StatsCacheModelUsage>();

        var files = Directory.EnumerateFiles(ProjectsDir, "*.jsonl", SearchOption.AllDirectories);

        foreach (var file in files)
            try
            {
                using var reader = new StreamReader(file);
                while (await reader.ReadLineAsync() is { } line)
                {
                    // Fast filter: skip lines that can't be assistant messages with usage
                    if (!line.Contains("\"assistant\"") || !line.Contains("\"usage\""))
                        continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        if (!root.TryGetProperty("type", out var typeProp) ||
                            typeProp.GetString() != "assistant")
                            continue;

                        if (!root.TryGetProperty("message", out var msg) ||
                            msg.ValueKind != JsonValueKind.Object)
                            continue;

                        if (!msg.TryGetProperty("usage", out var usage))
                            continue;

                        // Extract timestamp → date (supports both ISO 8601 string and Unix ms number)
                        if (!root.TryGetProperty("timestamp", out var tsProp))
                            continue;

                        string dateStr;
                        if (tsProp.ValueKind == JsonValueKind.String)
                        {
                            var tsStr = tsProp.GetString();
                            if (tsStr == null || !DateTimeOffset.TryParse(tsStr, out var dto))
                                continue;
                            dateStr = dto.UtcDateTime.ToString("yyyy-MM-dd");
                        }
                        else if (tsProp.ValueKind == JsonValueKind.Number)
                        {
                            var tsMs = (long)tsProp.GetDouble();
                            if (tsMs <= 0) continue;
                            dateStr = DateTimeOffset.FromUnixTimeMilliseconds(tsMs)
                                .UtcDateTime.ToString("yyyy-MM-dd");
                        }
                        else
                        {
                            continue;
                        }

                        var model = msg.TryGetProperty("model", out var mp)
                            ? mp.GetString() ?? "unknown"
                            : "unknown";

                        var inputTokens = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt64() : 0;
                        var outputTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt64() : 0;
                        var cacheCreation = usage.TryGetProperty("cache_creation_input_tokens", out var cc)
                            ? cc.GetInt64()
                            : 0;
                        var cacheRead = usage.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt64() : 0;

                        // dailyModelTokens (per-model breakdown)
                        if (!dailyModelTokens.TryGetValue(dateStr, out var dayTokens))
                            dailyModelTokens[dateStr] = dayTokens = new Dictionary<string, DailyModelTokenBreakdown>();
                        if (!dayTokens.TryGetValue(model, out var breakdown))
                            dayTokens[model] = breakdown = new DailyModelTokenBreakdown();
                        breakdown.InputTokens += inputTokens;
                        breakdown.OutputTokens += outputTokens;
                        breakdown.CacheCreationInputTokens += cacheCreation;
                        breakdown.CacheReadInputTokens += cacheRead;

                        // modelUsage
                        if (!modelUsage.TryGetValue(model, out var mu))
                            modelUsage[model] = mu = new StatsCacheModelUsage();
                        mu.InputTokens += inputTokens;
                        mu.OutputTokens += outputTokens;
                        mu.CacheCreationInputTokens += cacheCreation;
                        mu.CacheReadInputTokens += cacheRead;
                    }
                    catch
                    {
                        /* skip unparseable lines */
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "통계 세션 파일 읽기 실패: {File}", file);
            }

        // Safety: don't overwrite existing data with empty results
        if (dailyModelTokens.Count == 0 && modelUsage.Count == 0)
            return;

        // Build updated cache, preserving fields we don't compute
        var updated = existingCache ?? new StatsCache();
        updated.LastComputedDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        updated.DailyModelTokens = dailyModelTokens
            .Select(kv => new StatsCacheDailyModelTokens { Date = kv.Key, TokensByModel = kv.Value })
            .OrderBy(d => d.Date)
            .ToList();
        updated.ModelUsage = modelUsage;
        updated.Version = 3;

        // Write back to disk
        try
        {
            var json = JsonSerializer.Serialize(updated, WriteOptions);
            await File.WriteAllTextAsync(StatsCachePath, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "통계 캐시를 디스크에 쓰기 실패");
        }
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