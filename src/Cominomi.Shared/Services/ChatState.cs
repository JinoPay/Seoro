using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class ChatState
{
    public Workspace? CurrentWorkspace { get; private set; }
    public Session? CurrentSession { get; private set; }
    public bool IsStreaming { get; private set; }

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

    public void UpdateWorkingDirectory(string path)
    {
        if (CurrentSession != null)
        {
            CurrentSession.WorkingDirectory = path;
            NotifyStateChanged();
        }
    }

    public void FinishMessage(ChatMessage message)
    {
        message.IsStreaming = false;
        NotifyStateChanged();
    }

    public void NotifyStateChanged() => OnChange?.Invoke();
}
