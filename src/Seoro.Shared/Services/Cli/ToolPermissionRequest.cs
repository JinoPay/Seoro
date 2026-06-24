namespace Seoro.Shared.Services.Cli;

/// <summary>대화형 권한 요청의 종류. CLI가 도구 실행 전 호스트 승인을 구할 때 분기에 쓴다.</summary>
public enum ToolPermissionKind
{
    /// <summary>객관식 질문(AskUserQuestion).</summary>
    AskUserQuestion,

    /// <summary>플랜 제출/승인(ExitPlanMode).</summary>
    ExitPlanMode,

    /// <summary>그 외 일반 도구(자동 허용 대상).</summary>
    Other,
}

/// <summary>
///     CLI가 도구 실행 직전에 보내는 권한 요청을 AgentBridge 비의존 형태로 표현한 모델.
///     <see cref="ToolPermissionHandler"/>로 전달되어 UI가 소비한다.
/// </summary>
public sealed record ToolPermissionRequest
{
    /// <summary>요청 종류.</summary>
    public required ToolPermissionKind Kind { get; init; }

    /// <summary>도구 이름(예: "AskUserQuestion", "ExitPlanMode").</summary>
    public required string ToolName { get; init; }

    /// <summary>이 요청에 대응하는 tool_use 식별자(있으면).</summary>
    public string? ToolUseId { get; init; }

    /// <summary>AskUserQuestion 도구 입력의 원본 JSON. 기존 UI 파서가 그대로 소비한다.</summary>
    public string? RawInputJson { get; init; }

    /// <summary>ExitPlanMode 플랜 텍스트.</summary>
    public string? PlanText { get; init; }
}

/// <summary>호스트가 권한 요청에 내리는 결정. 세 구현 중 하나.</summary>
public abstract record ToolPermissionDecision;

/// <summary>
///     AskUserQuestion 응답. 질문 텍스트별 선택 목록과(또는) 자유 응답을 담는다.
///     단일 선택 질문은 목록의 첫 값만 쓰인다.
/// </summary>
public sealed record AllowAnswerDecision(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Selections,
    string? FreeResponse = null) : ToolPermissionDecision;

/// <summary>
///     도구 실행 허용. <see cref="NextPermissionMode"/>를 지정하면(ExitPlanMode 승인)
///     그 권한 모드로 전환한 뒤 허용한다(= AgentBridge ApprovePlanAsync).
/// </summary>
public sealed record AllowDecision(string? NextPermissionMode = null) : ToolPermissionDecision;

/// <summary>도구 실행 거부. <see cref="Interrupt"/>가 true면 현재 턴도 중단한다.</summary>
public sealed record DenyDecision(string Message, bool Interrupt = false) : ToolPermissionDecision;

/// <summary>
///     대화형 권한 요청 핸들러. UI 응답이 올 때까지 대기한 뒤 결정을 돌려준다.
///     Claude 프로바이더 전용이며, 미지정 시 권한 콜백 경로는 비활성화된다.
/// </summary>
public delegate Task<ToolPermissionDecision> ToolPermissionHandler(
    ToolPermissionRequest request, CancellationToken ct);
