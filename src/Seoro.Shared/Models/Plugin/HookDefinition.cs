using System.Text.Json.Serialization;

namespace Seoro.Shared.Models.Plugin;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HookEvent
{
    // Seoro 앱 레벨 이벤트
    OnMessageComplete,
    OnSessionCreate,
    OnSessionArchive,
    OnBranchPush,
    OnPrCreate,
    OnPrMerge,

    // CLI 표준 hook 이벤트 (Claude CLI hooks.md와 맞춤)
    PreToolUse,
    PostToolUse,
    NotificationSend,
    Stop
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HookType
{
    Command,
    Prompt
}

public class HookDefinition
{
    public bool Enabled { get; set; } = true;
    public HookEvent Event { get; set; }
    public HookType Type { get; set; } = HookType.Command;

    /// <summary>
    ///     Hook별 타임아웃 오버라이드 (초). null = AppSettings.HookTimeoutSeconds 전역 기본값 사용.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    public string Command { get; set; } = string.Empty;
    public string? Matcher { get; set; }
    public string? WorkingDirectory { get; set; }
}

public record HookExecutionResult(
    string Command,
    bool Success,
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut);