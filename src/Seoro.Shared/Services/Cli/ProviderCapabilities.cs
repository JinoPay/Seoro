namespace Seoro.Shared.Services.Cli;

/// <summary>
///     CLI 프로바이더가 지원하는 기능을 선언하는 레코드.
///     UI 및 서비스 코드에서 프로바이더 ID 하드코딩 대신 이 플래그를 조회한다.
/// </summary>
public record ProviderCapabilities
{
    /// <summary>Effort Level (--effort-level) 지원 여부.</summary>
    public bool SupportsEffortLevel { get; init; }

    /// <summary>세션 포크 지원 여부.</summary>
    public bool SupportsForkSession { get; init; }

    /// <summary>플랜 모드 권한 지원 여부.</summary>
    public bool SupportsPlanMode { get; init; }

    /// <summary>AllowedTools / DisallowedTools 지원 여부.</summary>
    public bool SupportsToolFiltering { get; init; }

    /// <summary>MaxBudgetUsd 지원 여부.</summary>
    public bool SupportsMaxBudget { get; init; }

    /// <summary>Fallback 모델 지원 여부.</summary>
    public bool SupportsFallbackModel { get; init; }

    /// <summary>이미지 첨부 (--image) 지원 여부.</summary>
    public bool SupportsImageAttachment { get; init; }

    /// <summary>웹 검색 지원 여부. Codex exec: --config web_search=live.</summary>
    public bool SupportsWebSearch { get; init; }

    /// <summary>MCP 서버 지원 여부.</summary>
    public bool SupportsMcp { get; init; }

    /// <summary>
    ///     양방향 프로토콜에서 도구별 실시간 승인 콜백 지원 여부.
    ///     UI가 승인 다이얼로그를 띄울지 판단하는 데 사용한다.
    /// </summary>
    public bool SupportsToolApproval { get; init; }
}
