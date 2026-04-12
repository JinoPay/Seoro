using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seoro.Shared.Services.Codex;

namespace Seoro.Shared.Tests;

public class CodexEventConverterTests
{
    private static CodexEventConverter CreateConverter() => new(NullLogger.Instance);

    private static JsonElement Parse(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    // ── thread.started ──

    [Fact]
    public void ThreadStarted_EmitsSystemInit()
    {
        var converter = CreateConverter();
        var json = """{"type":"thread.started","thread_id":"t-abc"}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Single(events);
        Assert.Equal("system", events[0].Type);
        Assert.Equal("init", events[0].Subtype);
        Assert.Equal("t-abc", events[0].SessionId);
    }

    [Fact]
    public void ThreadStarted_OnlyEmitsInitOnce()
    {
        var converter = CreateConverter();
        var json = """{"type":"thread.started","thread_id":"t-abc"}""";

        var first = converter.Convert(Parse(json)).ToList();
        var second = converter.Convert(Parse(json)).ToList();

        Assert.Single(first);
        Assert.Empty(second);
    }

    // ── turn.started ──

    [Fact]
    public void TurnStarted_EmitsMessageStart()
    {
        var converter = CreateConverter();
        var json = """{"type":"turn.started"}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Single(events);
        Assert.Equal("message_start", events[0].Type);
        Assert.Equal("assistant", events[0].Message?.Role);
    }

    // ── item.started + agent_message ──

    [Fact]
    public void ItemStarted_AgentMessage_EmitsContentBlockStart()
    {
        var converter = CreateConverter();
        var json = """{"type":"item.started","item":{"id":"i1","type":"agent_message","text":"Hello"}}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Single(events);
        Assert.Equal("content_block_start", events[0].Type);
        Assert.Equal("text", events[0].ContentBlock?.Type);
    }

    // ── item.updated + agent_message → delta ──

    [Fact]
    public void ItemUpdated_AgentMessage_EmitsTextDelta()
    {
        var converter = CreateConverter();
        // item.started initializes accumulator to "" (ignores any initial text field)
        converter.Convert(Parse("""{"type":"item.started","item":{"id":"i1","type":"agent_message","text":""}}""")).ToList();

        // item.updated: prev="" → delta is the full new text
        var updated = """{"type":"item.updated","item":{"id":"i1","type":"agent_message","text":"Hello world"}}""";
        var events = converter.Convert(Parse(updated)).ToList();

        Assert.Single(events);
        Assert.Equal("content_block_delta", events[0].Type);
        Assert.Equal("text_delta", events[0].Delta?.Type);
        Assert.Equal("Hello world", events[0].Delta?.Text);
    }

    [Fact]
    public void ItemUpdated_AgentMessage_AccumulatesCorrectly()
    {
        var converter = CreateConverter();
        // accumulator starts at ""
        converter.Convert(Parse("""{"type":"item.started","item":{"id":"i1","type":"agent_message","text":""}}""")).ToList();
        // first update: delta = "AB" (full text), accum = "AB"
        converter.Convert(Parse("""{"type":"item.updated","item":{"id":"i1","type":"agent_message","text":"AB"}}""")).ToList();

        // second update: prev="AB", new="ABC" → delta = "C"
        var events = converter.Convert(Parse("""{"type":"item.updated","item":{"id":"i1","type":"agent_message","text":"ABC"}}""")).ToList();

        Assert.Single(events);
        Assert.Equal("C", events[0].Delta?.Text);
    }

    // ── item.completed + agent_message ──

    [Fact]
    public void ItemCompleted_AgentMessage_EmitsDeltaAndBlockStop()
    {
        var converter = CreateConverter();
        converter.Convert(Parse("""{"type":"item.started","item":{"id":"i1","type":"agent_message","text":"Hi"}}""")).ToList();

        var events = converter.Convert(Parse("""{"type":"item.completed","item":{"id":"i1","type":"agent_message","text":"Hi"}}""")).ToList();

        Assert.Contains(events, e => e.Type == "content_block_delta" && e.Delta?.Text == "Hi");
        Assert.Contains(events, e => e.Type == "content_block_stop");
    }

    // ── turn.completed ──

    [Fact]
    public void TurnCompleted_EmitsResultWithUsage()
    {
        var converter = CreateConverter();
        var json = """{"type":"turn.completed","usage":{"input_tokens":100,"output_tokens":50,"cached_input_tokens":20}}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Single(events);
        Assert.Equal("result", events[0].Type);
        Assert.NotNull(events[0].Usage);
        Assert.Equal(100, events[0].Usage!.InputTokens);
        Assert.Equal(50, events[0].Usage!.OutputTokens);
        Assert.Equal(20, events[0].Usage!.CacheReadInputTokens);
    }

    // ── turn.failed ──

    [Fact]
    public void TurnFailed_EmitsError()
    {
        var converter = CreateConverter();
        var json = """{"type":"turn.failed","error":{"message":"Something went wrong"}}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Single(events);
        Assert.Equal("error", events[0].Type);
    }

    // ── error ──

    [Fact]
    public void TopLevelError_EmitsError()
    {
        var converter = CreateConverter();
        var json = """{"type":"error","message":"Network failure"}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Single(events);
        Assert.Equal("error", events[0].Type);
    }

    // ── command_execution ──

    [Fact]
    public void ItemStarted_CommandExecution_EmitsToolUseStart()
    {
        var converter = CreateConverter();
        var json = """{"type":"item.started","item":{"id":"c1","type":"command_execution","command":"ls","status":"in_progress"}}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Single(events);
        Assert.Equal("content_block_start", events[0].Type);
        Assert.Equal("tool_use", events[0].ContentBlock?.Type);
        Assert.Equal("Bash", events[0].ContentBlock?.Name);
    }

    [Fact]
    public void ItemCompleted_CommandExecution_EmitsToolResultAfterStop()
    {
        var converter = CreateConverter();
        converter.Convert(Parse("""{"type":"item.started","item":{"id":"c1","type":"command_execution","command":"ls","status":"in_progress"}}""")).ToList();

        var events = converter.Convert(Parse("""{"type":"item.completed","item":{"id":"c1","type":"command_execution","command":"ls","aggregated_output":"file.txt","exit_code":0,"status":"completed"}}""")).ToList();

        Assert.Contains(events, e => e.Type == "content_block_stop");
        Assert.Contains(events, e => e.Type == "content_block_start" && e.ContentBlock?.Type == "tool_result");
    }

    // ── reasoning ──

    [Fact]
    public void ItemStarted_Reasoning_EmitsThinkingBlockStart()
    {
        var converter = CreateConverter();
        var json = """{"type":"item.started","item":{"id":"r1","type":"reasoning","summary":[]}}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Single(events);
        Assert.Equal("content_block_start", events[0].Type);
        Assert.Equal("thinking", events[0].ContentBlock?.Type);
    }

    // ── unknown type ──

    [Fact]
    public void UnknownType_EmitsNothing()
    {
        var converter = CreateConverter();
        var json = """{"type":"some.future.event","data":{}}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Empty(events);
    }

    // ── missing type field ──

    [Fact]
    public void MissingTypeField_EmitsNothing()
    {
        var converter = CreateConverter();
        var json = """{"not_type":"foo"}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Empty(events);
    }

    // ── file_search ──

    [Fact]
    public void ItemStarted_FileSearch_EmitsToolUseStart()
    {
        var converter = CreateConverter();
        var json = """{"type":"item.started","item":{"id":"fs1","type":"file_search"}}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Single(events);
        Assert.Equal("content_block_start", events[0].Type);
        Assert.Equal("tool_use", events[0].ContentBlock?.Type);
        Assert.Equal("FileSearch", events[0].ContentBlock?.Name);
    }

    // ── mcp_elicitation ──

    [Fact]
    public void ItemStarted_McpElicitation_EmitsToolUseStart()
    {
        var converter = CreateConverter();
        var json = """{"type":"item.started","item":{"id":"me1","type":"mcp_elicitation"}}""";

        var events = converter.Convert(Parse(json)).ToList();

        Assert.Single(events);
        Assert.Equal("content_block_start", events[0].Type);
        Assert.Equal("tool_use", events[0].ContentBlock?.Type);
        Assert.Equal("McpElicitation", events[0].ContentBlock?.Name);
    }

    // ── command_execution streaming output ──

    [Fact]
    public void ItemUpdated_CommandExecution_EmitsStreamingDelta()
    {
        var converter = CreateConverter();
        // item.started 초기화
        converter.Convert(Parse("""{"type":"item.started","item":{"id":"c2","type":"command_execution","command":"ls","status":"in_progress"}}""")).ToList();

        // item.updated with aggregated_output
        var events = converter.Convert(Parse("""{"type":"item.updated","item":{"id":"c2","type":"command_execution","command":"ls","aggregated_output":"file1.txt\nfile2.txt","status":"in_progress"}}""")).ToList();

        Assert.Single(events);
        Assert.Equal("content_block_delta", events[0].Type);
        Assert.Equal("text_delta", events[0].Delta?.Type);
        Assert.Equal("file1.txt\nfile2.txt", events[0].Delta?.Text);
    }

    [Fact]
    public void ItemUpdated_CommandExecution_AccumulatesCorrectly()
    {
        var converter = CreateConverter();
        converter.Convert(Parse("""{"type":"item.started","item":{"id":"c3","type":"command_execution","command":"cat","status":"in_progress"}}""")).ToList();
        converter.Convert(Parse("""{"type":"item.updated","item":{"id":"c3","type":"command_execution","command":"cat","aggregated_output":"line1","status":"in_progress"}}""")).ToList();

        // 두 번째 업데이트: delta는 추가된 부분만
        var events = converter.Convert(Parse("""{"type":"item.updated","item":{"id":"c3","type":"command_execution","command":"cat","aggregated_output":"line1\nline2","status":"in_progress"}}""")).ToList();

        Assert.Single(events);
        Assert.Equal("\nline2", events[0].Delta?.Text);
    }
}
