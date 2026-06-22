using Seoro.Shared.Models;
using Seoro.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Seoro.Shared.Tests;

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

    // --- InitAsync ---

    [Fact]
    public async Task InitAsync_Success_ReturnsSuccess()
    {
        _processRunner.NextResult = new ProcessResult(true, "Initialized empty Git repository\n", "", 0);
        var result = await _sut.InitAsync("/repo");
        Assert.True(result.Success);
    }

    [Fact]
    public async Task InitAsync_Failure_ReturnsError()
    {
        _processRunner.NextResult = new ProcessResult(false, "", "fatal: cannot mkdir", 128);
        var result = await _sut.InitAsync("/repo");
        Assert.False(result.Success);
        Assert.Contains("cannot mkdir", result.Error);
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

    // --- GetWorkingTreeStatusAsync: 캐시 + 무효화 + 단일 비행 ---

    [Fact]
    public async Task GetWorkingTreeStatusAsync_SecondCallWithinTtl_DoesNotSpawnAgain()
    {
        _processRunner.NextResult = new ProcessResult(true, "", "", 0);

        await _sut.GetWorkingTreeStatusAsync("/repo");
        var countAfterFirst = _processRunner.Invocations.Count;

        await _sut.GetWorkingTreeStatusAsync("/repo");

        Assert.Equal(countAfterFirst, _processRunner.Invocations.Count);
    }

    [Fact]
    public async Task GetWorkingTreeStatusAsync_CachedCall_ReturnsSameInstance()
    {
        _processRunner.NextResult = new ProcessResult(true, "", "", 0);

        var first = await _sut.GetWorkingTreeStatusAsync("/repo");
        var second = await _sut.GetWorkingTreeStatusAsync("/repo");

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetWorkingTreeStatusAsync_AfterStageFile_CacheInvalidated()
    {
        _processRunner.NextResult = new ProcessResult(true, "", "", 0);

        await _sut.GetWorkingTreeStatusAsync("/repo");
        var countAfterFirst = _processRunner.Invocations.Count;

        await _sut.StageFileAsync("/repo", "src/a.cs");

        await _sut.GetWorkingTreeStatusAsync("/repo");

        // stage(1회) + status 재계산(3회) — 캐시가 무효화되어 다시 spawn 되어야 한다
        Assert.Equal(countAfterFirst + 4, _processRunner.Invocations.Count);
    }

    [Fact]
    public async Task GetWorkingTreeStatusAsync_AfterInvalidateStatusCacheAsync_Recomputes()
    {
        _processRunner.NextResult = new ProcessResult(true, "", "", 0);

        await _sut.GetWorkingTreeStatusAsync("/repo");
        var countAfterFirst = _processRunner.Invocations.Count;

        await _sut.InvalidateStatusCacheAsync("/repo");
        await _sut.GetWorkingTreeStatusAsync("/repo");

        Assert.Equal(countAfterFirst * 2, _processRunner.Invocations.Count);
    }

    [Fact]
    public async Task GetWorkingTreeStatusAsync_ConcurrentCalls_SingleFlight()
    {
        var gated = new GatedProcessRunner();
        var sut = new GitService(
            NullLogger<GitService>.Instance,
            gated,
            new FakeOptionsMonitor(),
            new FakeShellService());

        var first = sut.GetWorkingTreeStatusAsync("/repo");
        var second = sut.GetWorkingTreeStatusAsync("/repo");

        // 두 호출이 모두 시작될 시간을 준 뒤 게이트 해제
        await Task.Delay(50);
        gated.Release();

        var r1 = await first;
        var r2 = await second;

        Assert.Same(r1, r2);
        // 파이프라인이 한 번만 실행됨 (staged numstat + unstaged numstat + porcelain = 3회)
        Assert.Equal(3, gated.Invocations.Count);
    }

    // --- HasUnresolvedConflictsAsync ---

    [Fact]
    public async Task HasUnresolvedConflictsAsync_NoMergeHead_NoProcessSpawn()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"git_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, ".git"));
        try
        {
            var result = await _sut.HasUnresolvedConflictsAsync(tempDir);

            Assert.False(result);
            // MERGE_HEAD 가 없으면 git 프로세스를 전혀 띄우지 않아야 한다
            Assert.Empty(_processRunner.Invocations);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task HasUnresolvedConflictsAsync_MergeHeadWithConflictMarker_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"git_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, ".git"));
        await File.WriteAllTextAsync(Path.Combine(tempDir, ".git", "MERGE_HEAD"), "abc123\n");
        try
        {
            _processRunner.NextResult = new ProcessResult(true, "UU src/conflict.cs\n", "", 0);

            var result = await _sut.HasUnresolvedConflictsAsync(tempDir);

            Assert.True(result);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
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

    /// <summary>Release() 전까지 모든 RunAsync 를 블로킹하는 러너 — 단일 비행(coalescing) 검증용.</summary>
    private class GatedProcessRunner : IProcessRunner
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<ProcessRunOptions> Invocations { get; } = [];

        public void Release() => _gate.TrySetResult();

        public async Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default)
        {
            lock (Invocations)
            {
                Invocations.Add(options);
            }

            await _gate.Task;
            return new ProcessResult(true, "", "", 0);
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
        public Task<ShellInfo> GetTerminalShellAsync()
            => GetShellAsync();
        public Task<List<ShellInfo>> GetAvailableShellsAsync()
            => Task.FromResult(new List<ShellInfo> { new("/bin/sh", "-c", ShellType.Sh) });
        public Task<string?> WhichAsync(string executableName)
            => Task.FromResult<string?>(null);
        public Task<string?> GetLoginShellPathAsync()
            => Task.FromResult<string?>(null);
        public void InvalidateCache() { }
    }
}
