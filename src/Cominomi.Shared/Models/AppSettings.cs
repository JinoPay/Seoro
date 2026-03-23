using Cominomi.Shared;

namespace Cominomi.Shared.Models;

public class AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string DefaultModel { get; set; } = ModelDefinitions.Default.Id;
    public string Theme { get; set; } = "dark";
    public string? ClaudePath { get; set; }
    public string? GitPath { get; set; }
    public string? DefaultCloneDirectory { get; set; }
    public string DefaultEffortLevel { get; set; } = CominomiConstants.DefaultEffortLevel;
    public string DefaultPermissionMode { get; set; } = CominomiConstants.DefaultPermissionMode;
    public int? DefaultMaxTurns { get; set; }
    public decimal? DefaultMaxBudgetUsd { get; set; }
    public string? FallbackModel { get; set; }
    public string? McpConfigPath { get; set; }
    public bool DebugMode { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public bool NotificationSound { get; set; } = true;
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
    public string LastWorkspaceId { get; set; } = "";
    public string LastSessionId { get; set; } = "";

    // Summarization
    public string SummarizationModel { get; set; } = "haiku";
    public string SummarizationPrompt { get; set; } = "Generate a short, natural title for this chat (3-7 words). Use the same language as the user's message. Use Title Case (capitalize each word). Output only the title text, nothing else.";

    // Timeouts (seconds)
    public int DefaultProcessTimeoutSeconds { get; set; } = 30;
    public int HookTimeoutSeconds { get; set; } = 5;
    public int SummarizationTimeoutSeconds { get; set; } = 15;
    public int VersionCheckTimeoutSeconds { get; set; } = 5;

    // Plugins
    public List<string> DisabledPlugins { get; set; } = [];
}
