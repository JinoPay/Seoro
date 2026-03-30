using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class SessionStreamingState
{
    public bool IsStreaming { get; set; }
    public StreamingPhase Phase { get; set; }
    public string? ActiveToolName { get; set; }
    public bool HasCompleted { get; set; }
}

public class ChatState : IChatState
{
    // Sub-managers (composition)
    public MessageManager Messages { get; }
    public StreamingStateManager Streaming { get; }
    public SettingsStateManager Settings { get; }
    public TabManager Tabs { get; } = new();

    // Mediator: typed event bus
    private readonly IChatEventBus _eventBus;

    // Navigation state
    public Workspace? CurrentWorkspace { get; private set; }
    public Session? CurrentSession { get; private set; }
    private volatile string? _pendingMessage;
    public string? PendingMessage => _pendingMessage;
    public RightPanelMode RightPanel { get; private set; }

    // Debounce — single persistent timer, enabled/disabled via Change()
    private readonly Timer _debounceTimer;
    private volatile bool _pendingNotification;
    private volatile bool _timerActive;
    private const int DebounceMs = 50;

    public event Action? OnChange;
    public event Action? OnRequestCreateWorkspace;

    public ChatState(IActiveSessionRegistry activeSessionRegistry, IChatEventBus eventBus)
    {
        _eventBus = eventBus;
        _debounceTimer = new Timer(_ =>
        {
            if (_pendingNotification)
            {
                _pendingNotification = false;
                OnChange?.Invoke();
            }
        }, null, Timeout.Infinite, Timeout.Infinite); // starts disabled

        Messages = new MessageManager(NotifyStateChanged);
        Streaming = new StreamingStateManager(NotifyStateChanged);
        Streaming.BindRegistry(activeSessionRegistry);
        Settings = new SettingsStateManager(NotifyStateChanged);
        Tabs.OnTabChanged += NotifyStateChanged;
    }

    // --- Current session streaming shortcuts (backward compatible) ---

    public bool IsStreaming => CurrentSession != null && Streaming.IsSessionStreaming(CurrentSession.Id);
    public StreamingPhase Phase => CurrentSession != null ? Streaming.GetSessionPhase(CurrentSession.Id) : StreamingPhase.None;
    public string? ActiveToolName => CurrentSession != null ? Streaming.GetSessionToolName(CurrentSession.Id) : null;

    // --- Streaming delegation (backward compatible) ---
    // Streaming methods delegate to StreamingStateManager which calls _notifyChanged
    // (the debounced path). No separate typed events — streaming is high-frequency.

    public bool IsSessionStreaming(string sessionId) => Streaming.IsSessionStreaming(sessionId);
    public StreamingPhase GetSessionPhase(string sessionId) => Streaming.GetSessionPhase(sessionId);
    public string? GetSessionToolName(string sessionId) => Streaming.GetSessionToolName(sessionId);
    public bool HasAnyStreaming() => Streaming.HasAnyStreaming();
    public IReadOnlyList<string> GetStreamingSessionIds() => Streaming.GetStreamingSessionIds();
    public void RegisterActiveSession(Session session) => Streaming.RegisterActiveSession(session);
    public void UnregisterActiveSession(string sessionId) => Streaming.UnregisterActiveSession(sessionId);
    public Session? GetActiveSession(string sessionId) => Streaming.GetActiveSession(sessionId);

    public bool IsSessionCompleted(string sessionId) => Streaming.IsSessionCompleted(sessionId);
    public void ClearSessionCompleted(string sessionId) => Streaming.ClearCompleted(sessionId);

    public void SetStreaming(bool streaming, string? sessionId = null)
    {
        var resolvedId = sessionId ?? CurrentSession?.Id;
        Streaming.SetStreaming(streaming, resolvedId);

        // 현재 보고 있는 세션이면 completed 표시 불필요
        if (!streaming && resolvedId != null && resolvedId == CurrentSession?.Id)
            Streaming.ClearCompleted(resolvedId);
    }

    public void SetPhase(StreamingPhase phase, string? toolName = null, string? sessionId = null)
        => Streaming.SetPhase(phase, toolName, sessionId ?? CurrentSession?.Id);

    // --- Message delegation (backward compatible) ---

    public void AddUserMessage(string text)
    {
        if (CurrentSession != null) Messages.AddUserMessage(CurrentSession, text);
    }

    public void AddUserMessage(Session session, string text)
        => Messages.AddUserMessage(session, text);

    public void AddUserMessage(string text, List<FileAttachment> attachments)
    {
        if (CurrentSession != null) Messages.AddUserMessage(CurrentSession, text, attachments);
    }

    public void AddUserMessage(Session session, string text, List<FileAttachment> attachments)
        => Messages.AddUserMessage(session, text, attachments);

    public ChatMessage StartAssistantMessage()
        => Messages.StartAssistantMessage(CurrentSession!);

    public ChatMessage StartAssistantMessage(Session session)
        => Messages.StartAssistantMessage(session);

    public void AppendText(ChatMessage message, string text)
        => Messages.AppendText(message, text);

    public void AppendThinking(ChatMessage message, string text)
        => Messages.AppendThinking(message, text);

    public void AddToolCall(ChatMessage message, ToolCall toolCall)
        => Messages.AddToolCall(message, toolCall);

    public void FinishMessage(ChatMessage message)
        => Messages.FinishMessage(message);

    public void AddSystemMessage(string text)
    {
        if (CurrentSession != null) Messages.AddSystemMessage(CurrentSession, text);
    }

    public void AddSystemMessage(Session session, string text)
        => Messages.AddSystemMessage(session, text);

    // --- Settings delegation (backward compatible) ---

    public bool ShowSettings => Settings.ShowSettings;
    public string SettingsSection => Settings.SettingsSection;
    public string? SettingsWorkspaceId => Settings.SettingsWorkspaceId;

    public void OpenSettings(string section = "dashboard", string? workspaceId = null)
    {
        ShowNotifications = false;
        Settings.OpenSettings(section, workspaceId);
    }

    public void CloseSettings()
        => Settings.CloseSettings();

    // --- Overlay state (Notifications) ---

    public bool ShowNotifications { get; private set; }

    public void OpenNotifications()
    {
        Settings.CloseSettings();
        ShowNotifications = true;
        NotifyStateChanged();
    }

    public void CloseNotifications()
    {
        ShowNotifications = false;
        NotifyStateChanged();
    }

    public void SetSettingsSection(string section)
        => Settings.SetSettingsSection(section);

    public void SetSettingsWorkspace(string? workspaceId)
        => Settings.SetSettingsWorkspace(workspaceId);

    // --- Navigation & UI state (stays in ChatState) ---

    public void SetWorkspace(Workspace workspace)
    {
        CurrentWorkspace = workspace;
        CurrentSession = null;
        _eventBus.Publish(new WorkspaceChangedEvent(workspace));
        NotifyStateChanged();
    }

    public void SetSession(Session? session)
    {
        var old = CurrentSession;
        CurrentSession = session;
        Tabs.Reset(session?.Title);

        if (session != null)
            Streaming.ClearCompleted(session.Id);

        _eventBus.Publish(new SessionChangedEvent(old, session));
        NotifyStateChanged();
    }

    public void RequestCreateWorkspace()
    {
        OnRequestCreateWorkspace?.Invoke();
    }

    public void SetPendingMessage(string? message)
    {
        _pendingMessage = message;
        NotifyStateChanged();
    }

    /// <summary>
    /// Atomically reads and clears the pending message.
    /// Thread-safe: concurrent calls will never return the same message twice.
    /// </summary>
    public string? ConsumePendingMessage()
    {
        return Interlocked.Exchange(ref _pendingMessage, null);
    }

    /// <summary>
    /// Peeks at the pending message without consuming it.
    /// </summary>
    public string? PeekPendingMessage() => _pendingMessage;

    public void SetRightPanel(RightPanelMode mode)
    {
        RightPanel = mode;
        _eventBus.Publish(new RightPanelChangedEvent(mode));
        NotifyStateChanged();
    }

    public void ToggleRightPanel(RightPanelMode mode)
    {
        RightPanel = RightPanel == mode ? RightPanelMode.None : mode;
        _eventBus.Publish(new RightPanelChangedEvent(RightPanel));
        NotifyStateChanged();
    }

    // --- Debounced notification ---

    public void NotifyStateChanged()
    {
        if (Streaming.HasAnyStreaming())
        {
            _pendingNotification = true;
            if (!_timerActive)
            {
                _timerActive = true;
                _debounceTimer.Change(DebounceMs, DebounceMs);
            }
        }
        else
        {
            // Streaming stopped — disable timer and fire immediately
            if (_timerActive)
            {
                _timerActive = false;
                _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            _pendingNotification = false;
            OnChange?.Invoke();
        }
    }

    public void Dispose()
    {
        _debounceTimer.Dispose();
    }
}
