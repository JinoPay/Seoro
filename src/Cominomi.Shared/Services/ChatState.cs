using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class SessionStreamingState
{
    public bool HasCompleted { get; set; }
    public bool IsStreaming { get; set; }
    public StreamingPhase Phase { get; set; }
    public string? ActiveToolName { get; set; }
}

public class ChatState : IChatState
{
    private const int DebounceMs = 50;

    // Input draft storage (per-session, memory only)
    private readonly Dictionary<string, string> _inputDrafts = new();
    private readonly Dictionary<string, List<PendingAttachment>> _attachmentDrafts = new();

    // Mediator: typed event bus
    private readonly IChatEventBus _eventBus;

    // Debounce — single persistent timer, enabled/disabled via Change()
    private readonly Timer _debounceTimer;
    private volatile bool _pendingNotification;
    private volatile bool _timerActive;
    private volatile string? _pendingMessage;

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

    public event Action? OnChange;
    public event Action? OnRequestCreateWorkspace;
    public event Action? OnRequestShowOnboarding;
    public event Action? OnRequestShowWhatsNew;

    public bool HasAnyStreaming()
    {
        return Streaming.HasAnyStreaming();
    }

    public bool IsSessionCompleted(string sessionId)
    {
        return Streaming.IsSessionCompleted(sessionId);
    }

    // --- Streaming delegation (backward compatible) ---
    // Streaming methods delegate to StreamingStateManager which calls _notifyChanged
    // (the debounced path). No separate typed events — streaming is high-frequency.

    public bool IsSessionStreaming(string sessionId)
    {
        return Streaming.IsSessionStreaming(sessionId);
    }

    // --- Current session streaming shortcuts (backward compatible) ---

    public bool IsStreaming => CurrentSession != null && Streaming.IsSessionStreaming(CurrentSession.Id);

    // --- Overlay state (Notifications) ---

    public bool ShowNotifications { get; private set; }

    // --- Settings delegation (backward compatible) ---

    public bool ShowSettings => Settings.ShowSettings;

    public ChatMessage StartAssistantMessage()
    {
        return Messages.StartAssistantMessage(CurrentSession!);
    }

    public ChatMessage StartAssistantMessage(Session session)
    {
        return Messages.StartAssistantMessage(session);
    }

    public IReadOnlyList<string> GetStreamingSessionIds()
    {
        return Streaming.GetStreamingSessionIds();
    }

    // Sub-managers (composition)
    public MessageManager Messages { get; }
    public RightPanelMode RightPanel { get; private set; }
    public Session? CurrentSession { get; private set; }

    public Session? GetActiveSession(string sessionId)
    {
        return Streaming.GetActiveSession(sessionId);
    }

    public SettingsStateManager Settings { get; }

    public StreamingPhase GetSessionPhase(string sessionId)
    {
        return Streaming.GetSessionPhase(sessionId);
    }

    public StreamingPhase Phase =>
        CurrentSession != null ? Streaming.GetSessionPhase(CurrentSession.Id) : StreamingPhase.None;

    public StreamingStateManager Streaming { get; }

    public string GetInputDraft(string sessionId)
    {
        return _inputDrafts.TryGetValue(sessionId, out var draft) ? draft : string.Empty;
    }

    public List<PendingAttachment> GetAttachmentDraft(string sessionId)
    {
        return _attachmentDrafts.TryGetValue(sessionId, out var draft) ? draft : [];
    }

    public string SettingsSection => Settings.SettingsSection;
    public string? ActiveToolName => CurrentSession != null ? Streaming.GetSessionToolName(CurrentSession.Id) : null;

    /// <summary>
    ///     Atomically reads and clears the pending message.
    ///     Thread-safe: concurrent calls will never return the same message twice.
    /// </summary>
    public string? ConsumePendingMessage()
    {
        return Interlocked.Exchange(ref _pendingMessage, null);
    }

    public string? GetSessionToolName(string sessionId)
    {
        return Streaming.GetSessionToolName(sessionId);
    }

    /// <summary>
    ///     Peeks at the pending message without consuming it.
    /// </summary>
    public string? PeekPendingMessage()
    {
        return _pendingMessage;
    }

    public string? PendingMessage => _pendingMessage;
    public string? SettingsWorkspaceId => Settings.SettingsWorkspaceId;
    public TabManager Tabs { get; } = new();

    public void AddSystemMessage(string text)
    {
        if (CurrentSession != null) Messages.AddSystemMessage(CurrentSession, text);
    }

    public void AddSystemMessage(Session session, string text)
    {
        Messages.AddSystemMessage(session, text);
    }

    public void AddToolCall(ChatMessage message, ToolCall toolCall)
    {
        Messages.AddToolCall(message, toolCall);
    }

    // --- Message delegation (backward compatible) ---

    public void AddUserMessage(string text)
    {
        if (CurrentSession != null) Messages.AddUserMessage(CurrentSession, text);
    }

    public void AddUserMessage(Session session, string text)
    {
        Messages.AddUserMessage(session, text);
    }

    public void AddUserMessage(string text, List<FileAttachment> attachments)
    {
        if (CurrentSession != null) Messages.AddUserMessage(CurrentSession, text, attachments);
    }

    public void AddUserMessage(Session session, string text, List<FileAttachment> attachments)
    {
        Messages.AddUserMessage(session, text, attachments);
    }

    public void AppendText(ChatMessage message, string text)
    {
        Messages.AppendText(message, text);
    }

    public void AppendThinking(ChatMessage message, string text)
    {
        Messages.AppendThinking(message, text);
    }

    public void ClearSessionCompleted(string sessionId)
    {
        Streaming.ClearCompleted(sessionId);
    }

    public void CloseNotifications()
    {
        ShowNotifications = false;
        NotifyStateChanged();
    }

    public void CloseSettings()
    {
        Settings.CloseSettings();
    }

    public void FinishMessage(ChatMessage message)
    {
        Messages.FinishMessage(message);
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

    public void OpenNotifications()
    {
        Settings.CloseSettings();
        ShowNotifications = true;
        NotifyStateChanged();
    }

    public void OpenSettings(string section = "general", string? workspaceId = null)
    {
        ShowNotifications = false;
        Settings.OpenSettings(section, workspaceId);
    }

    public void RegisterActiveSession(Session session)
    {
        Streaming.RegisterActiveSession(session);
    }

    public void RequestCreateWorkspace()
    {
        OnRequestCreateWorkspace?.Invoke();
    }

    public void RequestShowOnboarding()
    {
        OnRequestShowOnboarding?.Invoke();
    }

    public void RequestShowWhatsNew()
    {
        OnRequestShowWhatsNew?.Invoke();
    }

    // --- Input draft (per-session) ---

    public void SetInputDraft(string sessionId, string text)
    {
        if (string.IsNullOrEmpty(text))
            _inputDrafts.Remove(sessionId);
        else
            _inputDrafts[sessionId] = text;
    }

    public void SetAttachmentDraft(string sessionId, List<PendingAttachment> attachments)
    {
        if (attachments.Count == 0)
            _attachmentDrafts.Remove(sessionId);
        else
            _attachmentDrafts[sessionId] = [..attachments];
    }

    public void SetPendingMessage(string? message)
    {
        _pendingMessage = message;
        NotifyStateChanged();
    }

    public void SetPhase(StreamingPhase phase, string? toolName = null, string? sessionId = null)
    {
        Streaming.SetPhase(phase, toolName, sessionId ?? CurrentSession?.Id);
    }

    public void SetRightPanel(RightPanelMode mode)
    {
        RightPanel = mode;
        _eventBus.Publish(new RightPanelChangedEvent(mode));
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

    public void SetSettingsSection(string section)
    {
        Settings.SetSettingsSection(section);
    }

    public void SetSettingsWorkspace(string? workspaceId)
    {
        Settings.SetSettingsWorkspace(workspaceId);
    }

    public void SetStreaming(bool streaming, string? sessionId = null)
    {
        var resolvedId = sessionId ?? CurrentSession?.Id;
        Streaming.SetStreaming(streaming, resolvedId);

        // 현재 보고 있는 세션이면 completed 표시 불필요
        if (!streaming && resolvedId != null && resolvedId == CurrentSession?.Id)
            Streaming.ClearCompleted(resolvedId);
    }

    // --- Navigation & UI state (stays in ChatState) ---

    public void SetWorkspace(Workspace workspace)
    {
        CurrentWorkspace = workspace;
        CurrentSession = null;
        _eventBus.Publish(new WorkspaceChangedEvent(workspace));
        NotifyStateChanged();
    }

    public void ToggleRightPanel(RightPanelMode mode)
    {
        RightPanel = RightPanel == mode ? RightPanelMode.None : mode;
        _eventBus.Publish(new RightPanelChangedEvent(RightPanel));
        NotifyStateChanged();
    }

    public void UnregisterActiveSession(string sessionId)
    {
        Streaming.UnregisterActiveSession(sessionId);
    }

    // Navigation state
    public Workspace? CurrentWorkspace { get; private set; }

    public void Dispose()
    {
        _debounceTimer.Dispose();
    }
}