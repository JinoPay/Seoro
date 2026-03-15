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
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string DefaultModel { get; set; } = ModelDefinitions.Default.Id;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Git fields
    public WorkspaceStatus Status { get; set; } = WorkspaceStatus.Initializing;
    public string RepoUrl { get; set; } = "";
    public string RepoLocalPath { get; set; } = "";
    public string BaseBranch { get; set; } = "main";
    public string? ErrorMessage { get; set; }
}
