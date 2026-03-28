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
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string DefaultModel { get; set; } = "";
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

    // Structured preferences
    public WorkspacePreferences Preferences { get; set; } = new();

    // Legacy free-text preferences — kept for migration/backward compatibility.
    // New code should use Preferences.* instead.
    [Obsolete("Use Preferences.CodeReviewPrompt instead")]
    public string? CodeReviewPreferences { get; set; }
    [Obsolete("Use Preferences.GeneralPrompt instead")]
    public string? GeneralPreferences { get; set; }

    /// <summary>
    /// Migrates legacy free-text preference fields into the structured Preferences object.
    /// Called on load to ensure old workspaces transition smoothly.
    /// </summary>
    public void MigratePreferences()
    {
        Preferences ??= new WorkspacePreferences();

#pragma warning disable CS0618 // Obsolete
        if (!string.IsNullOrWhiteSpace(CodeReviewPreferences) && string.IsNullOrWhiteSpace(Preferences.CodeReviewPrompt))
        {
            Preferences.CodeReviewPrompt = CodeReviewPreferences;
            CodeReviewPreferences = null;
        }
        if (!string.IsNullOrWhiteSpace(GeneralPreferences) && string.IsNullOrWhiteSpace(Preferences.GeneralPrompt))
        {
            Preferences.GeneralPrompt = GeneralPreferences;
            GeneralPreferences = null;
        }
#pragma warning restore CS0618
    }
}
