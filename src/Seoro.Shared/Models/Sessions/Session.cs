using System.Text.Json.Serialization;
using Seoro.Shared.Models.Files;
using Seoro.Shared.Services;

namespace Seoro.Shared.Models.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionStatus
{
    Initializing,
    Pending,
    Ready,
    Archived,
    Error
}

[JsonConverter(typeof(SessionJsonConverter))]
public class Session
{
    private long _totalInputTokens;
    private long _totalOutputTokens;
    public AgentType AgentType { get; init; } = AgentType.Code;

    public AppError? Error { get; set; }
    public bool PlanCompleted { get; set; }
    public bool TitleLocked { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public decimal? MaxBudgetUsd { get; init; }

    // Git 관련 정보 (worktree, branch)
    public GitContext Git { get; init; } = new();
    public int? MaxTurns { get; init; }
    public List<ChatMessage> Messages { get; set; } = [];

    public long TotalInputTokens
    {
        get => _totalInputTokens;
        set => _totalInputTokens = Guard.NonNegative(value, nameof(TotalInputTokens));
    }

    public long TotalOutputTokens
    {
        get => _totalOutputTokens;
        set => _totalOutputTokens = Guard.NonNegative(value, nameof(TotalOutputTokens));
    }

    [JsonIgnore] public object MessagesLock { get; } = new();

    public SessionStatus Status { get; private set; } = SessionStatus.Initializing;
    public string CityName { get; init; } = "";
    public string EffortLevel { get; set; } = SeoroConstants.DefaultEffortLevel;
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Model { get; set; } = ModelDefinitions.Default.Id;
    public string PermissionMode { get; set; } = SeoroConstants.DefaultPermissionMode;
    public string Title { get; set; } = "New Chat";
    public string WorkspaceId { get; init; } = "default";

    public string? ConversationId { get; set; }

    /// <summary>
    ///     세션이 사용하는 CLI 프로바이더 식별자. "claude" 또는 "codex".
    ///     세션 생성 후 변경 불가 (init-only).
    /// </summary>
    public string Provider { get; init; } = "claude";

    [JsonIgnore] public bool IsCodex => Provider == "codex";

    /// <summary>Codex 세션별 샌드박스 모드 오버라이드. null이면 글로벌 설정 사용.</summary>
    public string? CodexSandboxMode { get; set; }

    /// <summary>Codex 세션별 승인 정책 오버라이드. null이면 글로벌 설정 사용.</summary>
    public string? CodexApprovalPolicy { get; set; }

    [JsonIgnore] public string? ErrorMessage => Error?.Message;

    /// <summary>
    ///     대기 중인 AskUserQuestion 도구 호출의 원본 JSON 입력.
    ///     세션 전환 후에도 하단 표시줄이 유지되도록 저장됩니다.
    ///     사용자가 응답하면 초기화됩니다.
    /// </summary>
    public string? PendingAskUserQuestionInput { get; set; }

    public string? PlanFilePath { get; set; }
    public string? PlanContent { get; set; }

    /// <summary>
    ///     세션 전환이나 앱 재진입 후에도 입력창 초안을 복원하기 위한 임시 저장 텍스트.
    /// </summary>
    public string DraftInputText { get; set; } = string.Empty;

    /// <summary>
    ///     전송 전 첨부파일 초안을 세션 단위로 임시 저장합니다.
    /// </summary>
    public List<PendingAttachment> DraftAttachments { get; set; } = [];

    /// <summary>
    ///     전송 전 슬래시 명령어 칩 초안을 세션 단위로 임시 저장합니다.
    /// </summary>
    public string? DraftSkillName { get; set; }

    /// <summary>
    ///     이 세션에서 비활성화된 MCP 서버 이름 목록.
    ///     bypassAll 모드에서 해당 서버의 mcp__name__* 패턴이 AllowedTools에서 제외됩니다.
    /// </summary>
    public HashSet<string> DisabledMcpServers { get; set; } = [];

    /// <summary>
    ///     진행 중인 스트리밍 턴의 예상 토큰 사용량.
    ///     MessageStartHandler / MessageDeltaHandler에 의해 스트리밍 중 업데이트되고,
    ///     ResultHandler에 의해 완료 시 초기화됩니다 (오케스트레이터의 finally 블록에 의해서도).
    ///     저장되지 않음 - 일시적 UI 상태만 포함합니다.
    /// </summary>
    [JsonIgnore] public long PendingInputTokens { get; set; }

    [JsonIgnore] public long PendingOutputTokens { get; set; }

    public List<ChatMessage> GetMessagesSnapshot()
    {
        lock (MessagesLock)
        {
            return Messages.ToList();
        }
    }

    /// <summary>
    ///     역직렬화 또는 테스트 설정을 위해 Status를 초기화합니다. 검증을 건너뜁니다.
    /// </summary>
    public void SetInitialStatus(SessionStatus status)
    {
        Status = status;
    }

    /// <summary>
    ///     상태 전환을 검증하고 적용합니다. 잘못된 전환 시 예외를 발생시킵니다.
    /// </summary>
    public void TransitionStatus(SessionStatus target)
    {
        if (Status == target)
            return;

        if (!SessionStatusMachine.IsValidTransition(Status, target))
            throw new InvalidOperationException(
                $"Invalid session status transition: {Status} → {target} (session {Id})");

        Status = target;
    }
}
