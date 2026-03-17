using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISessionService
{
    Task<List<Session>> GetSessionsAsync();
    Task<List<Session>> GetSessionsByWorkspaceAsync(string workspaceId);
    Task<Session> CreateSessionAsync(string model, string workspaceId, string baseBranch);
    Task<Session> CreatePendingSessionAsync(string model, string workspaceId);
    Task<Session> CreateLocalDirSessionAsync(string model, string workspaceId);
    Task<Session> InitializeWorktreeAsync(string sessionId, string baseBranch);
    Task<Session?> LoadSessionAsync(string sessionId);
    Task SaveSessionAsync(Session session);
    Task DeleteSessionAsync(string sessionId);
    Task RenameBranchAsync(string sessionId, string newBranchName);
    Task CleanupSessionAsync(string sessionId);
    Task<bool> CheckMergeStatusAsync(string sessionId);
    Task<Session> PushBranchAsync(string sessionId, bool force = false, CancellationToken ct = default);
    Task<Session> CreatePrAsync(string sessionId, string title, string body, CancellationToken ct = default);
    Task<Session> MergePrAsync(string sessionId, string mergeMethod = "squash", CancellationToken ct = default);
    Task<Session> MergeAllAsync(string sessionId, string mergeMethod = "squash", string? prBodyTemplate = null, CancellationToken ct = default);
    Task RetryAfterConflictResolveAsync(string sessionId);
}
