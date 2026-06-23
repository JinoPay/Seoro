using Seoro.Shared.Models.Chat;
using Seoro.Shared.Services.Sessions.Native;

namespace Seoro.Shared.Tests;

public class CodexRolloutParserTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"codex_{Guid.NewGuid():N}.jsonl");

    public void Dispose()
    {
        try { File.Delete(_file); } catch { }
    }

    private List<ChatMessage> ParseLines(params string[] lines)
    {
        File.WriteAllLines(_file, lines);
        return CodexRolloutParser.Parse(_file);
    }

    [Fact]
    public void Parse_AssistantOutputText_ProducesAssistantMessage()
    {
        var msgs = ParseLines(
            """{"timestamp":"2026-05-22T13:49:09Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"완료했습니다"}]}}""");

        var m = Assert.Single(msgs);
        Assert.Equal(MessageRole.Assistant, m.Role);
        Assert.Equal("완료했습니다", m.Text);
    }

    [Fact]
    public void Parse_UserInputText_StripsSyntheticPrefix()
    {
        var msgs = ParseLines(
            """{"timestamp":"2026-05-22T13:49:09Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"[System Instructions]\n브랜치 변경 금지\n\n[User]\n버그를 고쳐줘"}]}}""");

        var m = Assert.Single(msgs);
        Assert.Equal(MessageRole.User, m.Role);
        Assert.Equal("버그를 고쳐줘", m.Text);
    }

    [Fact]
    public void Parse_DeveloperRole_IsExcluded()
    {
        var msgs = ParseLines(
            """{"timestamp":"2026-05-22T13:49:09Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"<permissions instructions>"}]}}""");

        Assert.Empty(msgs);
    }

    [Fact]
    public void Parse_EnvironmentContextUser_IsExcluded()
    {
        var msgs = ParseLines(
            """{"timestamp":"2026-05-22T13:49:09Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"<environment_context>\n  <cwd>/x</cwd>\n</environment_context>"}]}}""");

        Assert.Empty(msgs);
    }

    [Fact]
    public void Parse_FunctionCallWithOutput_ProducesToolCall()
    {
        var msgs = ParseLines(
            """{"timestamp":"2026-05-22T13:49:10Z","type":"response_item","payload":{"type":"function_call","name":"exec_command","arguments":"{\"cmd\":\"git status\"}","call_id":"call_1"}}""",
            """{"timestamp":"2026-05-22T13:49:11Z","type":"response_item","payload":{"type":"function_call_output","call_id":"call_1","output":"clean"}}""");

        var m = Assert.Single(msgs);
        var tc = Assert.Single(m.ToolCalls);
        Assert.Equal("call_1", tc.Id);
        Assert.Equal("exec_command", tc.Name);
        Assert.Contains("git status", tc.Input);
        Assert.Equal("clean", tc.Output);
        Assert.True(tc.IsComplete);
    }

    [Fact]
    public void Parse_CustomToolCall_UsesInputField()
    {
        var msgs = ParseLines(
            """{"timestamp":"2026-05-22T13:49:10Z","type":"response_item","payload":{"type":"custom_tool_call","status":"completed","call_id":"call_2","name":"apply_patch","input":"*** Begin Patch"}}""",
            """{"timestamp":"2026-05-22T13:49:11Z","type":"response_item","payload":{"type":"custom_tool_call_output","call_id":"call_2","output":"applied"}}""");

        var tc = Assert.Single(Assert.Single(msgs).ToolCalls);
        Assert.Equal("apply_patch", tc.Name);
        Assert.Contains("Begin Patch", tc.Input);
        Assert.Equal("applied", tc.Output);
    }

    [Fact]
    public void Parse_ReasoningWithSummary_ProducesThinking()
    {
        var msgs = ParseLines(
            """{"timestamp":"2026-05-22T13:49:09Z","type":"response_item","payload":{"type":"reasoning","summary":[{"type":"summary_text","text":"계획 수립"}],"encrypted_content":"abc"}}""");

        var m = Assert.Single(msgs);
        var part = Assert.Single(m.Parts);
        Assert.Equal(ContentPartType.Thinking, part.Type);
        Assert.Equal("계획 수립", part.Text);
    }

    [Fact]
    public void Parse_ReasoningEmptySummary_IsExcluded()
    {
        var msgs = ParseLines(
            """{"timestamp":"2026-05-22T13:49:09Z","type":"response_item","payload":{"type":"reasoning","summary":[],"encrypted_content":"abc"}}""");

        Assert.Empty(msgs);
    }

    [Fact]
    public void Parse_EventMsgLines_AreIgnored()
    {
        // event_msg는 response_item과 중복되므로 무시 — response_item만 카운트
        var msgs = ParseLines(
            """{"timestamp":"2026-05-22T13:49:09Z","type":"event_msg","payload":{"type":"agent_message","message":"중복"}}""",
            """{"timestamp":"2026-05-22T13:49:09Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"원본"}]}}""");

        var m = Assert.Single(msgs);
        Assert.Equal("원본", m.Text);
    }
}
