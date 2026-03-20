using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISpotlightService
{
    bool IsActive(string sessionId);
    string? GetSpotlightPath(string sessionId);
    Task StartAsync(Workspace workspace, Session session);
    Task StopAsync(string sessionId);
    Task RecoverAsync();
}
