using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IWorkspaceService
{
    event Action<Workspace>? OnWorkspaceSaved;
    Task DeleteWorkspaceAsync(string workspaceId);
    Task SaveWorkspaceAsync(Workspace workspace);

    Task<GitRepoInfo?> FindExistingRepoAsync(string remoteUrl);

    Task<List<Workspace>> GetWorkspacesAsync();
    Task<string> GetWorktreesDirAsync();
    Task<Workspace?> LoadWorkspaceAsync(string workspaceId);
    Task<Workspace> CreateFromLocalAsync(string localPath, string name, string model, CancellationToken ct = default);

    Task<Workspace> CreateFromUrlAsync(string url, string name, string model, IProgress<string>? progress = null,
        CancellationToken ct = default);
}