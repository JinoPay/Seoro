using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISessionService
{
    Task<List<Session>> GetSessionsAsync();
    Task<List<Session>> GetSessionsByWorkspaceAsync(string workspaceId);
    Task<Session> CreateSessionAsync(string workingDir, string model, string workspaceId);
    Task<Session?> LoadSessionAsync(string sessionId);
    Task SaveSessionAsync(Session session);
    Task DeleteSessionAsync(string sessionId);
}
