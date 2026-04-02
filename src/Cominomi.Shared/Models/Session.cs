using System.Text.Json.Serialization;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionStatus
{
    Initializing,
    Pending,
    Ready,
    Archived,
    Error
}

[JsonConverter(typeof(SessionJsonConverter))]
public class Session
{
    private long _totalInputTokens;
    private long _totalOutputTokens;
    public AgentType AgentType { get; init; } = AgentType.Code;

    public AppError? Error { get; set; }
    public bool PlanCompleted { get; set; }
    public bool TitleLocked { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public decimal? MaxBudgetUsd { get; init; }

    // Git 관심사 (worktree, branch)
    public GitContext Git { get; init; } = new();
    public int? MaxTurns { get; init; }
    public List<ChatMessage> Messages { get; set; } = [];

    public long TotalInputTokens
    {
        get => _totalInputTokens;
        set => _totalInputTokens = Guard.NonNegative(value, nameof(TotalInputTokens));
    }

    public long TotalOutputTokens
    {
        get => _totalOutputTokens;
        set => _totalOutputTokens = Guard.NonNegative(value, nameof(TotalOutputTokens));
    }

    [JsonIgnore] public object MessagesLock { get; } = new();

    public SessionStatus Status { get; private set; } = SessionStatus.Initializing;
    public string CityName { get; init; } = "";
    public string EffortLevel { get; set; } = CominomiConstants.DefaultEffortLevel;
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Model { get; set; } = ModelDefinitions.Default.Id;
    public string PermissionMode { get; set; } = CominomiConstants.DefaultPermissionMode;
    public string Title { get; set; } = "New Chat";
    public string WorkspaceId { get; init; } = "default";

    public string? ConversationId { get; set; }

    [JsonIgnore] public string? ErrorMessage => Error?.Message;

    /// <summary>
    ///     Raw JSON input from a pending AskUserQuestion tool call.
    ///     Persisted so the bottom bar survives session switches.
    ///     Cleared when the user responds.
    /// </summary>
    public string? PendingAskUserQuestionInput { get; set; }

    public string? PlanFilePath { get; set; }

    [JsonIgnore] public string? ResolvedModel { get; set; }

    public List<ChatMessage> GetMessagesSnapshot()
    {
        lock (MessagesLock)
        {
            return Messages.ToList();
        }
    }

    /// <summary>
    ///     Initializes Status for deserialization or test setup. Bypasses validation.
    /// </summary>
    public void SetInitialStatus(SessionStatus status)
    {
        Status = status;
    }

    /// <summary>
    ///     Validates and applies a status transition. Throws on invalid transitions.
    /// </summary>
    public void TransitionStatus(SessionStatus target)
    {
        if (Status == target)
            return;

        if (!SessionStatusMachine.IsValidTransition(Status, target))
            throw new InvalidOperationException(
                $"Invalid session status transition: {Status} → {target} (session {Id})");

        Status = target;
    }
}