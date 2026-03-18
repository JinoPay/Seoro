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
    public HookEvent Event { get; set; }
    public HookType Type { get; set; } = HookType.Command;
    public string Command { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public string? Matcher { get; set; }
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 5;
}

public record HookExecutionResult(
    string Command,
    bool Success,
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut);
