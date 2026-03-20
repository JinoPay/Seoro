using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

// ── Typed events replacing the monolithic OnChange ──

public abstract record ChatEvent;

public sealed record SessionChangedEvent(Session? OldSession, Session? NewSession) : ChatEvent;
public sealed record WorkspaceChangedEvent(Workspace Workspace) : ChatEvent;

public sealed record StreamingStartedEvent(string SessionId) : ChatEvent;
public sealed record StreamingStoppedEvent(string SessionId) : ChatEvent;
public sealed record StreamingPhaseChangedEvent(string SessionId, StreamingPhase Phase, string? ToolName = null) : ChatEvent;

public sealed record MessageAddedEvent(string SessionId, MessageRole Role) : ChatEvent;

public sealed record RightPanelChangedEvent(RightPanelMode Mode) : ChatEvent;
public sealed record TabChangedEvent : ChatEvent;
public sealed record SettingsChangedEvent : ChatEvent;

public sealed record SessionSyncCompletedEvent(string SessionId, SessionSyncResult Result) : ChatEvent;
