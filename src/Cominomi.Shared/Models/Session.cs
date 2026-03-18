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
    Pushed,
    PrOpen,
    ConflictDetected,
    Merged,
    Archived,
    Error
}

[JsonConverter(typeof(SessionJsonConverter))]
public class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Chat";
    public string Model { get; set; } = ModelDefinitions.Default.Id;
    public string WorkspaceId { get; set; } = "default";
    public string PermissionMode { get; set; } = CominomiConstants.DefaultPermissionMode;
    public string EffortLevel { get; set; } = CominomiConstants.DefaultEffortLevel;
    public AgentType AgentType { get; set; } = AgentType.Code;
    public string CityName { get; set; } = "";
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
    public string? ErrorMessage { get; set; }
    public List<ChatMessage> Messages { get; set; } = [];

    // Git 관심사 (worktree, branch)
    public GitContext Git { get; set; } = new();

    // PR/이슈 관심사
    public PrContext Pr { get; set; } = new();

    public string? ConversationId { get; set; }
    public int? MaxTurns { get; set; }
    public decimal? MaxBudgetUsd { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public bool PlanCompleted { get; set; }
    public string? PlanFilePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string? ResolvedModel { get; set; }
}
