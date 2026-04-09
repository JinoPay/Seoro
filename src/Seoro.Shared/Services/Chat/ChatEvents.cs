
namespace Seoro.Shared.Services.Chat;

// ── Typed events replacing the monolithic OnChange ──

public abstract record ChatEvent;

public sealed record SessionChangedEvent(Session? OldSession, Session? NewSession) : ChatEvent;

public sealed record WorkspaceChangedEvent(Workspace Workspace) : ChatEvent;

public sealed record StreamingStartedEvent(string SessionId) : ChatEvent;

public sealed record StreamingStoppedEvent(string SessionId) : ChatEvent;

public sealed record StreamingPhaseChangedEvent(string SessionId, StreamingPhase Phase, string? ToolName = null)
    : ChatEvent;

public sealed record MessageAddedEvent(string SessionId, MessageRole Role) : ChatEvent;

public sealed record RightPanelChangedEvent(RightPanelMode Mode) : ChatEvent;

public sealed record TabChangedEvent : ChatEvent;

public sealed record SettingsChangedEvent : ChatEvent;

public sealed record TogglePlanModeEvent : ChatEvent;

public sealed record ToggleEffortLevelEvent : ChatEvent;

public sealed record ToggleSyncEvent : ChatEvent;

public sealed record WorktreeSyncStartedEvent(string SessionId, string WorkspaceId) : ChatEvent;

public sealed record WorktreeSyncStoppedEvent(string SessionId, string WorkspaceId) : ChatEvent;

public sealed record MergeRequestedEvent(ChatInputMessage Input) : ChatEvent;

public sealed record WindowCloseRequestedEvent : ChatEvent;

public sealed record BranchChangedEvent(string SessionId, string BranchName) : ChatEvent;

public sealed record SessionTitleChangedEvent(string SessionId, string Title) : ChatEvent;