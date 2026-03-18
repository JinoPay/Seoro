using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceStatus
{
    Initializing,
    Ready,
    Error
}

public class Workspace
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string DefaultModel { get; set; } = ModelDefinitions.Default.Id;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Git fields
    public WorkspaceStatus Status { get; set; } = WorkspaceStatus.Initializing;
    public string RepoUrl { get; set; } = "";
    public string RepoLocalPath { get; set; } = "";
    public AppError? Error { get; set; }

    [JsonIgnore]
    public string? ErrorMessage => Error?.Message;
    public string? SystemPrompt { get; set; }

    // Git defaults
    public string? DefaultBaseBranch { get; set; }
    public string DefaultRemote { get; set; } = "origin";

    // Scripts
    public string? SetupScript { get; set; }
    public string? RunScript { get; set; }
    public string? ArchiveScript { get; set; }

    // Preferences
    public string? CodeReviewPreferences { get; set; }
    public string? CreatePrPreferences { get; set; }
    public string? BranchRenamePreferences { get; set; }
    public string? GeneralPreferences { get; set; }
}
