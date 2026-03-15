using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISpotlightService
{
    bool IsActive(string workspaceId);
    Task StartAsync(Workspace workspace);
    Task StopAsync(Workspace workspace);
}
