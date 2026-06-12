using System.Text.Json;

namespace Seoro.Shared.Services.Cli.Approval;

/// <summary>
///     양방향 CLI 프로토콜에서 도구/명령 실행 승인 요청을 처리하는 도메인 인터페이스.
///     Claude의 control_request(permission)와 Codex app-server의 *_requestApproval을
///     동일한 도메인 모델로 통일한다. 각 프로바이더는 자신의 와이어 포맷을
///     <see cref="ToolApprovalRequest" /> / <see cref="ToolApprovalDecision" />로 인코딩/디코딩한다.
/// </summary>
public interface IToolApprovalHandler
{
    /// <summary>승인 요청을 사용자(또는 정책)에게 전달하고 결정을 반환한다.</summary>
    Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken ct = default);
}

/// <summary>승인 요청의 종류.</summary>
public enum ToolApprovalKind
{
    /// <summary>셸/명령 실행 (Bash 등).</summary>
    Command,

    /// <summary>파일 변경 (Edit/Write 등).</summary>
    FileChange,

    /// <summary>사용자 입력 요청.</summary>
    UserInput,

    /// <summary>그 외 일반 도구.</summary>
    GenericTool
}

/// <summary>승인 결정 결과.</summary>
public enum ApprovalOutcome
{
    /// <summary>이번 한 번 허용.</summary>
    Allow,

    /// <summary>이번 세션 동안 허용 (반복 묻지 않음).</summary>
    AllowForSession,

    /// <summary>거부.</summary>
    Deny,

    /// <summary>현재 턴 취소.</summary>
    Cancel
}

/// <summary>승인 요청 도메인 모델. 프로바이더 와이어 포맷과 무관.</summary>
public sealed record ToolApprovalRequest
{
    /// <summary>요청이 속한 세션 식별자.</summary>
    public required string SessionId { get; init; }

    /// <summary>승인 요청 종류.</summary>
    public required ToolApprovalKind Kind { get; init; }

    /// <summary>도구 이름 (예: "Bash", "Edit").</summary>
    public required string ToolName { get; init; }

    /// <summary>Command 종류일 때 실행할 명령.</summary>
    public string? Command { get; init; }

    /// <summary>FileChange 종류일 때 변경 미리보기 목록.</summary>
    public IReadOnlyList<FileChangePreview>? FileChanges { get; init; }

    /// <summary>요청 사유 (선택).</summary>
    public string? Reason { get; init; }

    /// <summary>프로바이더 원본 입력 (디버깅/고급 표시용).</summary>
    public JsonElement? RawInput { get; init; }
}

/// <summary>파일 변경 미리보기 한 건.</summary>
public sealed record FileChangePreview
{
    /// <summary>변경 대상 파일 경로.</summary>
    public required string Path { get; init; }

    /// <summary>변경 종류 (예: "create", "modify", "delete").</summary>
    public string? ChangeType { get; init; }
}

/// <summary>승인 결정 도메인 모델.</summary>
public sealed record ToolApprovalDecision
{
    /// <summary>결정 결과.</summary>
    public required ApprovalOutcome Outcome { get; init; }

    /// <summary>거부 시 사유 (선택).</summary>
    public string? DenyReason { get; init; }

    /// <summary>한 번 허용하는 기본 결정.</summary>
    public static ToolApprovalDecision Allow { get; } = new() { Outcome = ApprovalOutcome.Allow };
}
