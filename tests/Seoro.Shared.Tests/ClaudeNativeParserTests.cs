using Seoro.Shared.Models.Chat;
using Seoro.Shared.Services.Sessions.Native;

namespace Seoro.Shared.Tests;

public class ClaudeNativeParserTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"claude_{Guid.NewGuid():N}.jsonl");

    public void Dispose()
    {
        try { File.Delete(_file); } catch { }
    }

    private List<ChatMessage> ParseLines(params string[] lines)
    {
        File.WriteAllLines(_file, lines);
        return ClaudeNativeParser.Parse(_file);
    }

    [Fact]
    public void Parse_UserStringMessage_ProducesUserMessage()
    {
        var msgs = ParseLines(
            """{"type":"user","uuid":"u1","timestamp":"2026-06-10T01:00:00Z","message":{"role":"user","content":"안녕하세요"}}""");

        var m = Assert.Single(msgs);
        Assert.Equal(MessageRole.User, m.Role);
        Assert.Equal("안녕하세요", m.Text);
        Assert.Equal("u1", m.Id);
        Assert.Single(m.Parts);
    }

    [Fact]
    public void Parse_AssistantWithThinkingTextAndTool_ProducesInterleavedParts()
    {
        var msgs = ParseLines(
            """{"type":"assistant","uuid":"a1","timestamp":"2026-06-10T01:00:01Z","message":{"role":"assistant","content":[{"type":"thinking","thinking":"생각중"},{"type":"text","text":"답변"},{"type":"tool_use","id":"toolu_1","name":"Bash","input":{"command":"ls"}}]}}""");

        var m = Assert.Single(msgs);
        Assert.Equal(MessageRole.Assistant, m.Role);
        Assert.Equal(3, m.Parts.Count);
        Assert.Equal(ContentPartType.Thinking, m.Parts[0].Type);
        Assert.Equal(ContentPartType.Text, m.Parts[1].Type);
        Assert.Equal(ContentPartType.ToolCall, m.Parts[2].Type);
        Assert.Equal("답변", m.Text);

        var tc = Assert.Single(m.ToolCalls);
        Assert.Equal("toolu_1", tc.Id);
        Assert.Equal("Bash", tc.Name);
        Assert.Contains("ls", tc.Input);
    }

    [Fact]
    public void Parse_ToolResultInFollowingUser_FillsOutput()
    {
        var msgs = ParseLines(
            """{"type":"assistant","uuid":"a1","timestamp":"2026-06-10T01:00:01Z","message":{"role":"assistant","content":[{"type":"tool_use","id":"toolu_1","name":"Bash","input":{}}]}}""",
            """{"type":"user","uuid":"u2","timestamp":"2026-06-10T01:00:02Z","message":{"role":"user","content":[{"tool_use_id":"toolu_1","type":"tool_result","content":"파일목록","is_error":false}]}}""");

        // tool_result-only user는 별도 메시지를 만들지 않음 → assistant 하나만
        var m = Assert.Single(msgs);
        var tc = Assert.Single(m.ToolCalls);
        Assert.Equal("파일목록", tc.Output);
        Assert.True(tc.IsComplete);
        Assert.False(tc.IsError);
    }

    [Fact]
    public void Parse_ToolResultError_SetsIsError()
    {
        var msgs = ParseLines(
            """{"type":"assistant","uuid":"a1","message":{"role":"assistant","content":[{"type":"tool_use","id":"t1","name":"Bash","input":{}}]}}""",
            """{"type":"user","uuid":"u2","message":{"role":"user","content":[{"tool_use_id":"t1","type":"tool_result","content":"오류","is_error":true}]}}""");

        Assert.True(Assert.Single(msgs).ToolCalls[0].IsError);
    }

    [Fact]
    public void Parse_SidechainLines_AreExcluded()
    {
        var msgs = ParseLines(
            """{"type":"user","uuid":"u1","isSidechain":true,"message":{"role":"user","content":"서브에이전트"}}""",
            """{"type":"assistant","uuid":"a1","isSidechain":true,"message":{"role":"assistant","content":[{"type":"text","text":"무시"}]}}""");

        Assert.Empty(msgs);
    }

    [Fact]
    public void Parse_NoiseAndSystemTypes_AreExcluded()
    {
        var msgs = ParseLines(
            """{"type":"file-history-snapshot","uuid":"x"}""",
            """{"type":"system","content":"hook"}""",
            """{"type":"summary","summary":"제목"}""",
            """{"type":"user","uuid":"u1","message":{"role":"user","content":"실제"}}""");

        var m = Assert.Single(msgs);
        Assert.Equal("실제", m.Text);
    }

    [Fact]
    public void Parse_UserToolResultArrayWithText_ProducesUserMessage()
    {
        // 텍스트 + tool_result 혼합 user → 텍스트 부분은 메시지가 됨
        var msgs = ParseLines(
            """{"type":"assistant","uuid":"a1","message":{"role":"assistant","content":[{"type":"tool_use","id":"t1","name":"X","input":{}}]}}""",
            """{"type":"user","uuid":"u2","message":{"role":"user","content":[{"tool_use_id":"t1","type":"tool_result","content":"out"},{"type":"text","text":"추가 질문"}]}}""");

        Assert.Equal(2, msgs.Count);
        Assert.Equal("추가 질문", msgs[1].Text);
        Assert.Equal(MessageRole.User, msgs[1].Role);
    }

    [Fact]
    public void Parse_TimestampParsedToUtc()
    {
        var msgs = ParseLines(
            """{"type":"user","uuid":"u1","timestamp":"2026-06-10T01:02:03Z","message":{"role":"user","content":"t"}}""");

        Assert.Equal(new DateTime(2026, 6, 10, 1, 2, 3, DateTimeKind.Utc), msgs[0].Timestamp);
    }
}
