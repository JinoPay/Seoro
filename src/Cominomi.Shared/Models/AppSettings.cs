namespace Cominomi.Shared.Models;

public class AppSettings
{
    public string DefaultModel { get; set; } = ModelDefinitions.Default.Id;
    public string Theme { get; set; } = "dark";
    public string? ClaudePath { get; set; }
    public string? DefaultCloneDirectory { get; set; }
    public bool DefaultThinkingEnabled { get; set; }
    public string DefaultPermissionMode { get; set; } = "bypassAll";
    public string LastWorkspaceId { get; set; } = "";
    public string LastSessionId { get; set; } = "";
}
