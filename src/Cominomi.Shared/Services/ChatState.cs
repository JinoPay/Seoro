using System.Collections.Concurrent;
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

public enum RightPanelMode
{
    None,
    Diff,
    Context
}

public class SessionStreamingState
{
    public bool IsStreaming { get; set; }
    public StreamingPhase Phase { get; set; }
    public string? ActiveToolName { get; set; }
}

public class ChatState : IDisposable
{
    public Workspace? CurrentWorkspace { get; private set; }
    public Session? CurrentSession { get; private set; }
    public bool IsSpotlightActive { get; private set; }
    public string? PendingMessage { get; private set; }
    public RightPanelMode RightPanel { get; private set; }

    private readonly ConcurrentDictionary<string, SessionStreamingState> _streamingStates = new();
    private Timer? _debounceTimer;
    private volatile bool _pendingNotification;
    private const int DebounceMs = 50;

    public event Action? OnChange;

    // Current session streaming shortcuts (backward compatible)
    public bool IsStreaming => CurrentSession != null && IsSessionStreaming(CurrentSession.Id);
    public StreamingPhase Phase => CurrentSession != null ? GetSessionPhase(CurrentSession.Id) : StreamingPhase.None;
    public string? ActiveToolName => CurrentSession != null ? GetSessionToolName(CurrentSession.Id) : null;

    // Per-session streaming state
    public bool IsSessionStreaming(string sessionId)
        => _streamingStates.TryGetValue(sessionId, out var s) && s.IsStreaming;

    public StreamingPhase GetSessionPhase(string sessionId)
        => _streamingStates.TryGetValue(sessionId, out var s) ? s.Phase : StreamingPhase.None;

    public string? GetSessionToolName(string sessionId)
        => _streamingStates.TryGetValue(sessionId, out var s) ? s.ActiveToolName : null;

    public bool HasAnyStreaming()
        => _streamingStates.Values.Any(s => s.IsStreaming);

    public IReadOnlyList<string> GetStreamingSessionIds()
        => _streamingStates.Where(kv => kv.Value.IsStreaming).Select(kv => kv.Key).ToList();

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

    public void SetStreaming(bool streaming, string? sessionId = null)
    {
        var key = sessionId ?? CurrentSession?.Id;
        if (key == null) return;

        var state = _streamingStates.GetOrAdd(key, _ => new SessionStreamingState());
        state.IsStreaming = streaming;
        if (!streaming)
        {
            state.Phase = StreamingPhase.None;
            state.ActiveToolName = null;
        }
        NotifyStateChanged();
    }

    public void SetPhase(StreamingPhase phase, string? toolName = null, string? sessionId = null)
    {
        var key = sessionId ?? CurrentSession?.Id;
        if (key == null) return;

        var state = _streamingStates.GetOrAdd(key, _ => new SessionStreamingState());
        if (state.Phase == phase && state.ActiveToolName == toolName) return;
        state.Phase = phase;
        state.ActiveToolName = toolName;
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

        NotifyStateChanged();
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

        NotifyStateChanged();
    }

    public void AddToolCall(ChatMessage message, ToolCall toolCall)
    {
        message.ToolCalls.Add(toolCall);

        message.Parts.Add(new ContentPart
        {
            Type = ContentPartType.ToolCall,
            ToolCall = toolCall
        });

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

    public void SetPendingMessage(string? message)
    {
        PendingMessage = message;
        NotifyStateChanged();
    }

    public string? ConsumePendingMessage()
    {
        var msg = PendingMessage;
        PendingMessage = null;
        return msg;
    }

    public void SetRightPanel(RightPanelMode mode)
    {
        RightPanel = mode;
        NotifyStateChanged();
    }

    public void ToggleRightPanel(RightPanelMode mode)
    {
        RightPanel = RightPanel == mode ? RightPanelMode.None : mode;
        NotifyStateChanged();
    }

    public void NotifyStateChanged()
    {
        if (HasAnyStreaming())
        {
            // During streaming, debounce to reduce re-render flood
            _pendingNotification = true;
            _debounceTimer ??= new Timer(_ =>
            {
                if (_pendingNotification)
                {
                    _pendingNotification = false;
                    OnChange?.Invoke();
                }
            }, null, DebounceMs, DebounceMs);
        }
        else
        {
            // Not streaming: fire immediately, stop timer
            if (_debounceTimer != null)
            {
                _debounceTimer.Dispose();
                _debounceTimer = null;
            }
            _pendingNotification = false;
            OnChange?.Invoke();
        }
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }
}
