
namespace Seoro.Shared.Services.Git;

public interface IWorktreeSyncService : IDisposable
{
    bool IsSyncActive { get; }
    string? SyncedSessionId { get; }
    bool IsSessionSynced(string sessionId);
    Task RecoverFromCrashAsync();
    Task StopSyncAsync(CancellationToken ct = default);

    Task<bool> StartSyncAsync(Session session, Workspace workspace, CancellationToken ct = default);
}