using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HookEvent
{
    // Cominomi app-level events
    OnMessageComplete,
    OnSessionCreate,
    OnSessionArchive,
    OnBranchPush,
    OnPrCreate,
    OnPrMerge,

    // CLI standard hook events (aligned with Claude CLI hooks.md)
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
    ///     Per-hook timeout override in seconds. Null = use AppSettings.HookTimeoutSeconds global default.
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