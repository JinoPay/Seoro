using System.Runtime.CompilerServices;
using AB = AgentBridge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Seoro.Shared.Models.Chat;
using Seoro.Shared.Models.Settings;
using Seoro.Shared.Services.Cli;

namespace Seoro.Shared.Tests;

public class AgentBridgeCliProviderTests
{
    private static CliSendOptions Send(string sessionId = "s1") => new()
    {
        Message = "hi",
        WorkingDir = "/work",
        Model = "claude-x",
        SessionId = sessionId,
    };

    private static AgentBridgeCliProvider NewProvider(AB.IAgentProvider agent)
        => new(agent, new StubOptionsMonitor(new AppSettings()), NullLogger<AgentBridgeCliProvider>.Instance);

    [Fact]
    public async Task HappyPath_TranslatesInitAndResult()
    {
        var agent = new ScriptedAgent("claude", new AB.AgentMessage[]
        {
            new AB.SystemMessage("init") { SessionId = "conv-1" },
            new AB.ResultMessage("success", IsError: false),
        });
        var provider = NewProvider(agent);

        var events = new List<StreamEvent>();
        await foreach (var ev in provider.SendMessageAsync(Send()))
            events.Add(ev);

        Assert.Equal("system", events[0].Type);
        Assert.Equal("conv-1", events[0].SessionId);
        Assert.Equal("result", events[^1].Type);
    }

    [Fact]
    public async Task Cancel_StopsInFlightStream()
    {
        var agent = new BlockingAgent("claude");
        var provider = NewProvider(agent);

        var received = new List<StreamEvent>();
        var run = Task.Run(async () =>
        {
            await foreach (var ev in provider.SendMessageAsync(Send("s1")))
                received.Add(ev);
        });

        await agent.Blocking.Task; // 첫 이벤트 후 블록 진입까지 대기
        provider.Cancel("s1");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(received); // init만 받고 취소됨
        Assert.Equal("system", received[0].Type);
    }

    [Fact]
    public async Task SecondSend_CancelsPreviousForSameSession()
    {
        var agent = new BlockingAgent("claude");
        var provider = NewProvider(agent);

        var run1 = Task.Run(async () =>
        {
            await foreach (var _ in provider.SendMessageAsync(Send("s1")))
            {
            }
        });

        await agent.Blocking.Task; // 첫 스트림이 블록에 진입

        // 같은 세션으로 두 번째 전송을 시작한다. SendMessageAsync는 async iterator라 본문(이전 스트림 취소)은
        // 첫 MoveNextAsync 호출 시점에 실행된다 → 명시적으로 한 번 진행시킨다.
        var run2Enumerator = provider.SendMessageAsync(Send("s1")).GetAsyncEnumerator();
        try
        {
            await run2Enumerator.MoveNextAsync(); // 이전 세션 스트림 취소 트리거
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run1.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            // 두 번째 스트림도 취소해 BlockingAgent의 무한 대기를 정리한다.
            provider.Cancel("s1");
            await run2Enumerator.DisposeAsync();
        }
    }

    // ── 테스트 더블 ──────────────────────────────────────────────

    private sealed class StubOptionsMonitor(AppSettings value) : IOptionsMonitor<AppSettings>
    {
        public AppSettings CurrentValue { get; } = value;
        public AppSettings Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AppSettings, string?> listener) => null;
    }

    private abstract class FakeAgentBase(string id) : AB.IAgentProvider
    {
        public string Id { get; } = id;
        public string DisplayName => Id;
        public AB.AgentCapabilities Capabilities => new();

        public ValueTask<AB.AgentVersion> ProbeVersionAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AB.AgentVersion("1.0.0"));

        public ValueTask<AB.AgentInstallation> DetectInstallationAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AB.AgentInstallation(true, "/bin/" + Id));

        public ValueTask<AB.IAgentSession> CreateSessionAsync(
            AB.AgentOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public abstract IAsyncEnumerable<AB.AgentMessage> QueryAsync(
            AB.AgentPrompt prompt, AB.AgentOptions? options = null, CancellationToken cancellationToken = default);
    }

    private sealed class ScriptedAgent(string id, AB.AgentMessage[] messages) : FakeAgentBase(id)
    {
        public override async IAsyncEnumerable<AB.AgentMessage> QueryAsync(
            AB.AgentPrompt prompt, AB.AgentOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var m in messages)
            {
                await Task.Yield();
                yield return m;
            }
        }
    }

    private sealed class BlockingAgent(string id) : FakeAgentBase(id)
    {
        public TaskCompletionSource Blocking { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async IAsyncEnumerable<AB.AgentMessage> QueryAsync(
            AB.AgentPrompt prompt, AB.AgentOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new AB.SystemMessage("init") { SessionId = "conv-1" };
            Blocking.TrySetResult();
            // 취소될 때까지 블록(취소 시 OperationCanceledException).
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield return new AB.ResultMessage("success", IsError: false);
        }
    }
}
