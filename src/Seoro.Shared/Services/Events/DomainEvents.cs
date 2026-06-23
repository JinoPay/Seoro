
namespace Seoro.Shared.Services.Events;

// ── Typed events replacing the monolithic OnChange ──

public abstract record DomainEvent;

public sealed record SessionChangedEvent(Session? OldSession, Session? NewSession) : DomainEvent;

public sealed record WorkspaceChangedEvent(Workspace Workspace) : DomainEvent;

public sealed record StreamingStartedEvent(string SessionId) : DomainEvent;

public sealed record StreamingStoppedEvent(string SessionId) : DomainEvent;

public sealed record StreamingPhaseChangedEvent(string SessionId, StreamingPhase Phase, string? ToolName = null)
    : DomainEvent;

public sealed record MessageAddedEvent(string SessionId, MessageRole Role) : DomainEvent;

public sealed record RightPanelChangedEvent(RightPanelMode Mode) : DomainEvent;

public sealed record TabChangedEvent : DomainEvent;

public sealed record SettingsChangedEvent : DomainEvent;

public sealed record TogglePlanModeEvent : DomainEvent;

public sealed record ToggleEffortLevelEvent : DomainEvent;

public sealed record ToggleSyncEvent : DomainEvent;

public sealed record ApprovePlanEvent : DomainEvent;

public sealed record WorktreeSyncStartedEvent(string SessionId, string WorkspaceId) : DomainEvent;

public sealed record WorktreeSyncStoppedEvent(string SessionId, string WorkspaceId) : DomainEvent;

public sealed record MergeRequestedEvent(ChatInputMessage Input) : DomainEvent;

public sealed record WindowCloseRequestedEvent : DomainEvent;

public sealed record BranchChangedEvent(string SessionId, string BranchName) : DomainEvent;

public sealed record SessionTitleChangedEvent(string SessionId, string Title) : DomainEvent;

/// <summary>
///     워크트리의 머지 충돌 진입/해제 이벤트. <see cref="ConflictWatcherService"/>가 발행.
///     <see cref="WorkingDir"/> 는 충돌이 감지된 경로(세션 워크트리 또는 향후 Alt B 의 임시 클론).
///     <see cref="Entered"/> 가 true 면 충돌 시작, false 면 해제 (.git/MERGE_HEAD 삭제 감지).
/// </summary>
public sealed record ConflictDetectedEvent(string WorkingDir, bool Entered) : DomainEvent;