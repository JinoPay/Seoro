using System.Text.Json;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

/// <summary>
/// Reads ~/.claude/stats-cache.json (Claude CLI external indexer)
/// to provide complete historical usage stats.
/// </summary>
public class StatsCacheService : IStatsCacheService
{
    private static readonly string StatsCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "stats-cache.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<UsageStats> GetMergedStatsAsync(int? days = null)
    {
        var cache = await ReadStatsCacheAsync();
        if (cache == null)
            return new UsageStats();

        return BuildStats(cache, days);
    }

    private static async Task<StatsCache?> ReadStatsCacheAsync()
    {
        try
        {
            if (!File.Exists(StatsCachePath))
                return null;

            var json = await File.ReadAllTextAsync(StatsCachePath);
            return JsonSerializer.Deserialize<StatsCache>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static UsageStats BuildStats(StatsCache cache, int? days)
    {
        var cutoff = days.HasValue
            ? DateTime.UtcNow.Date.AddDays(-days.Value).ToString("yyyy-MM-dd")
            : "0000-01-01";

        var stats = new UsageStats();

        // ── 1. Model usage ──
        var modelMap = new Dictionary<string, ModelUsage>();
        foreach (var (modelId, usage) in cache.ModelUsage)
        {
            var normalized = ModelDefinitions.NormalizeModelId(modelId);
            if (!modelMap.TryGetValue(normalized, out var mu))
            {
                mu = new ModelUsage { Model = normalized };
                modelMap[normalized] = mu;
            }
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
            {
                mu.TotalCost = (decimal)mu.InputTokens / 1_000_000m * pricing.Input
                             + (decimal)mu.OutputTokens / 1_000_000m * pricing.Output
                             + (decimal)mu.CacheCreationTokens / 1_000_000m * pricing.CacheWrite
                             + (decimal)mu.CacheReadTokens / 1_000_000m * pricing.CacheRead;
            }
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
            foreach (var (model, tokens) in day.TokensByModel)
            {
                var normalized = ModelDefinitions.NormalizeModelId(model);
                dtt.TotalTokens += tokens;
                dtt.TokensByModel[normalized] = dtt.TokensByModel.GetValueOrDefault(normalized) + tokens;
            }
            dailyMap[day.Date] = dtt;
        }
        stats.DailyTokenTrend = dailyMap.Values.OrderBy(d => d.Date).ToList();

        // ── 4. Hour counts ──
        stats.HourCounts = new int[24];
        foreach (var (hourStr, count) in cache.HourCounts)
        {
            if (int.TryParse(hourStr, out var h) && h is >= 0 and < 24)
                stats.HourCounts[h] += count;
        }

        // ── 5. First session date ──
        stats.FirstSessionDate = cache.FirstSessionDate;

        // ── 6. Longest session ──
        if (cache.LongestSession != null)
        {
            stats.LongestSession = new LongestSessionInfo
            {
                SessionId = cache.LongestSession.SessionId,
                DurationMs = cache.LongestSession.Duration,
                MessageCount = cache.LongestSession.MessageCount
            };
        }

        return stats;
    }
}
