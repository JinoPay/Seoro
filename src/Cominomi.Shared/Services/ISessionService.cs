using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISessionService
{
    Task<List<Session>> GetSessionsAsync();
    Task<List<Session>> GetSessionsByWorkspaceAsync(string workspaceId);
    Task<Session> CreateSessionAsync(string model, string workspaceId);
    Task<Session?> LoadSessionAsync(string sessionId);
    Task SaveSessionAsync(Session session);
    Task DeleteSessionAsync(string sessionId);
    Task RenameBranchAsync(string sessionId, string newBranchName);
    Task CleanupSessionAsync(string sessionId);
    Task<bool> CheckMergeStatusAsync(string sessionId);
}
