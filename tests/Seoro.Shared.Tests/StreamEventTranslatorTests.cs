using System.Text.Json;
using AB = AgentBridge;
using Seoro.Shared.Models.Chat;
using Seoro.Shared.Services.Cli;

namespace Seoro.Shared.Tests;

public class StreamEventTranslatorTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    private static List<StreamEvent> Run(StreamEventTranslator t, AB.AgentMessage msg)
        => t.Translate(msg).ToList();

    [Fact]
    public void Claude_SystemInit_MapsToSystemInit()
    {
        var t = new StreamEventTranslator("claude");
        var events = Run(t, new AB.SystemMessage("init") { SessionId = "sess-1" });

        var evt = Assert.Single(events);
        Assert.Equal("system", evt.Type);
        Assert.Equal("init", evt.Subtype);
        Assert.Equal("sess-1", evt.SessionId);
    }

    [Fact]
    public void Codex_ThreadStarted_NormalizedToSystemInit()
    {
        var t = new StreamEventTranslator("codex");
        var events = Run(t, new AB.SystemMessage("thread.started") { SessionId = "thread-9" });

        var evt = Assert.Single(events);
        Assert.Equal("system", evt.Type);
        Assert.Equal("init", evt.Subtype);
        Assert.Equal("thread-9", evt.SessionId);
    }

    [Fact]
    public void System_OtherSubtype_Skipped()
    {
        var t = new StreamEventTranslator("codex");
        Assert.Empty(Run(t, new AB.SystemMessage("turn.started")));
    }

    [Fact]
    public void Partial_PassesThroughInnerEvent()
    {
        var t = new StreamEventTranslator("claude");
        var raw = Json("""
            {"type":"stream_event","session_id":"s","parent_tool_use_id":"parent-1",
             "event":{"type":"content_block_delta","index":2,"delta":{"type":"text_delta","text":"Hello"}}}
            """);
        var events = Run(t, new AB.PartialMessage("Hello", raw) { SessionId = "s" });

        var evt = Assert.Single(events);
        Assert.Equal("content_block_delta", evt.Type);
        Assert.Equal(2, evt.Index);
        Assert.Equal("text_delta", evt.Delta?.Type);
        Assert.Equal("Hello", evt.Delta?.Text);
        // 외부 래퍼의 parent_tool_use_id가 내부 이벤트로 전파되어야 한다.
        Assert.Equal("parent-1", evt.ParentToolUseId);
    }

    [Fact]
    public void Partial_ToolUseStart_PreservesNameForExitPlanMode()
    {
        var t = new StreamEventTranslator("claude");
        var raw = Json("""
            {"type":"stream_event",
             "event":{"type":"content_block_start","index":0,
                      "content_block":{"type":"tool_use","id":"tu1","name":"ExitPlanMode","input":{}}}}
            """);
        var evt = Assert.Single(Run(t, new AB.PartialMessage(null, raw)));
        Assert.Equal("content_block_start", evt.Type);
        Assert.Equal("tool_use", evt.ContentBlock?.Type);
        Assert.Equal("ExitPlanMode", evt.ContentBlock?.Name);
    }

    [Fact]
    public void Claude_TerminalAssistantMessage_SkippedWhenPartialsOn()
    {
        var t = new StreamEventTranslator("claude");
        var am = new AB.AssistantMessage(new AB.ContentBlock[] { new AB.TextBlock("already streamed") });
        Assert.Empty(Run(t, am));
    }

    [Fact]
    public void Codex_AgentMessage_EmitsAssistantText()
    {
        var t = new StreamEventTranslator("codex");
        var am = new AB.AssistantMessage(new AB.ContentBlock[] { new AB.TextBlock("hi there") })
        {
            SessionId = "s",
        };
        var evt = Assert.Single(Run(t, am));
        Assert.Equal("assistant", evt.Type);
        var block = Assert.Single(evt.Message!.Content!);
        Assert.Equal("text", block.Type);
        Assert.Equal("hi there", block.Text);
    }

    [Fact]
    public void Codex_CommandExecution_NormalizedToBashWithCommandInput()
    {
        var t = new StreamEventTranslator("codex");
        var item = Json("""{"id":"c1","type":"command_execution","command":"ls -la","aggregated_output":"x"}""");
        var am = new AB.AssistantMessage(new AB.ContentBlock[]
        {
            new AB.ToolUseBlock("c1", "command_execution", item),
        });

        var evt = Assert.Single(Run(t, am));
        var block = Assert.Single(evt.Message!.Content!);
        Assert.Equal("tool_use", block.Type);
        Assert.Equal("Bash", block.Name);
        Assert.Equal("c1", block.Id);
        Assert.Equal("ls -la", block.Input!.Value.GetProperty("command").GetString());
    }

    [Fact]
    public void Codex_FileChange_NormalizedToEditWithPathInput()
    {
        var t = new StreamEventTranslator("codex");
        var item = Json("""{"id":"f1","type":"file_change","changes":[{"path":"/repo/.claude/plans/foo.md"}]}""");
        var am = new AB.AssistantMessage(new AB.ContentBlock[]
        {
            new AB.ToolUseBlock("f1", "file_change", item),
        });

        var evt = Assert.Single(Run(t, am));
        var block = Assert.Single(evt.Message!.Content!);
        Assert.Equal("Edit", block.Name);
        Assert.Equal("/repo/.claude/plans/foo.md", block.Input!.Value.GetProperty("path").GetString());
    }

    [Fact]
    public void UserMessage_ToolResult_PreservesToolUseIdAndContent()
    {
        var t = new StreamEventTranslator("codex");
        var um = new AB.UserMessage(new AB.ContentBlock[]
        {
            new AB.ToolResultBlock("tu-42", "command output", IsError: true),
        });

        var evt = Assert.Single(Run(t, um));
        Assert.Equal("user", evt.Type);
        var block = Assert.Single(evt.Message!.Content!);
        Assert.Equal("tool_result", block.Type);
        Assert.Equal("tu-42", block.ToolUseId);
        Assert.True(block.IsError);
        Assert.Equal("command output", block.Content!.Value.GetString());
    }

    [Fact]
    public void Result_MapsUsageCostAndError()
    {
        var t = new StreamEventTranslator("claude");
        var rm = new AB.ResultMessage("error_max_turns", IsError: true)
        {
            SessionId = "s",
            Result = "boom",
            TotalCostUsd = 0.1234,
            Usage = new AB.TokenUsage
            {
                InputTokens = 100,
                OutputTokens = 50,
                CacheReadInputTokens = 10,
            },
        };

        var events = Run(t, rm);
        Assert.Equal(2, events.Count);

        var result = events[0];
        Assert.Equal("result", result.Type);
        Assert.Equal("error_max_turns", result.Subtype);
        Assert.Equal(0.1234m, result.TotalCostUsd);
        Assert.Equal(100, result.Usage!.InputTokens);
        Assert.Equal(50, result.Usage!.OutputTokens);
        Assert.Equal(10, result.Usage!.CacheReadInputTokens);

        var error = events[1];
        Assert.Equal("error", error.Type);
        Assert.Equal("boom", error.GetErrorMessage());
    }

    [Fact]
    public void Error_MapsToErrorEvent()
    {
        var t = new StreamEventTranslator("claude");
        var evt = Assert.Single(Run(t, new AB.ErrorMessage("something failed")));
        Assert.Equal("error", evt.Type);
        Assert.Equal("something failed", evt.GetErrorMessage());
    }
}
