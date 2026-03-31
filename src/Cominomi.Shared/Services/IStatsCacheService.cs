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

    /// <summary>
    /// Refreshes stats-cache.json by scanning session JSONL files
    /// if the cache is stale (lastComputedDate is not today).
    /// Returns true if a refresh was performed.
    /// </summary>
    Task<bool> RefreshIfStaleAsync();

    /// <summary>
    /// Forces a refresh of stats-cache.json by scanning session JSONL files,
    /// regardless of staleness. Used by manual refresh buttons.
    /// </summary>
    Task ForceRefreshAsync();

    /// <summary>
    /// Computes live activity stats from ~/.claude/history.jsonl.
    /// Returns null if history.jsonl does not exist.
    /// Merges tool call counts from stats-cache.json dailyActivity.
    /// </summary>
    Task<LiveActivityStats?> ComputeLiveActivityAsync();
}
