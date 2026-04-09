using System.Text.Json.Serialization;

namespace Seoro.Shared.Models.Settings;

/// <summary>
///     Claude CLI의 기본 settings.json 스키마를 나타냅니다.
///     세 가지 범위를 지원합니다: Global (~/.claude/settings.json),
///     Project (.claude/settings.json), Local (.claude/settings.local.json).
/// </summary>
public class ClaudeSettings
{
    public bool? AlwaysThinkingEnabled { get; set; }

    public bool? AutoMemoryEnabled { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClaudeDefaultMode? DefaultMode { get; set; }

    /// <summary>
    ///     서버명으로 인덱싱된 MCP 서버 설정.
    /// </summary>
    public Dictionary<string, ClaudeMcpServerConfig>? McpServers { get; set; }

    /// <summary>
    ///     Claude CLI hook 형식: 이벤트명 → hook 이벤트 설정 목록.
    ///     각 설정은 선택적 matcher와 handler 목록을 포함합니다.
    /// </summary>
    public Dictionary<string, List<ClaudeHookEventConfig>>? Hooks { get; set; }

    public Dictionary<string, string>? Env { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EffortLevel? EffortLevel { get; set; }

    public List<string>? AdditionalDirectories { get; set; }

    public PermissionRules? Permissions { get; set; }
    public string? Model { get; set; }
}

public class PermissionRules
{
    public List<string>? Allow { get; set; }
    public List<string>? Ask { get; set; }
    public List<string>? Deny { get; set; }
}

/// <summary>
///     단일 hook 이벤트 설정 항목. 선택적 matcher 패턴과
///     이벤트 발생 시 실행할 hook handler 목록을 포함합니다.
/// </summary>
public class ClaudeHookEventConfig
{
    public List<ClaudeHookHandler> Hooks { get; set; } = [];
    public string? Matcher { get; set; }
}

public class ClaudeHookHandler
{
    public bool? Async { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClaudeHookHandlerType Type { get; set; } = ClaudeHookHandlerType.Command;

    public Dictionary<string, string>? Headers { get; set; }
    public int? Timeout { get; set; }

    public string? Command { get; set; }
    public string? Model { get; set; }
    public string? Prompt { get; set; }
    public string? StatusMessage { get; set; }
    public string? Url { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClaudeHookHandlerType
{
    Command,
    Http,
    Prompt,
    Agent
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffortLevel
{
    Low,
    Medium,
    High
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClaudeDefaultMode
{
    Default,
    Plan,
    AcceptEdits,
    DontAsk,
    BypassPermissions
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClaudeSettingsScope
{
    /// <summary>~/.claude/settings.json</summary>
    Global,

    /// <summary>.claude/settings.json (git-tracked)</summary>
    Project,

    /// <summary>.claude/settings.local.json (gitignored)</summary>
    Local
}

/// <summary>
///     MCP server config in Claude CLI settings.json format.
///     Supports stdio (command+args) and sse (url+headers) transports.
/// </summary>
public class ClaudeMcpServerConfig
{
    // shared
    public Dictionary<string, string>? Env { get; set; }
    public Dictionary<string, string>? Headers { get; set; }

    public List<string>? Args { get; set; }

    // stdio fields
    public string? Command { get; set; }

    // sse fields
    public string? Url { get; set; }
}

/// <summary>
///     All 22 Claude CLI hook events.
/// </summary>
public static class ClaudeHookEvents
{
    public static readonly IReadOnlyList<string> All =
    [
        PreToolUse, PostToolUse, PostToolUseFailure,
        SessionStart, SessionEnd,
        PermissionRequest, UserPromptSubmit, Notification,
        SubagentStart, SubagentStop,
        Stop, StopFailure,
        ConfigChange,
        PreCompact, PostCompact,
        InstructionsLoaded,
        WorktreeCreate, WorktreeRemove,
        Elicitation, ElicitationResult,
        TeammateIdle, TaskCompleted
    ];

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [PreToolUse] = "도구 실행 전",
        [PostToolUse] = "도구 성공 후",
        [PostToolUseFailure] = "도구 실패 후",
        [SessionStart] = "세션 시작 또는 재개 시",
        [SessionEnd] = "세션 종료 시",
        [PermissionRequest] = "권한 다이얼로그 표시 시",
        [UserPromptSubmit] = "사용자 입력 처리 전",
        [Notification] = "알림 전송 시",
        [SubagentStart] = "서브에이전트 생성 시",
        [SubagentStop] = "서브에이전트 종료 시",
        [Stop] = "Claude 응답 완료 시",
        [StopFailure] = "API 오류로 턴 종료 시",
        [ConfigChange] = "설정 파일 변경 시",
        [PreCompact] = "컨텍스트 압축 전",
        [PostCompact] = "컨텍스트 압축 후",
        [InstructionsLoaded] = "CLAUDE.md 파일 로드 시",
        [WorktreeCreate] = "워크트리 생성 시",
        [WorktreeRemove] = "워크트리 제거 시",
        [Elicitation] = "MCP 사용자 입력 요청 시",
        [ElicitationResult] = "MCP 사용자 응답 시",
        [TeammateIdle] = "팀 에이전트 유휴 시",
        [TaskCompleted] = "태스크 완료 시"
    };


    public const string ConfigChange = "ConfigChange";
    public const string Elicitation = "Elicitation";
    public const string ElicitationResult = "ElicitationResult";
    public const string InstructionsLoaded = "InstructionsLoaded";
    public const string Notification = "Notification";
    public const string PermissionRequest = "PermissionRequest";
    public const string PostCompact = "PostCompact";
    public const string PostToolUse = "PostToolUse";
    public const string PostToolUseFailure = "PostToolUseFailure";
    public const string PreCompact = "PreCompact";
    public const string PreToolUse = "PreToolUse";
    public const string SessionEnd = "SessionEnd";
    public const string SessionStart = "SessionStart";
    public const string Stop = "Stop";
    public const string StopFailure = "StopFailure";
    public const string SubagentStart = "SubagentStart";
    public const string SubagentStop = "SubagentStop";
    public const string TaskCompleted = "TaskCompleted";
    public const string TeammateIdle = "TeammateIdle";
    public const string UserPromptSubmit = "UserPromptSubmit";
    public const string WorktreeCreate = "WorktreeCreate";
    public const string WorktreeRemove = "WorktreeRemove";
}