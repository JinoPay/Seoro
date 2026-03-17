namespace Cominomi.Shared.Models;

public class AppSettings
{
    public string DefaultModel { get; set; } = ModelDefinitions.Default.Id;
    public string Theme { get; set; } = "dark";
    public string? ClaudePath { get; set; }
    public string? DefaultCloneDirectory { get; set; }
    public string DefaultEffortLevel { get; set; } = "auto";
    public string DefaultPermissionMode { get; set; } = "bypassAll";
    public int? DefaultMaxTurns { get; set; }
    public decimal? DefaultMaxBudgetUsd { get; set; }
    public string? FallbackModel { get; set; }
    public string? McpConfigPath { get; set; }
    public bool DebugMode { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
    public string LastWorkspaceId { get; set; } = "";
    public string LastSessionId { get; set; } = "";
}
