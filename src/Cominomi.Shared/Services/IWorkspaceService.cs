using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IWorkspaceService
{
    Task<List<Workspace>> GetWorkspacesAsync();
    Task<Workspace?> LoadWorkspaceAsync(string workspaceId);
    Task SaveWorkspaceAsync(Workspace workspace);
    Task DeleteWorkspaceAsync(string workspaceId);

    Task<Workspace> CreateFromUrlAsync(string url, string name, string branchName, string baseBranch, string model, IProgress<string>? progress = null, CancellationToken ct = default);
    Task<Workspace> CreateFromLocalAsync(string localPath, string name, string branchName, string baseBranch, string model, CancellationToken ct = default);

    Task<GitRepoInfo?> FindExistingRepoAsync(string remoteUrl);
}
