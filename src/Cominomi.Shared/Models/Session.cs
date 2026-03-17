using System.Text.Json.Serialization;

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

public class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Chat";
    public string WorktreePath { get; set; } = string.Empty;
    public string BranchName { get; set; } = "";
    public string BaseBranch { get; set; } = "";
    public string Model { get; set; } = ModelDefinitions.Default.Id;
    public string WorkspaceId { get; set; } = "default";
    public string PermissionMode { get; set; } = "bypassAll";
    public string EffortLevel { get; set; } = "auto";
    public AgentType AgentType { get; set; } = AgentType.Code;
    public string CityName { get; set; } = "";
    public SessionStatus Status { get; set; } = SessionStatus.Initializing;
    public string? ErrorMessage { get; set; }
    public List<ChatMessage> Messages { get; set; } = [];
    public string? PrUrl { get; set; }
    public int? PrNumber { get; set; }
    public List<string>? ConflictFiles { get; set; }
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
