
namespace Seoro.Shared.Services.Git;

public record GitResult(bool Success, string Output, string Error);

public interface IGitService
{
    Task<(int Additions, int Deletions)> GetDiffStatAsync(string workingDir, string baseBranch,
        CancellationToken ct = default);

    Task<(int Ahead, int Behind)> GetAheadBehindAsync(string workingDir, CancellationToken ct = default);

    Task<bool> IsGitRepoAsync(string path);
    Task<DiffSummary> GetDiffSummaryAsync(string workingDir, string baseBranch, CancellationToken ct = default);

    Task<GitResult> AddWorktreeAsync(string repoDir, string worktreePath, string branchName, string baseBranch,
        CancellationToken ct = default);

    Task<GitResult> CheckoutFilesAsync(string workingDir, IEnumerable<string> relativePaths,
        CancellationToken ct = default);

    Task<GitResult> CloneAsync(string url, string targetDir, IProgress<string>? progress = null,
        CancellationToken ct = default);

    Task<GitResult> CommitAsync(string workingDir, string message, CancellationToken ct = default);

    Task<GitResult> DeleteBranchAsync(string repoDir, string branchName, CancellationToken ct = default);
    Task<GitResult> FetchAllAsync(string repoDir, CancellationToken ct = default);
    Task<GitResult> FetchAsync(string repoDir, CancellationToken ct = default);
    Task<GitResult> InitAsync(string path, CancellationToken ct = default);
    Task<GitResult> RemoveWorktreeAsync(string repoDir, string worktreePath, CancellationToken ct = default);

    Task<GitResult> RenameBranchAsync(string workingDir, string oldName, string newName,
        CancellationToken ct = default);

    Task<GitResult> StageAllAsync(string workingDir, CancellationToken ct = default);

    Task<List<BranchGroup>> ListAllBranchesGroupedAsync(string repoDir);
    Task<List<string>> GetChangedFilesAsync(string workingDir, string baseBranch, CancellationToken ct = default);

    Task<List<string>> GetStatusPorcelainAsync(string workingDir, CancellationToken ct = default);
    Task<List<string>> ListTrackedFilesAsync(string workingDir, CancellationToken ct = default);
    Task<string?> DetectDefaultBranchAsync(string repoDir);
    Task<string?> GetCurrentBranchAsync(string repoDir);
    Task<string?> ResolveCommitHashAsync(string repoDir, string refName, CancellationToken ct = default);

    Task<string[]> ReadBaseFileLinesAsync(string workingDir, string baseBranch, string relativePath, int startLine,
        int endLine, CancellationToken ct = default);

    Task<string[]> ReadFileLinesAsync(string workingDir, string relativePath, int startLine, int endLine,
        CancellationToken ct = default);

    Task<string> GetNameStatusAsync(string workingDir, string baseBranch, CancellationToken ct = default);
    Task<string> ReadFileAsync(string workingDir, string relativePath, CancellationToken ct = default);
}