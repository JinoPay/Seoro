using Microsoft.Extensions.Logging.Abstractions;
using Seoro.Shared.Tests.Fakes;

namespace Seoro.Shared.Tests;

public class MergeStatusServiceTests
{
    private readonly EventBus _eventBus = new();
    private readonly ConfigurableGitService _git = new();
    private readonly FakeSessionService _sessions = new();
    private readonly FakeWorkspaceService _workspaces = new();
    private readonly FakeConflictWatcher _conflict = new();

    private MergeStatusService CreateSut() => new(
        _eventBus, _git, _sessions, _workspaces, _conflict,
        NullLogger<MergeStatusService>.Instance);

    private const string SessionId = "sess-1";

    private void SetupWorktreeSession(bool isLocalDir = false, string branch = "feature", string worktree = "/wt")
    {
        _sessions.Sessions[SessionId] = new Session
        {
            Id = SessionId,
            WorkspaceId = "ws-1",
            Git = new GitContext { IsLocalDir = isLocalDir, BranchName = branch, WorktreePath = worktree }
        };
        _workspaces.Workspace = new Workspace { Id = "ws-1", Name = "W", RepoLocalPath = "/repo" };
    }

    [Fact]
    public void GetCurrent_UnknownSession_ReturnsUnknown()
    {
        using var sut = CreateSut();
        Assert.Equal(MergeStatusKind.Unknown, sut.GetCurrent("nope").Kind);
    }

    [Fact]
    public async Task Refresh_SessionMissing_ResultsInUnknown()
    {
        using var sut = CreateSut();
        _sessions.Sessions.Clear();

        await sut.RefreshAsync(SessionId, "main");

        Assert.Equal(MergeStatusKind.Unknown, sut.GetCurrent(SessionId).Kind);
    }

    [Fact]
    public async Task Refresh_InConflict_TakesPriority()
    {
        using var sut = CreateSut();
        SetupWorktreeSession();
        _conflict.InConflict = true;
        _git.GetStatusPorcelainHook = (_, _) => Task.FromResult(new List<string> { "UU src/a.cs", "M  src/b.cs" });

        await sut.RefreshAsync(SessionId, "main");

        var status = sut.GetCurrent(SessionId);
        Assert.Equal(MergeStatusKind.InConflict, status.Kind);
        Assert.NotNull(status.ConflictingFiles);
        Assert.Equal(["src/a.cs"], status.ConflictingFiles);
    }

    [Fact]
    public async Task Refresh_NetworkFailure_ResultsInNetworkError()
    {
        using var sut = CreateSut();
        SetupWorktreeSession();
        _git.FetchAndCompareHook = (_, _, _, _) => Task.FromResult<(int, int)?>(null);

        await sut.RefreshAsync(SessionId, "main");

        Assert.Equal(MergeStatusKind.NetworkError, sut.GetCurrent(SessionId).Kind);
    }

    [Fact]
    public async Task Refresh_MergeWouldConflict_ResultsInConflictExpected()
    {
        using var sut = CreateSut();
        SetupWorktreeSession();
        _git.FetchAndCompareHook = (_, _, _, _) => Task.FromResult<(int, int)?>((2, 1));
        _git.SimulateMergeHook = (_, _, _, _) =>
            Task.FromResult(new MergeSimulationResult(true, true, ["src/x.cs"], 2, 1, null));

        await sut.RefreshAsync(SessionId, "main");

        var status = sut.GetCurrent(SessionId);
        Assert.Equal(MergeStatusKind.ConflictExpected, status.Kind);
        Assert.Equal(["src/x.cs"], status.ConflictingFiles);
    }

    [Fact]
    public async Task Refresh_BehindTarget_WhenNoConflictButBehind()
    {
        using var sut = CreateSut();
        SetupWorktreeSession();
        _git.FetchAndCompareHook = (_, _, _, _) => Task.FromResult<(int, int)?>((0, 3));
        _git.SimulateMergeHook = (_, _, _, _) =>
            Task.FromResult(new MergeSimulationResult(true, false, [], 0, 3, null));

        await sut.RefreshAsync(SessionId, "main");

        var status = sut.GetCurrent(SessionId);
        Assert.Equal(MergeStatusKind.BehindTarget, status.Kind);
        Assert.Equal(3, status.BehindCount);
    }

    [Fact]
    public async Task Refresh_Clean_WhenUpToDateAndNoUncommitted()
    {
        using var sut = CreateSut();
        SetupWorktreeSession();
        _git.FetchAndCompareHook = (_, _, _, _) => Task.FromResult<(int, int)?>((1, 0));
        _git.SimulateMergeHook = (_, _, _, _) =>
            Task.FromResult(new MergeSimulationResult(true, false, [], 1, 0, null));

        await sut.RefreshAsync(SessionId, "main");

        Assert.Equal(MergeStatusKind.Clean, sut.GetCurrent(SessionId).Kind);
    }

    [Fact]
    public async Task Refresh_UncommittedDirty_WhenWorktreeHasChanges()
    {
        using var sut = CreateSut();
        SetupWorktreeSession();
        _git.GetUncommittedChangesHook = (_, _) => Task.FromResult(new List<string> { "M src/a.cs" });
        _git.FetchAndCompareHook = (_, _, _, _) => Task.FromResult<(int, int)?>((1, 0));
        _git.SimulateMergeHook = (_, _, _, _) =>
            Task.FromResult(new MergeSimulationResult(true, false, [], 1, 0, null));

        await sut.RefreshAsync(SessionId, "main");

        var status = sut.GetCurrent(SessionId);
        Assert.Equal(MergeStatusKind.UncommittedDirty, status.Kind);
        Assert.Equal(1, status.UncommittedChangeCount);
    }

    [Fact]
    public async Task Refresh_NoTargetBranch_ReturnsUncommittedOrUnknown()
    {
        using var sut = CreateSut();
        SetupWorktreeSession();
        _git.GetUncommittedChangesHook = (_, _) => Task.FromResult(new List<string> { "M a" });

        // No target override and none set → ComputeStatus short-circuits on missing target.
        await sut.RefreshAsync(SessionId);

        Assert.Equal(MergeStatusKind.UncommittedDirty, sut.GetCurrent(SessionId).Kind);
    }

    [Fact]
    public async Task Refresh_LocalDir_NetworkFailure_ResultsInNetworkError()
    {
        using var sut = CreateSut();
        SetupWorktreeSession(isLocalDir: true, branch: "main");
        _git.FetchAndCompareHook = (_, _, _, _) => Task.FromResult<(int, int)?>(null);

        await sut.RefreshAsync(SessionId, "main");

        Assert.Equal(MergeStatusKind.NetworkError, sut.GetCurrent(SessionId).Kind);
    }

    [Fact]
    public void SetAndGetTargetBranch_RoundTrips()
    {
        using var sut = CreateSut();
        sut.SetTargetBranch(SessionId, "develop");
        Assert.Equal("develop", sut.GetTargetBranch(SessionId));
    }

    [Fact]
    public async Task Refresh_FiresStatusChangedEvent()
    {
        using var sut = CreateSut();
        SetupWorktreeSession();
        string? changed = null;
        sut.StatusChanged += id => changed = id;

        await sut.RefreshAsync(SessionId, "main");

        Assert.Equal(SessionId, changed);
    }

    // --- Fakes ---

    private sealed class FakeConflictWatcher : IConflictWatcherService
    {
        public bool InConflict { get; set; }
        public ValueTask<bool> IsInConflictAsync(string workingDir, CancellationToken ct = default)
            => ValueTask.FromResult(InConflict);
        public void Watch(Session session) { }
        public void Unwatch() { }
        public void WatchExtraPath(string workingDir) { }
        public void UnwatchExtraPath(string workingDir) { }
        public void Dispose() { }
    }

    private sealed class FakeSessionService : ISessionService
    {
        public Dictionary<string, Session> Sessions { get; } = new();

        public Task<Session?> LoadSessionAsync(string sessionId)
            => Task.FromResult(Sessions.TryGetValue(sessionId, out var s) ? s : null);

        public void InvalidateSessionCache(string sessionId) { }
        public Task CleanupSessionAsync(string sessionId) => Task.CompletedTask;
        public Task DeleteSessionAsync(string sessionId) => Task.CompletedTask;
        public Task RenameBranchAsync(string sessionId, string newBranchName) => Task.CompletedTask;
        public Task SaveSessionAsync(Session session) => Task.CompletedTask;
        public Task<List<Session>> GetSessionsAsync() => Task.FromResult<List<Session>>([]);
        public Task<List<Session>> GetSessionsByWorkspaceAsync(string workspaceId) => Task.FromResult<List<Session>>([]);
        public Task<Session> CreateLocalDirSessionAsync(string model, string workspaceId, string provider = "claude") => throw new NotImplementedException();
        public Task<Session> CreatePendingSessionAsync(string model, string workspaceId, string provider = "claude") => throw new NotImplementedException();
        public Task<Session> CreateSessionAsync(string model, string workspaceId, string baseBranch, string provider = "claude") => throw new NotImplementedException();
        public Task<Session> InitializeWorktreeAsync(string sessionId, string baseBranch) => throw new NotImplementedException();
        public Task<Session> RebaseWorktreeAsync(string sessionId, string newBaseBranch) => throw new NotImplementedException();
    }

    private sealed class FakeWorkspaceService : IWorkspaceService
    {
        public event Action<Workspace>? OnWorkspaceSaved;
        public Workspace? Workspace { get; set; }

        public Task<Workspace?> LoadWorkspaceAsync(string workspaceId) => Task.FromResult(Workspace);
        public Task<List<Workspace>> GetWorkspacesAsync() => Task.FromResult<List<Workspace>>([]);
        public Task SaveWorkspaceAsync(Workspace workspace) { OnWorkspaceSaved?.Invoke(workspace); return Task.CompletedTask; }
        public Task DeleteWorkspaceAsync(string workspaceId) => Task.CompletedTask;
        public Task<Workspace> CreateFromUrlAsync(string url, string name, string model, string? targetDir = null, IProgress<string>? progress = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetDefaultClonePathAsync(string url) => throw new NotImplementedException();
        public Task<Workspace> CreateFromLocalAsync(string localPath, string name, string model, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<GitRepoInfo?> FindExistingRepoAsync(string remoteUrl) => Task.FromResult<GitRepoInfo?>(null);
        public Task<string> GetWorktreesDirAsync() => Task.FromResult("/tmp/worktrees");
        public RemoteInfo GetRemoteInfo(string workspaceId) => RemoteInfo.None;
        public Task<RemoteInfo> RefreshRemoteInfoAsync(string workspaceId, CancellationToken ct = default) => Task.FromResult(RemoteInfo.None);
    }
}
