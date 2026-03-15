using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public enum StreamingPhase
{
    None,
    Sending,
    Thinking,
    WritingText,
    UsingTool
}

public class ChatState
{
    public Workspace? CurrentWorkspace { get; private set; }
    public Session? CurrentSession { get; private set; }
    public bool IsStreaming { get; private set; }
    public StreamingPhase Phase { get; private set; }
    public string? ActiveToolName { get; private set; }
    public bool IsSpotlightActive { get; private set; }

    public event Action? OnChange;

    public void SetWorkspace(Workspace workspace)
    {
        CurrentWorkspace = workspace;
        CurrentSession = null;
        NotifyStateChanged();
    }

    public void SetSession(Session session)
    {
        CurrentSession = session;
        NotifyStateChanged();
    }

    public void SetStreaming(bool streaming)
    {
        IsStreaming = streaming;
        if (!streaming)
        {
            Phase = StreamingPhase.None;
            ActiveToolName = null;
        }
        NotifyStateChanged();
    }

    public void SetPhase(StreamingPhase phase, string? toolName = null)
    {
        if (Phase == phase && ActiveToolName == toolName) return;
        Phase = phase;
        ActiveToolName = toolName;
        NotifyStateChanged();
    }

    public void AddUserMessage(string text)
    {
        CurrentSession?.Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Text = text
        });
        NotifyStateChanged();
    }

    public void AddUserMessage(string text, List<FileAttachment> attachments)
    {
        CurrentSession?.Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Text = text,
            Attachments = attachments
        });
        NotifyStateChanged();
    }

    public ChatMessage StartAssistantMessage()
    {
        var msg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            IsStreaming = true
        };
        CurrentSession?.Messages.Add(msg);
        NotifyStateChanged();
        return msg;
    }

    public void AppendText(ChatMessage message, string text)
    {
        message.Text += text;
        NotifyStateChanged();
    }

    public void AddToolCall(ChatMessage message, ToolCall toolCall)
    {
        message.ToolCalls.Add(toolCall);
        NotifyStateChanged();
    }

    public void FinishMessage(ChatMessage message)
    {
        message.IsStreaming = false;
        NotifyStateChanged();
    }

    public void SetSpotlightActive(bool active)
    {
        IsSpotlightActive = active;
        NotifyStateChanged();
    }

    public void NotifyStateChanged() => OnChange?.Invoke();
}
