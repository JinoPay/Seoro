
namespace Seoro.Shared.Services.Chat;

public interface IChatState : IDisposable
{
    // Current session streaming shortcuts
    bool IsStreaming { get; }

    // Overlay state (Notifications)
    bool ShowNotifications { get; }

    // Settings delegation
    bool ShowSettings { get; }

    // Sub-managers
    MessageManager Messages { get; }
    RightPanelMode RightPanel { get; }
    Session? CurrentSession { get; }
    SettingsStateManager Settings { get; }
    StreamingPhase Phase { get; }
    StreamingStateManager Streaming { get; }
    string SettingsSection { get; }
    string? ActiveToolName { get; }
    string? PendingMessage { get; }
    string? SettingsWorkspaceId { get; }
    TabManager Tabs { get; }

    // Navigation state
    Workspace? CurrentWorkspace { get; }

    // Events
    event Action? OnChange;
    event Action? OnRequestCreateWorkspace;
    event Action? OnRequestShowOnboarding;
    event Action? OnRequestShowWhatsNew;
    bool HasAnyStreaming();
    bool IsSessionCompleted(string sessionId);

    // Streaming delegation
    bool IsSessionStreaming(string sessionId);
    ChatMessage StartAssistantMessage();
    ChatMessage StartAssistantMessage(Session session);
    IReadOnlyList<string> GetStreamingSessionIds();
    Session? GetActiveSession(string sessionId);
    StreamingPhase GetSessionPhase(string sessionId);
    string GetInputDraft(string sessionId);
    List<PendingAttachment> GetAttachmentDraft(string sessionId);
    string? ConsumePendingMessage();
    string? GetSessionToolName(string sessionId);
    string? PeekPendingMessage();
    void AddSystemMessage(string text);
    void AddSystemMessage(Session session, string text);
    void AddToolCall(ChatMessage message, ToolCall toolCall);

    // Message delegation
    void AddUserMessage(string text);
    void AddUserMessage(Session session, string text);
    void AddUserMessage(string text, List<FileAttachment> attachments);
    void AddUserMessage(Session session, string text, List<FileAttachment> attachments);
    void AppendText(ChatMessage message, string text);
    void AppendThinking(ChatMessage message, string text);
    void ClearSessionCompleted(string sessionId);
    void CloseNotifications();
    void CloseSettings();
    void FinishMessage(ChatMessage message);

    // Notification
    void NotifyStateChanged();
    void OpenNotifications();
    void OpenSettings(string section = "general", string? workspaceId = null);
    void RegisterActiveSession(Session session);
    void RequestCreateWorkspace();

    void RequestShowOnboarding();
    void RequestShowWhatsNew();

    // Input draft (per-session temporary storage)
    void SetInputDraft(string sessionId, string text);
    void SetAttachmentDraft(string sessionId, List<PendingAttachment> attachments);
    void SetPendingMessage(string? message);
    void SetPhase(StreamingPhase phase, string? toolName = null, string? sessionId = null);
    void SetRightPanel(RightPanelMode mode);
    void SetSession(Session? session);
    void SetSettingsSection(string section);
    void SetSettingsWorkspace(string? workspaceId);
    void SetStreaming(bool streaming, string? sessionId = null);

    // Navigation & UI state
    void SetWorkspace(Workspace workspace);
    void ToggleRightPanel(RightPanelMode mode);
    void UnregisterActiveSession(string sessionId);
}