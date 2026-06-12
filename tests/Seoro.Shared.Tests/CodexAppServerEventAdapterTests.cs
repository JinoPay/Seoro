using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seoro.Shared.Services.Codex.AppServer;

namespace Seoro.Shared.Tests;

public class CodexAppServerEventAdapterTests
{
    private readonly CodexAppServerEventAdapter _sut = new(NullLogger.Instance);

    private static JsonElement P(string json) => JsonDocument.Parse(json).RootElement;

    // --- thread/started → system init ---

    [Fact]
    public void ThreadStarted_EmitsSystemInitWithThreadId()
    {
        var events = _sut.Adapt("thread/started", P("""{"thread":{"id":"th_123"}}""")).ToList();

        var e = Assert.Single(events);
        Assert.Equal("system", e.Type);
        Assert.Equal("init", e.Subtype);
        Assert.Equal("th_123", e.SessionId);
    }

    // --- agentMessage 스트리밍 흐름 (실측 형식) ---

    [Fact]
    public void AgentMessage_Start_EmitsTextContentBlockStart()
    {
        var events = _sut.Adapt("item/started",
            P("""{"item":{"id":"m1","type":"agentMessage","text":""}}""")).ToList();

        var e = Assert.Single(events);
        Assert.Equal("content_block_start", e.Type);
        Assert.Equal(0, e.Index);
        Assert.Equal("text", e.ContentBlock!.Type);
    }

    [Fact]
    public void AgentMessage_Delta_EmitsIncrementalTextDelta()
    {
        _sut.Adapt("item/started", P("""{"item":{"id":"m1","type":"agentMessage","text":""}}""")).ToList();

        var events = _sut.Adapt("item/agentMessage/delta", P("""{"itemId":"m1","delta":"CODE"}""")).ToList();

        var e = Assert.Single(events);
        Assert.Equal("content_block_delta", e.Type);
        Assert.Equal(0, e.Index);
        Assert.Equal("text_delta", e.Delta!.Type);
        Assert.Equal("CODE", e.Delta.Text);
    }

    [Fact]
    public void AgentMessage_Completed_WithFullDeltas_EmitsOnlyStop()
    {
        _sut.Adapt("item/started", P("""{"item":{"id":"m1","type":"agentMessage","text":""}}""")).ToList();
        _sut.Adapt("item/agentMessage/delta", P("""{"itemId":"m1","delta":"CODEX_PONG"}""")).ToList();

        var events = _sut.Adapt("item/completed",
            P("""{"item":{"id":"m1","type":"agentMessage","text":"CODEX_PONG"}}""")).ToList();

        // delta로 이미 전체 텍스트가 갔으므로 보충 없이 stop만
        var e = Assert.Single(events);
        Assert.Equal("content_block_stop", e.Type);
        Assert.Equal(0, e.Index);
    }

    [Fact]
    public void AgentMessage_Completed_WithMissingDelta_EmitsRemainderThenStop()
    {
        _sut.Adapt("item/started", P("""{"item":{"id":"m1","type":"agentMessage","text":""}}""")).ToList();

        var events = _sut.Adapt("item/completed",
            P("""{"item":{"id":"m1","type":"agentMessage","text":"Hi"}}""")).ToList();

        Assert.Equal(2, events.Count);
        Assert.Equal("content_block_delta", events[0].Type);
        Assert.Equal("Hi", events[0].Delta!.Text);
        Assert.Equal("content_block_stop", events[1].Type);
    }

    // --- userMessage / reasoning 은 표시 생략 ---

    [Fact]
    public void UserMessage_Started_EmitsNothing()
    {
        var events = _sut.Adapt("item/started",
            P("""{"item":{"id":"u1","type":"userMessage","content":[]}}""")).ToList();
        Assert.Empty(events);
    }

    // --- 도구류 → tool_use 블록 ---

    [Fact]
    public void CommandExecution_Started_EmitsToolUseBlock()
    {
        var events = _sut.Adapt("item/started",
            P("""{"item":{"id":"c1","type":"commandExecution","command":"ls"}}""")).ToList();

        var e = Assert.Single(events);
        Assert.Equal("content_block_start", e.Type);
        Assert.Equal("tool_use", e.ContentBlock!.Type);
        Assert.Equal("Bash", e.ContentBlock.Name);
        Assert.Equal("c1", e.ContentBlock.Id);
    }

    // --- turn/completed ---

    [Fact]
    public void TurnCompleted_Success_EmitsResultWithUsage()
    {
        _sut.Adapt("thread/tokenUsage/updated",
            P("""{"tokenUsage":{"last":{"inputTokens":100,"outputTokens":20,"cachedInputTokens":10}}}""")).ToList();

        var events = _sut.Adapt("turn/completed", P("""{"turn":{"status":"completed"}}""")).ToList();

        var e = Assert.Single(events);
        Assert.Equal("result", e.Type);
        Assert.NotNull(e.Usage);
        Assert.Equal(100, e.Usage!.InputTokens);
        Assert.Equal(20, e.Usage.OutputTokens);
        Assert.Equal(10, e.Usage.CacheReadInputTokens);
    }

    [Fact]
    public void TurnCompleted_Failed_EmitsError()
    {
        var events = _sut.Adapt("turn/completed",
            P("""{"turn":{"status":"failed","error":{"message":"boom"}}}""")).ToList();

        var e = Assert.Single(events);
        Assert.Equal("error", e.Type);
        Assert.Contains("boom", e.GetErrorMessage());
    }
}
