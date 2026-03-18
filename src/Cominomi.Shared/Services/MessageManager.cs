using Cominomi.Shared;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class MessageManager

{
    private readonly Action _notifyChanged;

    public MessageManager(Action notifyChanged)
    {
        _notifyChanged = notifyChanged;
    }

    public void AddUserMessage(Session session, string text)
    {
        Guard.NotNull(session, nameof(session));
        Guard.NotNull(text, nameof(text));

        session.Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Text = text
        });
        _notifyChanged();
    }

    public void AddUserMessage(Session session, string text, List<FileAttachment> attachments)
    {
        Guard.NotNull(session, nameof(session));
        Guard.NotNull(text, nameof(text));
        Guard.NotNull(attachments, nameof(attachments));

        session.Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Text = text,
            Attachments = attachments
        });
        _notifyChanged();
    }

    public ChatMessage StartAssistantMessage(Session session)
    {
        var msg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            IsStreaming = true,
            StreamingStartedAt = DateTime.UtcNow
        };
        session.Messages.Add(msg);
        _notifyChanged();
        return msg;
    }

    public void AppendText(ChatMessage message, string text)
    {
        Guard.NotNull(message, nameof(message));
        Guard.NotNull(text, nameof(text));

        message.Text += text;

        var lastPart = message.Parts.Count > 0 ? message.Parts[^1] : null;
        if (lastPart?.Type == ContentPartType.Text)
        {
            lastPart.Text += text;
        }
        else
        {
            message.Parts.Add(new ContentPart
            {
                Type = ContentPartType.Text,
                Text = text
            });
        }

        _notifyChanged();
    }

    public void AppendThinking(ChatMessage message, string text)
    {
        var lastPart = message.Parts.Count > 0 ? message.Parts[^1] : null;
        if (lastPart?.Type == ContentPartType.Thinking)
        {
            lastPart.Text += text;
        }
        else
        {
            message.Parts.Add(new ContentPart
            {
                Type = ContentPartType.Thinking,
                Text = text
            });
        }

        _notifyChanged();
    }

    public void AddToolCall(ChatMessage message, ToolCall toolCall)
    {
        Guard.NotNull(message, nameof(message));
        Guard.NotNull(toolCall, nameof(toolCall));

        message.ToolCalls.Add(toolCall);

        message.Parts.Add(new ContentPart
        {
            Type = ContentPartType.ToolCall,
            ToolCall = toolCall
        });

        _notifyChanged();
    }

    public void FinishMessage(ChatMessage message)
    {
        message.IsStreaming = false;
        message.StreamingFinishedAt = DateTime.UtcNow;
        _notifyChanged();
    }

    public void AddSystemMessage(Session session, string text)
    {
        session.Messages.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Text = text
        });
        _notifyChanged();
    }
}
