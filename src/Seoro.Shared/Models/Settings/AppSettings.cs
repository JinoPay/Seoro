namespace Seoro.Shared.Models.Settings;

public class AppSettings
{
    public bool AutoUpdateEnabled { get; set; } = true;
    public bool DebugMode { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public bool NotificationSound { get; set; } = true;

    // 온보딩
    public bool OnboardingCompleted { get; set; }
    public decimal? DefaultMaxBudgetUsd { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
    public double UiScale { get; set; } // 0 = 자동 (macOS: 1.1, Windows: 1.0)

    // 타임아웃 (초)
    public int DefaultProcessTimeoutSeconds { get; set; } = 30;
    public int HookTimeoutSeconds { get; set; } = 5;
    public int SchemaVersion { get; init; } = 1;
    public int UpdateCheckIntervalMinutes { get; set; } = 60;
    public int VersionCheckTimeoutSeconds { get; set; } = 5;
    public int? DefaultMaxTurns { get; set; }

    // 플러그인
    public List<string> DisabledPlugins { get; set; } = [];
    public string DefaultEffortLevel { get; set; } = SeoroConstants.DefaultEffortLevel;
    public string DefaultModel { get; set; } = ModelDefinitions.Default.Id;
    public string DefaultPermissionMode { get; set; } = SeoroConstants.DefaultPermissionMode;
    public string SessionLanguage { get; set; } = "en"; // "en" 또는 "ko" — 브랜치명 + 로컬 제목
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

    // 터미널
    public string? TerminalShell { get; set; } // null = 자동 감지
}