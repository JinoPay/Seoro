
namespace Seoro.Shared.Services.Gamification;

/// <summary>
///     Reads ~/.claude/stats-cache.json and merges with usage.jsonl
///     to provide complete historical usage data.
/// </summary>
public interface IStatsCacheService
{
    /// <summary>
    ///     Forces a refresh of stats-cache.json by scanning session JSONL files,
    ///     regardless of staleness. Used by manual refresh buttons.
    /// </summary>
    Task ForceRefreshAsync();

    /// <summary>
    ///     Refreshes stats-cache.json by scanning session JSONL files
    ///     if the cache is stale (lastComputedDate is not today).
    ///     Returns true if a refresh was performed.
    /// </summary>
    Task<bool> RefreshIfStaleAsync();

    /// <summary>
    ///     Computes live activity stats from ~/.claude/history.jsonl.
    ///     Returns null if history.jsonl does not exist.
    ///     Merges tool call counts from stats-cache.json dailyActivity.
    /// </summary>
    Task<LiveActivityStats?> ComputeLiveActivityAsync();

    /// <summary>
    ///     Returns merged UsageStats from stats-cache.json + usage.jsonl.
    ///     Falls back to usage.jsonl only if stats-cache.json is unavailable.
    /// </summary>
    Task<UsageStats> GetMergedStatsAsync(int? days = null);
}