using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

/// <summary>
/// Reads ~/.claude/stats-cache.json and merges with usage.jsonl
/// to provide complete historical usage data.
/// </summary>
public interface IStatsCacheService
{
    /// <summary>
    /// Returns merged UsageStats from stats-cache.json + usage.jsonl.
    /// Falls back to usage.jsonl only if stats-cache.json is unavailable.
    /// </summary>
    Task<UsageStats> GetMergedStatsAsync(int? days = null);
}
