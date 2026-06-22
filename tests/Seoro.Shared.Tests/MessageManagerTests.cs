namespace Seoro.Shared.Tests;

public class MessageManagerTests
{
    private int _notifyCount;
    private readonly MessageManager _sut;

    public MessageManagerTests()
    {
        _sut = new MessageManager(() => _notifyCount++);
    }

    private static Session NewSession() => new() { Id = "s1", Model = "sonnet", WorkspaceId = "ws-1" };

    [Fact]
    public void StartAssistantMessage_AddsStreamingAssistantMessage()
    {
        var session = NewSession();

        var msg = _sut.StartAssistantMessage(session);

        Assert.Single(session.Messages);
        Assert.Same(msg, session.Messages[0]);
        Assert.Equal(MessageRole.Assistant, msg.Role);
        Assert.True(msg.IsStreaming);
        Assert.NotNull(msg.StreamingStartedAt);
        Assert.Equal(1, _notifyCount);
    }

    [Fact]
    public void AddSystemMessage_AddsSystemRole()
    {
        var session = NewSession();

        _sut.AddSystemMessage(session, "hello");

        Assert.Single(session.Messages);
        Assert.Equal(MessageRole.System, session.Messages[0].Role);
        Assert.Equal("hello", session.Messages[0].Text);
        Assert.Equal(1, _notifyCount);
    }

    [Fact]
    public void AddUserMessage_AddsUserRole()
    {
        var session = NewSession();

        _sut.AddUserMessage(session, "hi there");

        Assert.Single(session.Messages);
        Assert.Equal(MessageRole.User, session.Messages[0].Role);
        Assert.Equal("hi there", session.Messages[0].Text);
        Assert.Equal(1, _notifyCount);
    }

    [Fact]
    public void AddUserMessage_WithAttachments_AttachesThem()
    {
        var session = NewSession();
        var attachments = new List<FileAttachment> { new() { OriginalFileName = "a.png" } };

        _sut.AddUserMessage(session, "with file", attachments);

        Assert.Single(session.Messages);
        Assert.Same(attachments, session.Messages[0].Attachments);
    }

    [Fact]
    public void AddToolCall_AddsToCallsAndParts()
    {
        var msg = new ChatMessage { Role = MessageRole.Assistant };
        var tool = new ToolCall { Name = "Read" };

        _sut.AddToolCall(msg, tool);

        Assert.Single(msg.ToolCalls);
        Assert.Same(tool, msg.ToolCalls[0]);
        Assert.Single(msg.Parts);
        Assert.Equal(ContentPartType.ToolCall, msg.Parts[0].Type);
        Assert.Same(tool, msg.Parts[0].ToolCall);
        Assert.Equal(1, _notifyCount);
    }

    [Fact]
    public void AppendText_CoalescesIntoTrailingTextPart()
    {
        var msg = new ChatMessage { Role = MessageRole.Assistant };

        _sut.AppendText(msg, "Hello ");
        _sut.AppendText(msg, "world");

        Assert.Equal("Hello world", msg.Text);
        Assert.Single(msg.Parts);
        Assert.Equal(ContentPartType.Text, msg.Parts[0].Type);
        Assert.Equal("Hello world", msg.Parts[0].Text);
        Assert.Equal(2, _notifyCount);
    }

    [Fact]
    public void AppendText_AfterNonText_StartsNewPart()
    {
        var msg = new ChatMessage { Role = MessageRole.Assistant };

        _sut.AppendThinking(msg, "thinking...");
        _sut.AppendText(msg, "answer");

        Assert.Equal(2, msg.Parts.Count);
        Assert.Equal(ContentPartType.Thinking, msg.Parts[0].Type);
        Assert.Equal(ContentPartType.Text, msg.Parts[1].Type);
        Assert.Equal("answer", msg.Parts[1].Text);
    }

    [Fact]
    public void AppendThinking_CoalescesIntoTrailingThinkingPart()
    {
        var msg = new ChatMessage { Role = MessageRole.Assistant };

        _sut.AppendThinking(msg, "step 1 ");
        _sut.AppendThinking(msg, "step 2");

        Assert.Single(msg.Parts);
        Assert.Equal(ContentPartType.Thinking, msg.Parts[0].Type);
        Assert.Equal("step 1 step 2", msg.Parts[0].Text);
    }

    [Fact]
    public void FinishMessage_ClearsStreamingAndSyncsTextFromTextParts()
    {
        var msg = new ChatMessage { Role = MessageRole.Assistant, IsStreaming = true };
        _sut.AppendThinking(msg, "ignored thinking");
        _sut.AppendText(msg, "visible ");
        _sut.AppendText(msg, "answer");

        _sut.FinishMessage(msg);

        Assert.False(msg.IsStreaming);
        Assert.NotNull(msg.StreamingFinishedAt);
        // Text is rebuilt from Text parts only (thinking excluded).
        Assert.Equal("visible answer", msg.Text);
    }
}
