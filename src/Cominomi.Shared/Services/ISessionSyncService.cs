using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISessionSyncService
{
    Task<SessionSyncResult> SyncAsync(string sessionId, CancellationToken ct = default);
}
