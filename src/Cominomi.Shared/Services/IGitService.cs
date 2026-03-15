namespace Cominomi.Shared.Services;

public record GitResult(bool Success, string Output, string Error);

public interface IGitService
{
    Task<GitResult> CloneAsync(string url, string targetDir, IProgress<string>? progress = null, CancellationToken ct = default);
    Task<GitResult> AddWorktreeAsync(string repoDir, string worktreePath, string branchName, string baseBranch, CancellationToken ct = default);
    Task<GitResult> RemoveWorktreeAsync(string repoDir, string worktreePath, CancellationToken ct = default);
    Task<string?> DetectDefaultBranchAsync(string repoDir);
    Task<bool> IsGitRepoAsync(string path);
    Task<string?> GetCurrentBranchAsync(string repoDir);
    Task<List<string>> ListBranchesAsync(string repoDir);
    Task<bool> BranchExistsAsync(string repoDir, string branchName);
    Task<GitResult> RenameBranchAsync(string workingDir, string oldName, string newName, CancellationToken ct = default);
    Task<GitResult> DeleteBranchAsync(string repoDir, string branchName, CancellationToken ct = default);
    Task<bool> IsBranchMergedAsync(string repoDir, string branchName, string baseBranch, CancellationToken ct = default);
    Task<string> GetNameStatusAsync(string workingDir, string baseBranch, CancellationToken ct = default);
    Task<string> GetUnifiedDiffAsync(string workingDir, string baseBranch, CancellationToken ct = default);
}
