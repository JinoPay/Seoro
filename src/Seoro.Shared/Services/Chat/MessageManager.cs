
namespace Seoro.Shared.Services.Chat;

public class MessageManager(Action notifyChanged)
{
    public ChatMessage StartAssistantMessage(Session session)
    {
        var msg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            IsStreaming = true,
            StreamingStartedAt = DateTime.UtcNow
        };
        lock (session.MessagesLock)
        {
            session.Messages.Add(msg);
        }

        notifyChanged();
        return msg;
    }

    public void AddSystemMessage(Session session, string text)
    {
        lock (session.MessagesLock)
        {
            session.Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Text = text
            });
        }

        notifyChanged();
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

        notifyChanged();
    }

    public void AddUserMessage(Session session, string text)
    {
        Guard.NotNull(session, nameof(session));
        Guard.NotNull(text, nameof(text));

        lock (session.MessagesLock)
        {
            session.Messages.Add(new ChatMessage
            {
                Role = MessageRole.User,
                Text = text
            });
        }

        notifyChanged();
    }

    public void AddUserMessage(Session session, string text, List<FileAttachment> attachments)
    {
        Guard.NotNull(session, nameof(session));
        Guard.NotNull(text, nameof(text));
        Guard.NotNull(attachments, nameof(attachments));

        lock (session.MessagesLock)
        {
            session.Messages.Add(new ChatMessage
            {
                Role = MessageRole.User,
                Text = text,
                Attachments = attachments
            });
        }

        notifyChanged();
    }

    public void AppendText(ChatMessage message, string text)
    {
        Guard.NotNull(message, nameof(message));
        Guard.NotNull(text, nameof(text));

        message.Text += text;

        var lastPart = message.Parts.Count > 0 ? message.Parts[^1] : null;
        if (lastPart?.Type == ContentPartType.Text)
            lastPart.Text += text;
        else
            message.Parts.Add(new ContentPart
            {
                Type = ContentPartType.Text,
                Text = text
            });

        notifyChanged();
    }

    public void AppendThinking(ChatMessage message, string text)
    {
        var lastPart = message.Parts.Count > 0 ? message.Parts[^1] : null;
        if (lastPart?.Type == ContentPartType.Thinking)
            lastPart.Text += text;
        else
            message.Parts.Add(new ContentPart
            {
                Type = ContentPartType.Thinking,
                Text = text
            });

        notifyChanged();
    }

    public void FinishMessage(ChatMessage message)
    {
        message.IsStreaming = false;
        message.StreamingFinishedAt = DateTime.UtcNow;

        // Sync Text from Parts (canonical source) to ensure consistency
        message.Text = string.Concat(
            message.Parts
                .Where(p => p.Type == ContentPartType.Text)
                .Select(p => p.Text));

        notifyChanged();
    }
}