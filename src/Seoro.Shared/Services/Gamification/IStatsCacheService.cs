
namespace Seoro.Shared.Services.Gamification;

/// <summary>
///     Reads ~/.claude/stats-cache.json (Claude CLI가 작성, 글리픽과 동일하게 수정 없음)
///     토큰/비용 데이터: stats-cache.json | 활동 데이터: history.jsonl
/// </summary>
public interface IStatsCacheService
{
    /// <summary>No-op — stats-cache.json은 Claude CLI가 관리, 수정하지 않음.</summary>
    Task ForceRefreshAsync();

    /// <summary>No-op — stats-cache.json은 Claude CLI가 관리, 수정하지 않음.</summary>
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