namespace Cominomi.Shared.Models;

public record SessionSyncResult(
    SessionStatus? NewStatus,
    MergeReadiness? Readiness,
    bool WasFetched,
    string? PrState,
    string? ErrorMessage,
    int? CommitsAhead = null,
    int? CommitsBehind = null)
{
    public static SessionSyncResult Skipped { get; } = new(null, null, false, null, null);
}
