using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Seoro.Shared.Tests;

public class PlanModeTests
{
    [Fact]
    public async Task FinalizeAsync_CodexPlanMode_UsesAssistantTextWithoutExitPlanMode()
    {
        var chatState = new ChatState(new ActiveSessionRegistry(), new FakeChatEventBus());
        var sut = new StreamEventProcessor([], chatState, NullLogger<StreamEventProcessor>.Instance);
        var session = new Session
        {
            Provider = "codex",
            PermissionMode = "plan",
            Git = new GitContext { WorktreePath = "/workspace" }
        };
        var ctx = new StreamProcessingContext
        {
            Session = session,
            AssistantMessage = new ChatMessage
            {
                Role = MessageRole.Assistant,
                Text = "## Context\n\nPlan body"
            },
            StreamStartTime = DateTime.UtcNow
        };

        await sut.FinalizeAsync(ctx);

        Assert.True(session.PlanCompleted);
        Assert.True(ctx.PlanReviewVisible);
        Assert.Equal("## Context\n\nPlan body", session.PlanContent);
        Assert.Equal("## Context\n\nPlan body", ctx.PlanContent);
        Assert.Null(session.PlanFilePath);
    }

    [Fact]
    public async Task BuildAsync_CodexPlanPrompt_DoesNotMentionClaudePlanFileFlow()
    {
        var sut = new SystemPromptBuilder(
            new FakeContextService(),
            new FakeMemoryService(),
            new FakeSettingsService(),
            new FakeCliProviderFactory(new FakeCliProvider("codex")),
            NullLogger<SystemPromptBuilder>.Instance);

        var prompt = await sut.BuildAsync(new Session
        {
            Provider = "codex",
            PermissionMode = "plan",
            Git = new GitContext { IsLocalDir = true }
        }, null);

        Assert.NotNull(prompt);
        Assert.Contains("return a detailed implementation plan in your final response", prompt);
        Assert.DoesNotContain(".claude/plans/", prompt);
        Assert.DoesNotContain("ExitPlanMode", prompt);
    }

    [Fact]
    public void SessionJsonConverter_RoundTripsPlanContent()
    {
        var session = new Session
        {
            Id = "plan-session",
            Provider = "codex",
            PermissionMode = "plan",
            PlanCompleted = true,
            PlanContent = "Saved plan content",
            PlanFilePath = ".context/plans/plan.md"
        };

        var json = JsonSerializer.Serialize(session, JsonDefaults.Options);
        var roundTripped = JsonSerializer.Deserialize<Session>(json, JsonDefaults.Options);

        Assert.NotNull(roundTripped);
        Assert.Equal("Saved plan content", roundTripped!.PlanContent);
        Assert.Equal(".context/plans/plan.md", roundTripped.PlanFilePath);
    }

    private sealed class FakeCliProvider(string id) : ICliProvider
    {
        public string ProviderId => id;
        public string DisplayName => id;
        public ProviderCapabilities Capabilities { get; } = new() { SupportsPlanMode = true };
        public void Dispose() { }
        public void Cancel(string? sessionId = null) { }
        public Task<(bool found, string resolvedPath)> DetectCliAsync() => Task.FromResult((true, id));
        public Task<string?> GetDetectedVersionAsync() => Task.FromResult<string?>("test");
        public async IAsyncEnumerable<StreamEvent> SendMessageAsync(CliSendOptions options, CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeCliProviderFactory(ICliProvider provider) : ICliProviderFactory
    {
        public ICliProvider GetProvider(string providerId) => provider;
        public IReadOnlyList<ICliProvider> GetAllProviders() => [provider];
        public ICliProvider GetProviderForSession(Session session) => provider;
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public event Action<AppSettings>? OnSettingsChanged;
        public Task SaveAsync(AppSettings settings)
        {
            OnSettingsChanged?.Invoke(settings);
            return Task.CompletedTask;
        }

        public Task<AppSettings> LoadAsync() => Task.FromResult(new AppSettings());
    }

    private sealed class FakeMemoryService : IMemoryService
    {
        public string BuildMemoryPrompt(IEnumerable<MemoryEntry> entries) => "";
        public Task DeleteAsync(string entryId) => Task.CompletedTask;
        public Task SaveAsync(MemoryEntry entry) => Task.CompletedTask;
        public Task<List<MemoryEntry>> GetAllAsync() => Task.FromResult<List<MemoryEntry>>([]);
        public Task<List<MemoryEntry>> GetByTypeAsync(MemoryType type) => Task.FromResult<List<MemoryEntry>>([]);
        public Task<List<MemoryEntry>> GetForWorkspaceAsync(string? workspaceId) => Task.FromResult<List<MemoryEntry>>([]);
        public Task<List<MemoryEntry>> SearchAsync(string query, string? workspaceId = null) => Task.FromResult<List<MemoryEntry>>([]);
        public Task<MemoryEntry?> FindAsync(string entryId) => Task.FromResult<MemoryEntry?>(null);
    }

    private sealed class FakeContextService : IContextService
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

    private sealed class FakeChatEventBus : IChatEventBus
    {
        public event Action? OnAny;
        public void Publish<T>(T evt) where T : ChatEvent => OnAny?.Invoke();
        public IDisposable Subscribe<T>(Action<T> handler) where T : ChatEvent => new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
