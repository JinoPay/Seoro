using Seoro.Shared.Models;

namespace Seoro.Shared.Services;

public interface ISessionService
{
    Task CleanupSessionAsync(string sessionId);
    Task DeleteSessionAsync(string sessionId);
    Task RenameBranchAsync(string sessionId, string newBranchName);
    Task SaveSessionAsync(Session session);
    Task<List<Session>> GetSessionsAsync();
    Task<List<Session>> GetSessionsByWorkspaceAsync(string workspaceId);
    Task<Session?> LoadSessionAsync(string sessionId);
    Task<Session> CreateLocalDirSessionAsync(string model, string workspaceId);
    Task<Session> CreatePendingSessionAsync(string model, string workspaceId);
    Task<Session> CreateSessionAsync(string model, string workspaceId, string baseBranch);
    Task<Session> InitializeWorktreeAsync(string sessionId, string baseBranch);
    Task<Session> RebaseWorktreeAsync(string sessionId, string newBaseBranch);
}