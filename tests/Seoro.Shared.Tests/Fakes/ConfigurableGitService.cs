namespace Seoro.Shared.Tests.Fakes;

/// <summary>
///     Hand-rolled configurable <see cref="IGitService"/> double for service tests.
///     Only the members exercised by current tests expose overridable delegates;
///     everything else returns harmless defaults. Add delegates as new tests need them.
/// </summary>
internal sealed class ConfigurableGitService : IGitService
{
    public GitResult NextResult { get; set; } = new(true, "", "");

    // --- Overridable hooks (set per test) ---
    public Func<string, Task<GitResult>>? FetchAllHook { get; set; }
    public Func<string, Task<List<BranchGroup>>>? ListAllBranchesGroupedHook { get; set; }
    public Func<string, Task<string?>>? DetectDefaultBranchHook { get; set; }
    public Func<string, CancellationToken, Task<List<string>>>? GetUncommittedChangesHook { get; set; }
    public Func<string, string, string, CancellationToken, Task<(int Ahead, int Behind)?>>? FetchAndCompareHook { get; set; }
    public Func<string, string, string, CancellationToken, Task<MergeSimulationResult>>? SimulateMergeHook { get; set; }
    public Func<string, CancellationToken, Task<List<string>>>? GetStatusPorcelainHook { get; set; }

    public Task<GitResult> FetchAllAsync(string repoDir, CancellationToken ct = default)
        => FetchAllHook?.Invoke(repoDir) ?? Task.FromResult(NextResult);
    public Task<List<BranchGroup>> ListAllBranchesGroupedAsync(string repoDir)
        => ListAllBranchesGroupedHook?.Invoke(repoDir) ?? Task.FromResult<List<BranchGroup>>([]);
    public Task<string?> DetectDefaultBranchAsync(string repoDir)
        => DetectDefaultBranchHook?.Invoke(repoDir) ?? Task.FromResult<string?>("origin/main");
    public Task<List<string>> GetUncommittedChangesAsync(string workingDir, CancellationToken ct = default)
        => GetUncommittedChangesHook?.Invoke(workingDir, ct) ?? Task.FromResult<List<string>>([]);
    public Task<(int Ahead, int Behind)?> FetchAndCompareAsync(string repoDir, string sourceRef, string targetRef, CancellationToken ct = default)
        => FetchAndCompareHook?.Invoke(repoDir, sourceRef, targetRef, ct) ?? Task.FromResult<(int, int)?>((0, 0));
    public Task<MergeSimulationResult> SimulateMergeAsync(string repoDir, string sourceRef, string targetRef, CancellationToken ct = default)
        => SimulateMergeHook?.Invoke(repoDir, sourceRef, targetRef, ct) ?? Task.FromResult(new MergeSimulationResult(true, false, [], 0, 0, null));
    public Task<List<string>> GetStatusPorcelainAsync(string workingDir, CancellationToken ct = default)
        => GetStatusPorcelainHook?.Invoke(workingDir, ct) ?? Task.FromResult<List<string>>([]);

    // --- Remaining members: harmless defaults ---
    public Task<GitResult> AddWorktreeAsync(string repoDir, string worktreePath, string branchName, string baseBranch, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> RemoveWorktreeAsync(string repoDir, string worktreePath, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> CloneAsync(string url, string targetDir, IProgress<string>? progress = null, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<bool> IsGitRepoAsync(string path) => Task.FromResult(true);
    public Task<GitResult> InitAsync(string path, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<string?> GetCurrentBranchAsync(string repoDir) => Task.FromResult<string?>("main");
    public Task<List<string>> ListBranchesAsync(string repoDir) => Task.FromResult<List<string>>(["main"]);
    public Task<bool> BranchExistsAsync(string repoDir, string branchName) => Task.FromResult(false);
    public Task<GitResult> RenameBranchAsync(string workingDir, string oldName, string newName, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> DeleteBranchAsync(string repoDir, string branchName, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<bool> IsBranchMergedAsync(string repoDir, string branchName, string baseBranch, CancellationToken ct = default) => Task.FromResult(false);
    public Task<GitResult> PushBranchAsync(string repoDir, string branchName, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> PushForceBranchAsync(string repoDir, string branchName, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> FetchAsync(string repoDir, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> RebaseAsync(string workingDir, string baseBranch, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<string> GetNameStatusAsync(string workingDir, string baseBranch, CancellationToken ct = default) => Task.FromResult("");
    public Task<string> GetUnifiedDiffAsync(string workingDir, string baseBranch, CancellationToken ct = default) => Task.FromResult("");
    public Task<List<string>> ListTrackedFilesAsync(string workingDir, CancellationToken ct = default) => Task.FromResult<List<string>>([]);
    public Task<GitResult> GetCommitLogAsync(string repoDir, string baseBranch, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> GetFormattedCommitLogAsync(string repoDir, string baseBranch, int maxCount = 50, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<string> ReadFileAsync(string workingDir, string relativePath, CancellationToken ct = default) => Task.FromResult("");
    public Task WriteFileAsync(string workingDir, string relativePath, string content, CancellationToken ct = default) => Task.CompletedTask;
    public Task<DateTime?> GetFileMtimeUtcAsync(string workingDir, string relativePath) => Task.FromResult<DateTime?>(null);
    public Task<(int Additions, int Deletions)> GetDiffStatAsync(string workingDir, string baseBranch, CancellationToken ct = default) => Task.FromResult((0, 0));
    public Task<DiffSummary> GetDiffSummaryAsync(string workingDir, string baseBranch, CancellationToken ct = default) => Task.FromResult(new DiffSummary());
    public Task<string[]> ReadFileLinesAsync(string workingDir, string relativePath, int startLine, int endLine, CancellationToken ct = default) => Task.FromResult(Array.Empty<string>());
    public Task<string[]> ReadBaseFileLinesAsync(string workingDir, string baseBranch, string relativePath, int startLine, int endLine, CancellationToken ct = default) => Task.FromResult(Array.Empty<string>());
    public Task<GitResult> RunAsync(string arguments, string workingDir, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<(int Ahead, int Behind)> GetAheadBehindAsync(string workingDir, string baseBranch, CancellationToken ct = default) => Task.FromResult((0, 0));
    public Task<GitResult> StageAllAsync(string workingDir, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> CommitAsync(string workingDir, string message, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<(int Ahead, int Behind)> GetAheadBehindAsync(string workingDir, CancellationToken ct = default) => Task.FromResult((0, 0));
    public Task<List<string>> GetChangedFilesAsync(string workingDir, string baseBranch, CancellationToken ct = default) => Task.FromResult<List<string>>([]);
    public Task<GitResult> CheckoutFilesAsync(string workingDir, IEnumerable<string> relativePaths, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<string?> ResolveCommitHashAsync(string repoDir, string refName, CancellationToken ct = default) => Task.FromResult<string?>("abc123def456");
    public Task<string?> GetRemoteUrlAsync(string repoDir, string remoteName = "origin", CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<bool> HasUnresolvedConflictsAsync(string workingDir, CancellationToken ct = default) => Task.FromResult(false);
    public Task<SquashMergeResult> SquashMergeViaTempCloneAsync(string mainRepoDir, string sourceWorktreePath, string sourceBranchName, string targetBranchName, string commitMessage, IProgress<string>? progress = null, CancellationToken ct = default) => Task.FromResult(SquashMergeResult.Succeeded(""));
    public Task InvalidateBranchCacheAsync(string repoDir) => Task.CompletedTask;
    public Task InvalidateStatusCacheAsync(string workingDir) => Task.CompletedTask;
    public Task<DiffSummary> GetWorkingTreeStatusAsync(string workingDir, CancellationToken ct = default) => Task.FromResult(new DiffSummary());
    public Task<GitResult> StageFileAsync(string workingDir, string relativePath, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> UnstageFileAsync(string workingDir, string relativePath, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> DiscardFileAsync(string workingDir, string relativePath, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> PushAsync(string workingDir, bool setUpstream = false, bool force = false, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<GitResult> PullAsync(string workingDir, bool rebase = true, CancellationToken ct = default) => Task.FromResult(NextResult);
    public Task<IReadOnlyList<CommitInfo>> GetCommitHistoryAsync(string repoDir, int limit = 500, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CommitInfo>>([]);
    public Task<IReadOnlyList<(string Path, string Status)>> GetCommitChangedFilesAsync(string repoDir, string sha, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<(string, string)>>([]);
    public Task<FileDiff?> GetCommitFileDiffAsync(string repoDir, string sha, string filePath, CancellationToken ct = default) => Task.FromResult<FileDiff?>(null);
}
