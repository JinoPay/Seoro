using System.Collections.Concurrent;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

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

    public TabManager Tabs { get; } = new();

    private readonly ConcurrentDictionary<string, SessionStreamingState> _streamingStates = new();
    private readonly ConcurrentDictionary<string, Session> _activeSessions = new();
    private Timer? _debounceTimer;
    private volatile bool _pendingNotification;
    private const int DebounceMs = 50;

    public event Action? OnChange;
    public event Action? OnRequestCreateWorkspace;

    public ChatState()
    {
        Tabs.OnTabChanged += NotifyStateChanged;
    }

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

    // Active session registry: holds live in-memory session objects during streaming
    public void RegisterActiveSession(Session session)
        => _activeSessions[session.Id] = session;

    public void UnregisterActiveSession(string sessionId)
        => _activeSessions.TryRemove(sessionId, out _);

    public Session? GetActiveSession(string sessionId)
        => _activeSessions.TryGetValue(sessionId, out var session) ? session : null;

    public void SetWorkspace(Workspace workspace)
    {
        CurrentWorkspace = workspace;
        CurrentSession = null;
        NotifyStateChanged();
    }

    public void SetSession(Session? session)
    {
        CurrentSession = session;
        Tabs.Reset(session?.Title);

        // 세션 선택 시 기본으로 파일 탐색기 열기
        if (session != null && session.Status != SessionStatus.Pending)
            RightPanel = RightPanelMode.Explorer;
        else
            RightPanel = RightPanelMode.None;

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

    public void AddUserMessage(Session session, string text)
    {
        session.Messages.Add(new ChatMessage
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

    public void AddUserMessage(Session session, string text, List<FileAttachment> attachments)
    {
        session.Messages.Add(new ChatMessage
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
            IsStreaming = true,
            StreamingStartedAt = DateTime.UtcNow
        };
        CurrentSession?.Messages.Add(msg);
        NotifyStateChanged();
        return msg;
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
        message.StreamingFinishedAt = DateTime.UtcNow;
        NotifyStateChanged();
    }

    public void SetSpotlightActive(bool active)
    {
        IsSpotlightActive = active;
        NotifyStateChanged();
    }

    public void RequestCreateWorkspace()
    {
        OnRequestCreateWorkspace?.Invoke();
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

    public void AddSystemMessage(string text)
    {
        CurrentSession?.Messages.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Text = text
        });
        NotifyStateChanged();
    }

    public void AddSystemMessage(Session session, string text)
    {
        session.Messages.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Text = text
        });
        NotifyStateChanged();
    }

    // Settings page state
    public bool ShowSettings { get; private set; }
    public string SettingsSection { get; private set; } = "general";
    public string? SettingsWorkspaceId { get; private set; }

    public void OpenSettings(string section = "general", string? workspaceId = null)
    {
        ShowSettings = true;
        SettingsSection = section;
        SettingsWorkspaceId = workspaceId;
        NotifyStateChanged();
    }

    public void CloseSettings()
    {
        ShowSettings = false;
        SettingsWorkspaceId = null;
        NotifyStateChanged();
    }

    public void SetSettingsSection(string section)
    {
        SettingsSection = section;
        NotifyStateChanged();
    }

    public void SetSettingsWorkspace(string? workspaceId)
    {
        SettingsWorkspaceId = workspaceId;
        SettingsSection = workspaceId != null ? "ws-general" : "general";
        NotifyStateChanged();
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
