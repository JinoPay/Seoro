using Cominomi.Shared.Models;
using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Tests;

public class GitServiceTests
{
    private readonly StubProcessRunner _processRunner = new();
    private readonly GitService _sut;

    public GitServiceTests()
    {
        _sut = new GitService(
            NullLogger<GitService>.Instance,
            _processRunner,
            new FakeOptionsMonitor(),
            new FakeShellService());
    }

    // --- IsGitRepoAsync ---

    [Fact]
    public async Task IsGitRepoAsync_NonExistentDir_ReturnsFalse()
    {
        var result = await _sut.IsGitRepoAsync("/nonexistent/path/xyz");
        Assert.False(result);
    }

    // --- GetCurrentBranchAsync ---

    [Fact]
    public async Task GetCurrentBranchAsync_Success_ReturnsBranchName()
    {
        _processRunner.NextResult = new ProcessResult(true, "feature/test\n", "", 0);
        var result = await _sut.GetCurrentBranchAsync("/repo");
        Assert.Equal("feature/test", result);
    }

    [Fact]
    public async Task GetCurrentBranchAsync_Failure_ReturnsNull()
    {
        _processRunner.NextResult = new ProcessResult(false, "", "fatal: not a git repository", 128);
        var result = await _sut.GetCurrentBranchAsync("/repo");
        Assert.Null(result);
    }

    // --- BranchExistsAsync ---

    [Fact]
    public async Task BranchExistsAsync_LocalExists_ReturnsTrue()
    {
        _processRunner.ResultQueue.Enqueue(new ProcessResult(true, "", "", 0));
        var result = await _sut.BranchExistsAsync("/repo", "main");
        Assert.True(result);
    }

    [Fact]
    public async Task BranchExistsAsync_OnlyRemoteExists_ReturnsTrue()
    {
        _processRunner.ResultQueue.Enqueue(new ProcessResult(false, "", "", 1)); // local: no
        _processRunner.ResultQueue.Enqueue(new ProcessResult(true, "", "", 0));  // remote: yes
        var result = await _sut.BranchExistsAsync("/repo", "main");
        Assert.True(result);
    }

    [Fact]
    public async Task BranchExistsAsync_NeitherExists_ReturnsFalse()
    {
        _processRunner.ResultQueue.Enqueue(new ProcessResult(false, "", "", 1));
        _processRunner.ResultQueue.Enqueue(new ProcessResult(false, "", "", 1));
        var result = await _sut.BranchExistsAsync("/repo", "feature/gone");
        Assert.False(result);
    }

    // --- ListBranchesAsync ---

    [Fact]
    public async Task ListBranchesAsync_ParsesBranches()
    {
        _processRunner.NextResult = new ProcessResult(true, "main\nfeature/a\nfeature/b\n", "", 0);
        var result = await _sut.ListBranchesAsync("/repo");
        Assert.Equal(["main", "feature/a", "feature/b"], result);
    }

    [Fact]
    public async Task ListBranchesAsync_Failure_ReturnsEmpty()
    {
        _processRunner.NextResult = new ProcessResult(false, "", "error", 1);
        var result = await _sut.ListBranchesAsync("/repo");
        Assert.Empty(result);
    }

    // --- ListTrackedFilesAsync ---

    [Fact]
    public async Task ListTrackedFilesAsync_ParsesFileList()
    {
        _processRunner.NextResult = new ProcessResult(true, "src/a.cs\nsrc/b.cs\n", "", 0);
        var result = await _sut.ListTrackedFilesAsync("/repo");
        Assert.Equal(["src/a.cs", "src/b.cs"], result);
    }

    [Fact]
    public async Task ListTrackedFilesAsync_Failure_ReturnsEmpty()
    {
        _processRunner.NextResult = new ProcessResult(false, "", "error", 1);
        var result = await _sut.ListTrackedFilesAsync("/repo");
        Assert.Empty(result);
    }

    // --- GetDiffStatAsync ---

    [Fact]
    public async Task GetDiffStatAsync_ParsesInsertionsAndDeletions()
    {
        _processRunner.NextResult = new ProcessResult(true, " 3 files changed, 36 insertions(+), 16 deletions(-)", "", 0);
        var (additions, deletions) = await _sut.GetDiffStatAsync("/repo", "main");
        Assert.Equal(36, additions);
        Assert.Equal(16, deletions);
    }

    [Fact]
    public async Task GetDiffStatAsync_InsertionsOnly()
    {
        _processRunner.NextResult = new ProcessResult(true, " 1 file changed, 10 insertions(+)", "", 0);
        var (additions, deletions) = await _sut.GetDiffStatAsync("/repo", "main");
        Assert.Equal(10, additions);
        Assert.Equal(0, deletions);
    }

    [Fact]
    public async Task GetDiffStatAsync_DeletionsOnly()
    {
        _processRunner.NextResult = new ProcessResult(true, " 2 files changed, 5 deletions(-)", "", 0);
        var (additions, deletions) = await _sut.GetDiffStatAsync("/repo", "main");
        Assert.Equal(0, additions);
        Assert.Equal(5, deletions);
    }

    [Fact]
    public async Task GetDiffStatAsync_EmptyOutput_ReturnsZeros()
    {
        _processRunner.NextResult = new ProcessResult(true, "", "", 0);
        var (additions, deletions) = await _sut.GetDiffStatAsync("/repo", "main");
        Assert.Equal(0, additions);
        Assert.Equal(0, deletions);
    }

    [Fact]
    public async Task GetDiffStatAsync_Failure_ReturnsZeros()
    {
        _processRunner.NextResult = new ProcessResult(false, "", "error", 1);
        var (additions, deletions) = await _sut.GetDiffStatAsync("/repo", "main");
        Assert.Equal(0, additions);
        Assert.Equal(0, deletions);
    }

    // --- DetectDefaultBranchAsync (cache behavior) ---

    [Fact]
    public async Task DetectDefaultBranchAsync_CachesResult()
    {
        _processRunner.NextResult = new ProcessResult(true, "refs/remotes/origin/main\n", "", 0);
        var first = await _sut.DetectDefaultBranchAsync("/repo");
        Assert.Equal("origin/main", first);

        // Second call should use cache (not hit processRunner again)
        _processRunner.NextResult = new ProcessResult(true, "refs/remotes/origin/develop\n", "", 0);
        var second = await _sut.DetectDefaultBranchAsync("/repo");
        Assert.Equal("origin/main", second); // still cached
    }

    [Fact]
    public async Task DetectDefaultBranchAsync_SymbolicRefFails_FallsBackToMain()
    {
        // symbolic-ref fails
        _processRunner.ResultQueue.Enqueue(new ProcessResult(false, "", "fatal", 1));
        // branch exists "main" — local check
        _processRunner.ResultQueue.Enqueue(new ProcessResult(true, "", "", 0));

        var result = await _sut.DetectDefaultBranchAsync("/repo");
        Assert.Equal("origin/main", result);
    }

    // --- FetchAsync (cache invalidation) ---

    [Fact]
    public async Task FetchAsync_InvalidatesBranchCache()
    {
        // Populate branch list cache
        _processRunner.NextResult = new ProcessResult(true, "main\ndev\n", "", 0);
        var branches = await _sut.ListBranchesAsync("/repo");
        Assert.Equal(2, branches.Count);

        // Fetch succeeds → cache should be invalidated
        _processRunner.NextResult = new ProcessResult(true, "", "", 0);
        await _sut.FetchAsync("/repo");

        // Next ListBranches should hit processRunner again
        _processRunner.NextResult = new ProcessResult(true, "main\ndev\nfeature/new\n", "", 0);
        branches = await _sut.ListBranchesAsync("/repo");
        Assert.Equal(3, branches.Count);
    }

    // --- RebaseAsync (auto-abort on failure) ---

    [Fact]
    public async Task RebaseAsync_OnFailure_AutoAborts()
    {
        _processRunner.ResultQueue.Enqueue(new ProcessResult(false, "", "CONFLICT", 1)); // rebase fails
        _processRunner.ResultQueue.Enqueue(new ProcessResult(true, "", "", 0)); // abort succeeds

        var result = await _sut.RebaseAsync("/repo", "main");

        Assert.False(result.Success);
        // Should have called rebase then rebase --abort
        Assert.Equal(2, _processRunner.Invocations.Count);
        Assert.Contains("--abort", _processRunner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task RebaseAsync_OnSuccess_NoAbort()
    {
        _processRunner.NextResult = new ProcessResult(true, "", "", 0);

        var result = await _sut.RebaseAsync("/repo", "main");

        Assert.True(result.Success);
        Assert.Single(_processRunner.Invocations);
    }

    // --- GetNameStatusAsync ---

    [Fact]
    public async Task GetNameStatusAsync_Success_ReturnsOutput()
    {
        _processRunner.NextResult = new ProcessResult(true, "M\tsrc/a.cs\nA\tsrc/b.cs\n", "", 0);
        var result = await _sut.GetNameStatusAsync("/repo", "main");
        Assert.Contains("M\tsrc/a.cs", result);
    }

    [Fact]
    public async Task GetNameStatusAsync_Failure_ReturnsEmpty()
    {
        _processRunner.NextResult = new ProcessResult(false, "", "error", 1);
        var result = await _sut.GetNameStatusAsync("/repo", "main");
        Assert.Equal("", result);
    }

    // --- ParseDiff (static) ---

    [Fact]
    public void ParseDiff_BasicScenario()
    {
        var nameStatus = "M\tsrc/a.cs\nA\tsrc/b.cs";
        var rawDiff = """
            diff --git a/src/a.cs b/src/a.cs
            --- a/src/a.cs
            +++ b/src/a.cs
            @@ -1,3 +1,4 @@
             line1
            +added line
             line2
            diff --git a/src/b.cs b/src/b.cs
            --- /dev/null
            +++ b/src/b.cs
            @@ -0,0 +1,2 @@
            +new file line 1
            +new file line 2
            """;

        var summary = GitService.ParseDiff(nameStatus, rawDiff);

        Assert.Equal(2, summary.Files.Count);
        Assert.Equal("src/a.cs", summary.Files[0].FilePath);
        Assert.Equal(FileChangeType.Modified, summary.Files[0].ChangeType);
        Assert.Equal(1, summary.Files[0].Additions);
        Assert.Equal(0, summary.Files[0].Deletions);

        Assert.Equal("src/b.cs", summary.Files[1].FilePath);
        Assert.Equal(FileChangeType.Added, summary.Files[1].ChangeType);
        Assert.Equal(2, summary.Files[1].Additions);
    }

    [Fact]
    public void ParseDiff_EmptyNameStatus_ReturnsEmptySummary()
    {
        var summary = GitService.ParseDiff("", "diff content");
        Assert.Empty(summary.Files);
    }

    [Fact]
    public void ParseDiff_EmptyDiff_HasFilesWithoutContent()
    {
        var nameStatus = "M\tsrc/a.cs";
        var summary = GitService.ParseDiff(nameStatus, "");

        Assert.Single(summary.Files);
        Assert.Equal("src/a.cs", summary.Files[0].FilePath);
        Assert.Equal(0, summary.Files[0].Additions);
        Assert.Equal(0, summary.Files[0].Deletions);
    }

    [Fact]
    public void ParseDiff_DeletedFile_SetsCorrectChangeType()
    {
        var nameStatus = "D\told/file.cs";
        var rawDiff = """
            diff --git a/old/file.cs b/old/file.cs
            --- a/old/file.cs
            +++ /dev/null
            @@ -1,3 +0,0 @@
            -line1
            -line2
            -line3
            """;

        var summary = GitService.ParseDiff(nameStatus, rawDiff);

        Assert.Single(summary.Files);
        Assert.Equal(FileChangeType.Deleted, summary.Files[0].ChangeType);
        Assert.Equal(0, summary.Files[0].Additions);
        Assert.Equal(3, summary.Files[0].Deletions);
    }

    [Fact]
    public void ParseDiff_RenamedFile_ParsesNewPath()
    {
        var nameStatus = "R100\told/name.cs\tnew/name.cs";
        var rawDiff = """
            diff --git a/old/name.cs b/new/name.cs
            similarity index 100%
            rename from old/name.cs
            rename to new/name.cs
            """;

        var summary = GitService.ParseDiff(nameStatus, rawDiff);

        Assert.Single(summary.Files);
        Assert.Equal("new/name.cs", summary.Files[0].FilePath);
        Assert.Equal(FileChangeType.Renamed, summary.Files[0].ChangeType);
    }

    [Fact]
    public void ParseDiff_TotalAdditionsAndDeletions()
    {
        var nameStatus = "M\ta.cs\nM\tb.cs";
        var rawDiff = """
            diff --git a/a.cs b/a.cs
            --- a/a.cs
            +++ b/a.cs
            +line1
            +line2
            -old1
            diff --git a/b.cs b/b.cs
            --- a/b.cs
            +++ b/b.cs
            +new
            """;

        var summary = GitService.ParseDiff(nameStatus, rawDiff);

        Assert.Equal(3, summary.TotalAdditions);
        Assert.Equal(1, summary.TotalDeletions);
    }

    // --- Shared stub ---

    private class StubProcessRunner : IProcessRunner
    {
        public List<ProcessRunOptions> Invocations { get; } = [];
        public ProcessResult NextResult { get; set; } = new(true, "", "", 0);
        public Queue<ProcessResult> ResultQueue { get; } = new();

        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default)
        {
            Invocations.Add(options);
            var result = ResultQueue.Count > 0 ? ResultQueue.Dequeue() : NextResult;
            return Task.FromResult(result);
        }

        public Task<StreamingProcess> RunStreamingAsync(ProcessRunOptions options, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }

    private class FakeOptionsMonitor : IOptionsMonitor<AppSettings>
    {
        public AppSettings CurrentValue { get; } = new();
        public AppSettings Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AppSettings, string?> listener) => null;
    }

    private class FakeShellService : IShellService
    {
        public Task<ShellInfo> GetShellAsync()
            => Task.FromResult(new ShellInfo("/bin/sh", "-c", ShellType.Sh));
        public Task<string?> WhichAsync(string executableName)
            => Task.FromResult<string?>(null);
        public void InvalidateCache() { }
    }
}
