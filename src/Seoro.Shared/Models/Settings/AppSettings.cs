namespace Seoro.Shared.Models.Settings;

public class AppSettings
{
    public bool AutoUpdateEnabled { get; set; } = true;
    public bool DebugMode { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public bool NotificationSound { get; set; } = true;

    /// <summary>
    ///     스쿼시 머지가 성공하면 해당 세션을 자동으로 Archived 상태로 전환한다.
    ///     기본 false — 사용자가 설정에서 명시적으로 켜야 동작.
    ///     컨덕터 스타일 자동화 옵션 (Phase 5).
    /// </summary>
    public bool AutoArchiveOnMergeSuccess { get; set; }

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
    public string UiLanguage { get; set; } = "ko"; // "ko" | "en" — 앱 UI 표시 언어
    public string SessionLanguage { get; set; } = "en"; // "en" 또는 "ko" — 브랜치명 + 로컬 제목
    public string LastSeenVersion { get; set; } = "";
    public string LastSessionId { get; set; } = "";
    public string LastWorkspaceId { get; set; } = "";
    public string NotificationSoundName { get; set; } = "default";
    public string Theme { get; set; } = "dark";
    public string? ClaudePath { get; set; }
    public string? CodexPath { get; set; }
    public string DefaultCodexModel { get; set; } = "gpt-5.4";
    public string CodexApprovalPolicy { get; set; } = "never"; // untrusted | on-request | never
    public string CodexSandboxMode { get; set; } = "workspace-write"; // read-only | workspace-write | danger-full-access
    public string CodexReasoningEffort { get; set; } = "medium"; // minimal | low | medium | high | xhigh
    public bool CodexWebSearch { get; set; }
    public bool CodexEphemeral { get; set; }
    public string? DefaultCloneDirectory { get; set; }
    public string? FallbackModel { get; set; }
    public string? GhPath { get; set; }
    public string? GitPath { get; set; }
    public string? McpConfigPath { get; set; }

    // 터미널
    public string? TerminalShell { get; set; } // null = 자동 감지

    // 머지 설정
    /// <summary>기본 PR 머지 전략. "Merge" | "Squash" | "Rebase"</summary>
    public string DefaultMergeStrategy { get; set; } = "Squash";

    // AI 프롬프트 템플릿 (null = SeoroConstants 기본값 사용)
    /// <summary>PR 생성 시 AI에게 전달하는 프롬프트. {branch}, {target}, {uncommittedNote} 변수 지원.</summary>
    public string? MergePromptCreatePr { get; set; }
    /// <summary>커밋·푸시 시 AI에게 전달하는 프롬프트. {branch} 변수 지원.</summary>
    public string? MergePromptPush { get; set; }
    /// <summary>충돌 해결 시 AI에게 전달하는 프롬프트. {conflictFiles} 변수 지원.</summary>
    public string? MergePromptResolveConflict { get; set; }
}