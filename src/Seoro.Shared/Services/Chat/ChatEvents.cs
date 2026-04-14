
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

/// <summary>
///     워크트리의 머지 충돌 진입/해제 이벤트. <see cref="ConflictWatcherService"/>가 발행.
///     <see cref="WorkingDir"/> 는 충돌이 감지된 경로(세션 워크트리 또는 향후 Alt B 의 임시 클론).
///     <see cref="Entered"/> 가 true 면 충돌 시작, false 면 해제 (.git/MERGE_HEAD 삭제 감지).
/// </summary>
public sealed record ConflictDetectedEvent(string WorkingDir, bool Entered) : ChatEvent;

/// <summary>
///     슬림 MergeToolbar 클릭 또는 ConflictDetectedEvent 발생 시
///     EmbeddedDiffPanel 의 Merge 탭으로 전환 요청.
/// </summary>
public sealed record MergeTabRequestedEvent : ChatEvent;

/// <summary>
///     MergePanel 의 diff stats 클릭 시 EmbeddedDiffPanel 의 Changes 탭으로 전환 요청.
/// </summary>
public sealed record ChangesTabRequestedEvent : ChatEvent;