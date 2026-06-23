
namespace Seoro.Shared.Services.Sessions;

public interface ISessionService
{
    Task CleanupSessionAsync(string sessionId);
    Task DeleteSessionAsync(string sessionId);
    Task SaveSessionAsync(Session session);
    Task<List<Session>> GetSessionsAsync();
    Task<List<Session>> GetSessionsByWorkspaceAsync(string workspaceId);
    Task<Session?> LoadSessionAsync(string sessionId);
    Task<Session> CreateLocalDirSessionAsync(string model, string workspaceId, string provider = "claude");
    Task<Session> CreatePendingSessionAsync(string model, string workspaceId, string provider = "claude");
    Task<Session> InitializeWorktreeAsync(string sessionId, string baseBranch);
    Task<Session> RebaseWorktreeAsync(string sessionId, string newBaseBranch);
}