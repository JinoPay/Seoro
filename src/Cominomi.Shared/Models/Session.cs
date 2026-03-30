using System.Text.Json.Serialization;
using Cominomi.Shared;
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
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Chat";
    public string Model { get; set; } = ModelDefinitions.Default.Id;
    public string WorkspaceId { get; init; } = "default";
    public string PermissionMode { get; set; } = CominomiConstants.DefaultPermissionMode;
    public string EffortLevel { get; set; } = CominomiConstants.DefaultEffortLevel;
    public AgentType AgentType { get; init; } = AgentType.Code;
    public string CityName { get; init; } = "";
    public SessionStatus Status { get; private set; } = SessionStatus.Initializing;

    /// <summary>
    /// Validates and applies a status transition. Throws on invalid transitions.
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

    /// <summary>
    /// Initializes Status for deserialization or test setup. Bypasses validation.
    /// </summary>
    public void SetInitialStatus(SessionStatus status) => Status = status;
    public AppError? Error { get; set; }

    [JsonIgnore]
    public string? ErrorMessage => Error?.Message;
    public List<ChatMessage> Messages { get; set; } = [];

    // Git 관심사 (worktree, branch)
    public GitContext Git { get; init; } = new();

    public string? ConversationId { get; set; }
    public int? MaxTurns { get; init; }
    public decimal? MaxBudgetUsd { get; init; }
    private long _totalInputTokens;
    private long _totalOutputTokens;

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
    public bool TitleLocked { get; set; }
    public bool PlanCompleted { get; set; }
    public string? PlanFilePath { get; set; }

    /// <summary>
    /// Raw JSON input from a pending AskUserQuestion tool call.
    /// Persisted so the bottom bar survives session switches.
    /// Cleared when the user responds.
    /// </summary>
    public string? PendingAskUserQuestionInput { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string? ResolvedModel { get; set; }
}
