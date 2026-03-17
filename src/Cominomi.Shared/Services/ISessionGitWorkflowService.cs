using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISessionGitWorkflowService
{
    Task<bool> CheckMergeStatusAsync(string sessionId);
    Task<Session> PushBranchAsync(string sessionId, bool force = false, CancellationToken ct = default);
    Task<Session> CreatePrAsync(string sessionId, string title, string body, CancellationToken ct = default);
    Task<Session> MergePrAsync(string sessionId, string mergeMethod = "squash", CancellationToken ct = default);
    Task<Session> MergeAllAsync(string sessionId, string mergeMethod = "squash", string? prBodyTemplate = null, CancellationToken ct = default);
    Task RetryAfterConflictResolveAsync(string sessionId);
}
