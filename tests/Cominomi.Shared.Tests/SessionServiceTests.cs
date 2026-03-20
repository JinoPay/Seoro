using System.Text.Json;
using Cominomi.Shared;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Tests;

public class SessionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeGitService _gitService = new();
    private readonly FakeWorkspaceService _workspaceService;
    private readonly FakeOptionsMonitor _optionsMonitor = new();
    private readonly FakeContextService _contextService = new();
    private readonly FakeHooksEngine _hooksEngine = new();
    private readonly SessionService _sut;

    public SessionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"session_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _workspaceService = new FakeWorkspaceService(_tempDir);

        _sut = new SessionService(
            _gitService,
            _workspaceService,
            _optionsMonitor,
            _contextService,
            _hooksEngine,
            new ActiveSessionRegistry(),
            NullLogger<SessionService>.Instance);

        // Override internal _sessionsDir via reflection
        var field = typeof(SessionService).GetField("_sessionsDir",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(_sut, _tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // --- GenerateBranchName (static) ---

    [Fact]
    public void GenerateBranchName_SimpleMessage_CreatesSlug()
    {
        var result = SessionService.GenerateBranchName("Fix the login bug");
        Assert.StartsWith(CominomiConstants.BranchPrefix, result);
        Assert.Contains("fix-the-login-bug", result);
    }

    [Fact]
    public void GenerateBranchName_SpecialChars_Stripped()
    {
        var result = SessionService.GenerateBranchName("Add feature! @#$ test");
        Assert.DoesNotContain("!", result);
        Assert.DoesNotContain("@", result);
        Assert.DoesNotContain("#", result);
        Assert.DoesNotContain("$", result);
    }

    [Fact]
    public void GenerateBranchName_LongMessage_Truncated()
    {
        var longMsg = new string('a', 100);
        var result = SessionService.GenerateBranchName(longMsg);
        // prefix + slug should be within limits
        var slug = result[CominomiConstants.BranchPrefix.Length..];
        Assert.True(slug.Length <= 40);
    }

    [Fact]
    public void GenerateBranchName_EmptyMessage_UsesHashFallback()
    {
        var result = SessionService.GenerateBranchName("   ");
        Assert.StartsWith(CominomiConstants.BranchPrefix, result);
        var slug = result[CominomiConstants.BranchPrefix.Length..];
        Assert.Matches(@"^[0-9a-f]+$", slug);
    }

    [Fact]
    public void GenerateBranchName_KoreanMessage_UsesHashFallback()
    {
        var result = SessionService.GenerateBranchName("로그인 버그 수정");
        Assert.StartsWith(CominomiConstants.BranchPrefix, result);
        var slug = result[CominomiConstants.BranchPrefix.Length..];
        Assert.Matches(@"^[0-9a-f]+$", slug);
        Assert.True(slug.Length > 0);
    }

    [Fact]
    public void GenerateBranchName_MixedKoreanEnglish_KeepsEnglish()
    {
        var result = SessionService.GenerateBranchName("fix 로그인 bug");
        Assert.StartsWith(CominomiConstants.BranchPrefix, result);
        Assert.Contains("fix", result);
        Assert.Contains("bug", result);
    }

    [Fact]
    public void GenerateBranchName_MultipleSpaces_CollapsedToSingleHyphen()
    {
        var result = SessionService.GenerateBranchName("a   b   c");
        Assert.Contains("a-b-c", result);
        Assert.DoesNotContain("--", result);
    }

    // --- CreatePendingSessionAsync ---

    [Fact]
    public async Task CreatePendingSessionAsync_CreatesSessionWithPendingStatus()
    {
        var session = await _sut.CreatePendingSessionAsync("sonnet", "ws-1");

        Assert.Equal(SessionStatus.Pending, session.Status);
        Assert.Equal("sonnet", session.Model);
        Assert.Equal("ws-1", session.WorkspaceId);
        Assert.NotEmpty(session.CityName);
    }

    [Fact]
    public async Task CreatePendingSessionAsync_FiresHook()
    {
        await _sut.CreatePendingSessionAsync("sonnet", "ws-1");

        Assert.Single(_hooksEngine.FiredEvents);
        Assert.Equal(HookEvent.OnSessionCreate, _hooksEngine.FiredEvents[0].Event);
    }

    [Fact]
    public async Task CreatePendingSessionAsync_NullModel_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreatePendingSessionAsync("", "ws-1"));
    }

    [Fact]
    public async Task CreatePendingSessionAsync_WorkspaceNotFound_Throws()
    {
        _workspaceService.WorkspaceToReturn = null;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreatePendingSessionAsync("sonnet", "ws-missing"));
    }

    // --- CreateLocalDirSessionAsync ---

    [Fact]
    public async Task CreateLocalDirSessionAsync_CreatesReadySessionWithLocalDir()
    {
        var session = await _sut.CreateLocalDirSessionAsync("sonnet", "ws-1");

        Assert.Equal(SessionStatus.Ready, session.Status);
        Assert.True(session.Git.IsLocalDir);
        Assert.Equal("/fake/repo", session.Git.WorktreePath);
    }

    // --- SaveSessionAsync + LoadSessionAsync roundtrip ---

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var session = new Session
        {
            Id = "test-roundtrip",
            Title = "Test Session",
            Model = "sonnet",
            WorkspaceId = "ws-1",
            CityName = "Seoul"
        };
        session.SetInitialStatus(SessionStatus.Ready);
        session.Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Text = "Hello!"
        });

        await _sut.SaveSessionAsync(session);

        // Verify files exist
        Assert.True(File.Exists(Path.Combine(_tempDir, "test-roundtrip.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "test-roundtrip.messages.json")));

        var loaded = await _sut.LoadSessionAsync("test-roundtrip");

        Assert.NotNull(loaded);
        Assert.Equal("test-roundtrip", loaded.Id);
        Assert.Equal("Test Session", loaded.Title);
        Assert.Single(loaded.Messages);
        Assert.Equal("Hello!", loaded.Messages[0].Text);
    }

    [Fact]
    public async Task LoadSessionAsync_NonExistent_ReturnsNull()
    {
        var result = await _sut.LoadSessionAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveSessionAsync_FallbackTitle_UsesFirstUserMessage()
    {
        var session = new Session
        {
            Id = "title-test",
            CityName = "Seoul",
            Title = "Seoul" // same as CityName → triggers fallback
        };
        session.SetInitialStatus(SessionStatus.Ready);
        session.Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Text = "Fix the authentication bug in the login page"
        });

        await _sut.SaveSessionAsync(session);

        Assert.Equal("Fix the authentication bug in the login page", session.Title);
    }

    [Fact]
    public async Task SaveSessionAsync_LongFirstMessage_TruncatesTitle()
    {
        var session = new Session
        {
            Id = "title-trunc",
            CityName = "Seoul",
            Title = "Seoul"
        };
        session.SetInitialStatus(SessionStatus.Ready);
        session.Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Text = new string('x', 100)
        });

        await _sut.SaveSessionAsync(session);

        Assert.Equal(53, session.Title.Length); // 50 chars + "..."
        Assert.EndsWith("...", session.Title);
    }

    // --- Tool output truncation ---

    [Fact]
    public async Task SaveSessionAsync_TruncatesLongToolOutput()
    {
        var session = new Session { Id = "trunc-test", CityName = "A", Title = "A" };
        session.SetInitialStatus(SessionStatus.Ready);
        var longOutput = new string('z', 3000);
        session.Messages.Add(new ChatMessage
        {
            Role = MessageRole.Assistant,
            ToolCalls = [new ToolCall { Id = "tc-1", Name = "Read", Output = longOutput }]
        });

        await _sut.SaveSessionAsync(session);

        // Read messages file directly
        var messagesJson = await File.ReadAllTextAsync(Path.Combine(_tempDir, "trunc-test.messages.json"));
        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(messagesJson, JsonDefaults.Options)!;
        var savedOutput = messages[0].ToolCalls[0].Output;

        Assert.True(savedOutput.Length < longOutput.Length);
        Assert.Contains("[...truncated", savedOutput);
    }

    // --- DeleteSessionAsync ---

    [Fact]
    public async Task DeleteSessionAsync_RemovesFiles()
    {
        // Create a session first
        var session = new Session { Id = "del-test" };
        session.SetInitialStatus(SessionStatus.Ready);
        session.Messages.Add(new ChatMessage { Role = MessageRole.User, Text = "test" });
        await _sut.SaveSessionAsync(session);

        await _sut.DeleteSessionAsync("del-test");

        Assert.False(File.Exists(Path.Combine(_tempDir, "del-test.json")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "del-test.messages.json")));
    }

    // --- GetSessionsAsync / GetSessionsByWorkspaceAsync ---

    [Fact]
    public async Task GetSessionsByWorkspaceAsync_FiltersCorrectly()
    {
        var s1 = new Session { Id = "s1", WorkspaceId = "ws-1" };
        s1.SetInitialStatus(SessionStatus.Ready);
        var s2 = new Session { Id = "s2", WorkspaceId = "ws-2" };
        s2.SetInitialStatus(SessionStatus.Ready);
        var s3 = new Session { Id = "s3", WorkspaceId = "ws-1" };
        s3.SetInitialStatus(SessionStatus.Ready);

        await _sut.SaveSessionAsync(s1);
        await _sut.SaveSessionAsync(s2);
        await _sut.SaveSessionAsync(s3);

        var ws1Sessions = await _sut.GetSessionsByWorkspaceAsync("ws-1");
        Assert.Equal(2, ws1Sessions.Count);
        Assert.All(ws1Sessions, s => Assert.Equal("ws-1", s.WorkspaceId));
    }

    // --- Stubs ---

    private class FakeGitService : IGitService
    {
        public GitResult NextResult { get; set; } = new(true, "", "");

        public Task<GitResult> AddWorktreeAsync(string repoDir, string worktreePath, string branchName, string baseBranch, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<GitResult> RemoveWorktreeAsync(string repoDir, string worktreePath, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<GitResult> CloneAsync(string url, string targetDir, IProgress<string>? progress = null, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<string?> DetectDefaultBranchAsync(string repoDir)
            => Task.FromResult<string?>("origin/main");
        public Task<bool> IsGitRepoAsync(string path) => Task.FromResult(true);
        public Task<string?> GetCurrentBranchAsync(string repoDir) => Task.FromResult<string?>("main");
        public Task<List<string>> ListBranchesAsync(string repoDir) => Task.FromResult<List<string>>(["main"]);
        public Task<bool> BranchExistsAsync(string repoDir, string branchName) => Task.FromResult(false);
        public Task<GitResult> RenameBranchAsync(string workingDir, string oldName, string newName, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<GitResult> DeleteBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<bool> IsBranchMergedAsync(string repoDir, string branchName, string baseBranch, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<GitResult> PushBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<GitResult> PushForceBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<GitResult> FetchAsync(string repoDir, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<GitResult> FetchAllAsync(string repoDir, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<GitResult> RebaseAsync(string workingDir, string baseBranch, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<List<BranchGroup>> ListAllBranchesGroupedAsync(string repoDir)
            => Task.FromResult<List<BranchGroup>>([]);
        public Task<string> GetNameStatusAsync(string workingDir, string baseBranch, CancellationToken ct = default)
            => Task.FromResult("");
        public Task<string> GetUnifiedDiffAsync(string workingDir, string baseBranch, CancellationToken ct = default)
            => Task.FromResult("");
        public Task<List<string>> ListTrackedFilesAsync(string workingDir, CancellationToken ct = default)
            => Task.FromResult<List<string>>([]);
        public Task<GitResult> GetCommitLogAsync(string repoDir, string baseBranch, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<GitResult> GetFormattedCommitLogAsync(string repoDir, string baseBranch, int maxCount = 50, CancellationToken ct = default)
            => Task.FromResult(NextResult);
        public Task<string> ReadFileAsync(string workingDir, string relativePath, CancellationToken ct = default)
            => Task.FromResult("");
        public Task<(int Additions, int Deletions)> GetDiffStatAsync(string workingDir, string baseBranch, CancellationToken ct = default)
            => Task.FromResult((0, 0));
        public Task<DiffSummary> GetDiffSummaryAsync(string workingDir, string baseBranch, CancellationToken ct = default)
            => Task.FromResult(new DiffSummary());
        public Task<GitResult> RunAsync(string arguments, string workingDir, CancellationToken ct = default)
            => Task.FromResult(NextResult);
    }

    private class FakeWorkspaceService : IWorkspaceService
    {
        public Workspace? WorkspaceToReturn;

        public FakeWorkspaceService(string tempDir)
        {
            WorkspaceToReturn = new Workspace
            {
                Id = "ws-1",
                Name = "Test",
                RepoLocalPath = "/fake/repo"
            };
        }

        public Task<List<Workspace>> GetWorkspacesAsync() => Task.FromResult<List<Workspace>>([]);
        public Task<Workspace?> LoadWorkspaceAsync(string workspaceId) => Task.FromResult(WorkspaceToReturn);
        public Task SaveWorkspaceAsync(Workspace workspace) => Task.CompletedTask;
        public Task DeleteWorkspaceAsync(string workspaceId) => Task.CompletedTask;
        public Task<Workspace> CreateFromUrlAsync(string url, string name, string model, IProgress<string>? progress = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Workspace> CreateFromLocalAsync(string localPath, string name, string model, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<GitRepoInfo?> FindExistingRepoAsync(string remoteUrl)
            => Task.FromResult<GitRepoInfo?>(null);
        public Task<string> GetWorktreesDirAsync() => Task.FromResult("/tmp/worktrees");
    }

    private class FakeOptionsMonitor : IOptionsMonitor<AppSettings>
    {
        public AppSettings CurrentValue { get; } = new();
        public AppSettings Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AppSettings, string?> listener) => null;
    }

    private class FakeContextService : IContextService
    {
        public Task<ContextInfo> LoadContextAsync(string worktreePath) => Task.FromResult(new ContextInfo());
        public Task SaveNotesAsync(string worktreePath, string content) => Task.CompletedTask;
        public Task SaveTodosAsync(string worktreePath, string content) => Task.CompletedTask;
        public Task SavePlanAsync(string worktreePath, string planName, string content) => Task.CompletedTask;
        public Task DeletePlanAsync(string worktreePath, string planName) => Task.CompletedTask;
        public Task<List<PlanFile>> GetPlansAsync(string worktreePath) => Task.FromResult<List<PlanFile>>([]);
        public Task EnsureContextDirectoryAsync(string worktreePath) => Task.CompletedTask;
        public Task ArchiveContextAsync(string worktreePath, string archivePath) => Task.CompletedTask;
        public string BuildContextPrompt(ContextInfo context) => "";
    }

    private class FakeHooksEngine : IHooksEngine
    {
        public List<(HookEvent Event, Dictionary<string, string>? Env)> FiredEvents { get; } = [];

        public Task<List<HookExecutionResult>> FireAsync(HookEvent hookEvent, Dictionary<string, string>? env = null)
        {
            FiredEvents.Add((hookEvent, env));
            return Task.FromResult<List<HookExecutionResult>>([]);
        }
        public List<HookDefinition> GetHooks() => [];
        public Task AddHookAsync(HookDefinition hook) => Task.CompletedTask;
        public Task RemoveHookAsync(HookEvent hookEvent, string command) => Task.CompletedTask;
        public Task LoadAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
    }
}
