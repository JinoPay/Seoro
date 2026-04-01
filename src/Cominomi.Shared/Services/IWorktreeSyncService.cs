using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IWorktreeSyncService : IDisposable
{
    bool IsSyncActive { get; }
    string? SyncedSessionId { get; }

    Task<bool> StartSyncAsync(Session session, Workspace workspace, CancellationToken ct = default);
    Task StopSyncAsync(CancellationToken ct = default);
    bool IsSessionSynced(string sessionId);
    Task RecoverFromCrashAsync();
}
