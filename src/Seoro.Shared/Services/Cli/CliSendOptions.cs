namespace Seoro.Shared.Services.Cli;

/// <summary>
///     CLI 프로바이더에 메시지를 전송할 때 사용하는 옵션 레코드.
///     프로바이더별로 지원하지 않는 옵션은 무시된다.
/// </summary>
public record CliSendOptions
{
    /// <summary>전송할 메시지 내용.</summary>
    public required string Message { get; init; }

    /// <summary>CLI 프로세스의 작업 디렉터리.</summary>
    public required string WorkingDir { get; init; }

    /// <summary>사용할 모델 ID.</summary>
    public required string Model { get; init; }

    /// <summary>권한 모드. Claude: --permission-mode, Codex: --config approval_policy + --sandbox.</summary>
    public string PermissionMode { get; init; } = SeoroConstants.DefaultPermissionMode;

    /// <summary>노력 수준. Claude: --effort, Codex: model_reasoning_effort로 매핑.</summary>
    public string EffortLevel { get; init; } = SeoroConstants.DefaultEffortLevel;

    /// <summary>세션 식별자.</summary>
    public string? SessionId { get; init; }

    /// <summary>대화 재개 ID. Claude: conversation_id, Codex: thread_id.</summary>
    public string? ConversationId { get; init; }

    /// <summary>시스템 프롬프트.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>이전 대화를 계속할지 여부.</summary>
    public bool ContinueMode { get; init; }

    /// <summary>세션을 포크할지 여부. Claude: --fork-session, Codex: codex fork로 매핑.</summary>
    public bool ForkSession { get; init; }

    /// <summary>최대 턴 수.</summary>
    public int? MaxTurns { get; init; }

    /// <summary>최대 예산 (USD). Claude 전용 (Codex에서는 무시).</summary>
    public decimal? MaxBudgetUsd { get; init; }

    /// <summary>추가 접근 허용 디렉터리 목록.</summary>
    public List<string>? AdditionalDirs { get; init; }

    /// <summary>허용할 도구 목록. Claude 전용 (Codex에서는 무시).</summary>
    public List<string>? AllowedTools { get; init; }

    /// <summary>차단할 도구 목록. Claude 전용 (Codex에서는 무시).</summary>
    public List<string>? DisallowedTools { get; init; }
}
