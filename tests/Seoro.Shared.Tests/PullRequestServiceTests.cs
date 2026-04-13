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
        Assert.Equal("https://github.com/acme/seoro/pull/5", restored.Git.LastPrUrl);
    }

    private static PullRequestService CreateService(FakeProcessRunner runner)
    {
        return new PullRequestService(
            runner,
            new FakeShellService(),
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

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
