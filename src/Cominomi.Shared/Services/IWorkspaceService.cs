using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IWorkspaceService
{
    Task<List<Workspace>> GetWorkspacesAsync();
    Task<Workspace?> LoadWorkspaceAsync(string workspaceId);
    Task SaveWorkspaceAsync(Workspace workspace);
    Task DeleteWorkspaceAsync(string workspaceId);
    Task EnsureDefaultWorkspaceAsync();
}
