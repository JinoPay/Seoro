namespace Cominomi.Shared.Models;

public class AppSettings
{
    public bool AutoUpdateEnabled { get; set; } = true;
    public bool DebugMode { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public bool NotificationSound { get; set; } = true;

    // Onboarding
    public bool OnboardingCompleted { get; set; }
    public decimal? DefaultMaxBudgetUsd { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
    public double UiScale { get; set; } // 0 = auto (macOS: 1.1, Windows: 1.0)

    // Timeouts (seconds)
    public int DefaultProcessTimeoutSeconds { get; set; } = 30;
    public int HookTimeoutSeconds { get; set; } = 5;
    public int SchemaVersion { get; init; } = 1;
    public int UpdateCheckIntervalMinutes { get; set; } = 60;
    public int VersionCheckTimeoutSeconds { get; set; } = 5;
    public int? DefaultMaxTurns { get; set; }

    // Plugins
    public List<string> DisabledPlugins { get; set; } = [];
    public string DefaultEffortLevel { get; set; } = CominomiConstants.DefaultEffortLevel;
    public string DefaultModel { get; set; } = ModelDefinitions.Default.Id;
    public string DefaultPermissionMode { get; set; } = CominomiConstants.DefaultPermissionMode;
    public string LastSeenVersion { get; set; } = "";
    public string LastSessionId { get; set; } = "";
    public string LastWorkspaceId { get; set; } = "";
    public string NotificationSoundName { get; set; } = "default";
    public string Theme { get; set; } = "dark";
    public string? ClaudePath { get; set; }
    public string? DefaultCloneDirectory { get; set; }
    public string? FallbackModel { get; set; }
    public string? GhPath { get; set; }
    public string? GitPath { get; set; }
    public string? McpConfigPath { get; set; }

    // Terminal
    public string? TerminalShell { get; set; } // null = auto-detect
}