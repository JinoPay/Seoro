using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Seoro.Shared.Tests;

public class PullRequestServiceTests
{
    [Fact]
    public async Task TryCaptureCreatedPrAsync_StoresTrackedPrFromAssistantText()
    {
        var runner = new FakeProcessRunner((fileName, arguments) =>
        {
            Assert.Equal("gh", fileName);
            Assert.Equal("pr", arguments[0]);
            Assert.Equal("view", arguments[1]);

            return new ProcessResult(true,
                """
                {
                  "number": 42,
                  "url": "https://github.com/acme/seoro/pull/42",
                  "state": "OPEN",
                  "title": "feat: PR tracking",
                  "isDraft": false,
                  "mergeable": "MERGEABLE",
                  "mergeStateStatus": "CLEAN",
                  "reviewDecision": "APPROVED",
                  "headRefName": "feature/pr-track",
                  "baseRefName": "main",
                  "mergedAt": null,
                  "mergeCommit": null,
                  "statusCheckRollup": []
                }
                """, "", 0);
        });

        var service = CreateService(runner);
        var session = new Session
        {
            Git = new GitContext
            {
                WorktreePath = "/tmp/worktree",
                BranchName = "feature/pr-track"
            }
        };
        var message = new ChatMessage
        {
            Text = "생성 완료: https://github.com/acme/seoro/pull/42"
        };

        var tracked = await service.TryCaptureCreatedPrAsync(session, message);

        Assert.NotNull(tracked);
        Assert.Equal(42, tracked!.Number);
        Assert.Equal("main", tracked.BaseBranch);
        Assert.Equal("feat: PR tracking", tracked.Title);
        Assert.True(tracked.IsMergeable);
        Assert.Equal("https://github.com/acme/seoro/pull/42", session.Git.LastPrUrl);
    }

    [Fact]
    public async Task MergeAsync_UsesRequestedStrategyAndRefreshesPr()
    {
        var callCount = 0;
        var runner = new FakeProcessRunner((_, arguments) =>
        {
            callCount++;
            if (callCount == 1)
            {
                Assert.Contains("--squash", arguments);
                return new ProcessResult(true, "", "", 0);
            }

            return new ProcessResult(true,
                """
                {
                  "number": 7,
                  "url": "https://github.com/acme/seoro/pull/7",
                  "state": "MERGED",
                  "title": "feat: merge test",
                  "isDraft": false,
                  "mergeable": "UNKNOWN",
                  "mergeStateStatus": "UNKNOWN",
                  "reviewDecision": "APPROVED",
                  "headRefName": "feature/pr-track",
                  "baseRefName": "main",
                  "mergedAt": "2026-04-13T00:00:00Z",
                  "mergeCommit": { "oid": "abc123" },
                  "statusCheckRollup": []
                }
                """, "", 0);
        });

        var service = CreateService(runner);
        var session = new Session
        {
            Git = new GitContext
            {
                WorktreePath = "/tmp/worktree",
                BranchName = "feature/pr-track",
                TrackedPr = new TrackedPullRequest
                {
                    Url = "https://github.com/acme/seoro/pull/7",
                    Number = 7,
                    State = PullRequestLifecycleState.Open
                }
            }
        };

        var result = await service.MergeAsync(session, PullRequestMergeStrategy.Squash);

        Assert.True(result.Success);
        Assert.NotNull(result.PullRequest);
        Assert.True(result.PullRequest!.IsMerged);
        Assert.Equal("abc123", result.PullRequest.LastMergeCommitSha);
    }

    [Fact]
    public void SessionJsonConverter_RoundTripsTrackedPr()
    {
        var session = new Session
        {
            Id = "session-1",
            Git = new GitContext
            {
                WorktreePath = "/tmp/worktree",
                BranchName = "feature/pr-track",
                TrackedPr = new TrackedPullRequest
                {
                    Url = "https://github.com/acme/seoro/pull/5",
                    Number = 5,
                    Title = "feat: round trip",
                    BaseBranch = "main",
                    HeadBranch = "feature/pr-track",
                    State = PullRequestLifecycleState.Open
                }
            }
        };

        var json = JsonSerializer.Serialize(session, JsonDefaults.Options);
        var restored = JsonSerializer.Deserialize<Session>(json, JsonDefaults.Options);

        Assert.NotNull(restored);
        Assert.NotNull(restored!.Git.TrackedPr);
        Assert.Equal(5, restored.Git.TrackedPr!.Number);
        Assert.Equal("feat: round trip", restored.Git.TrackedPr.Title);
        Assert.Equal("https://github.com/acme/seoro/pull/5", restored.Git.LastPrUrl);
    }

    [Fact]
    public async Task GetPrForBranchAsync_SetsTrackedPrWhenFound()
    {
        var runner = new FakeProcessRunner((_, arguments) =>
        {
            Assert.Equal("feature/check-pr", arguments[2]);
            return new ProcessResult(true,
                """
                {
                  "number": 99,
                  "url": "https://github.com/acme/seoro/pull/99",
                  "state": "OPEN",
                  "title": "feat: check branch",
                  "isDraft": true,
                  "mergeable": "MERGEABLE",
                  "mergeStateStatus": "CLEAN",
                  "reviewDecision": "",
                  "headRefName": "feature/check-pr",
                  "baseRefName": "main",
                  "mergedAt": null,
                  "mergeCommit": null,
                  "statusCheckRollup": []
                }
                """, "", 0);
        });

        var service = CreateService(runner);
        var session = new Session
        {
            Git = new GitContext
            {
                WorktreePath = "/tmp/worktree",
                BranchName = "feature/check-pr"
            }
        };

        var pr = await service.GetPrForBranchAsync(session);

        Assert.NotNull(pr);
        Assert.Equal(99, pr!.Number);
        Assert.True(pr.IsDraft);
        Assert.NotNull(session.Git.TrackedPr);
        Assert.Equal(99, session.Git.TrackedPr!.Number);
    }

    [Fact]
    public async Task IsGhAvailableAsync_ReturnsFalseWhenNotInstalled()
    {
        var runner = new FakeProcessRunner((_, _) =>
            new ProcessResult(false, "", "gh: command not found", 127));

        var service = CreateService(runner);

        var available = await service.IsGhAvailableAsync();

        Assert.False(available);
    }

    private static PullRequestService CreateService(FakeProcessRunner runner)
    {
        return new PullRequestService(
            runner,
            new FakeShellService(),
            new FakeHooksEngine(),
            new StaticOptionsMonitor<AppSettings>(new AppSettings { GhPath = "gh" }),
            NullLogger<PullRequestService>.Instance);
    }

    private sealed class FakeProcessRunner(Func<string, string[], ProcessResult> handler) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default) =>
            Task.FromResult(handler(options.FileName, options.Arguments));

        public Task<StreamingProcess> RunStreamingAsync(ProcessRunOptions options, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeShellService : IShellService
    {
        public Task<List<ShellInfo>> GetAvailableShellsAsync() => Task.FromResult<List<ShellInfo>>([]);
        public Task<ShellInfo> GetShellAsync() => Task.FromResult(new ShellInfo("/bin/zsh", "-c ", ShellType.Zsh));
        public Task<ShellInfo> GetTerminalShellAsync() => GetShellAsync();
        public Task<string?> GetLoginShellPathAsync() => Task.FromResult<string?>("/usr/bin");
        public Task<string?> WhichAsync(string executableName) => Task.FromResult<string?>("gh");
        public void InvalidateCache() { }
    }

    private sealed class FakeHooksEngine : IHooksEngine
    {
        public Task<List<HookExecutionResult>> FireAsync(HookEvent hookEvent, Dictionary<string, string>? env = null) =>
            Task.FromResult<List<HookExecutionResult>>([]);

        public Task AddHookAsync(HookDefinition hook) => Task.CompletedTask;
        public Task RemoveHookAsync(HookEvent hookEvent, string command) => Task.CompletedTask;
        public Task LoadAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
        public List<HookDefinition> GetHooks() => [];
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
