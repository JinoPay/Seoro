using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IChatState : IDisposable
{
    // Sub-managers
    MessageManager Messages { get; }
    StreamingStateManager Streaming { get; }
    SettingsStateManager Settings { get; }
    TabManager Tabs { get; }

    // Navigation state
    Workspace? CurrentWorkspace { get; }
    Session? CurrentSession { get; }
    string? PendingMessage { get; }
    RightPanelMode RightPanel { get; }

    // Current session streaming shortcuts
    bool IsStreaming { get; }
    StreamingPhase Phase { get; }
    string? ActiveToolName { get; }

    // Streaming delegation
    bool IsSessionStreaming(string sessionId);
    StreamingPhase GetSessionPhase(string sessionId);
    string? GetSessionToolName(string sessionId);
    bool HasAnyStreaming();
    IReadOnlyList<string> GetStreamingSessionIds();
    void RegisterActiveSession(Session session);
    void UnregisterActiveSession(string sessionId);
    Session? GetActiveSession(string sessionId);
    void SetStreaming(bool streaming, string? sessionId = null);
    void SetPhase(StreamingPhase phase, string? toolName = null, string? sessionId = null);

    // Message delegation
    void AddUserMessage(string text);
    void AddUserMessage(Session session, string text);
    void AddUserMessage(string text, List<FileAttachment> attachments);
    void AddUserMessage(Session session, string text, List<FileAttachment> attachments);
    ChatMessage StartAssistantMessage();
    ChatMessage StartAssistantMessage(Session session);
    void AppendText(ChatMessage message, string text);
    void AppendThinking(ChatMessage message, string text);
    void AddToolCall(ChatMessage message, ToolCall toolCall);
    void FinishMessage(ChatMessage message);
    void AddSystemMessage(string text);
    void AddSystemMessage(Session session, string text);

    // Settings delegation
    bool ShowSettings { get; }
    string SettingsSection { get; }
    string? SettingsWorkspaceId { get; }
    void OpenSettings(string section = "general", string? workspaceId = null);
    void CloseSettings();
    void SetSettingsSection(string section);
    void SetSettingsWorkspace(string? workspaceId);

    // Overlay state (Activity / Notifications)
    bool ShowActivity { get; }
    bool ShowNotifications { get; }
    void OpenActivity();
    void CloseActivity();
    void OpenNotifications();
    void CloseNotifications();

    // Navigation & UI state
    void SetWorkspace(Workspace workspace);
    void SetSession(Session? session);
    void RequestCreateWorkspace();
    void SetPendingMessage(string? message);
    string? ConsumePendingMessage();
    string? PeekPendingMessage();
    void SetRightPanel(RightPanelMode mode);
    void ToggleRightPanel(RightPanelMode mode);

    // Notification
    void NotifyStateChanged();

    // Events
    event Action? OnChange;
    event Action? OnRequestCreateWorkspace;
}
