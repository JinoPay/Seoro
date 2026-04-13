namespace Seoro.Shared.Models.Git;

/// <summary>
///     Git worktree/branch 관련 속성을 그룹화한 모델.
///     Session에서 분리하여 관심사를 명확히 구분합니다.
/// </summary>
public class GitContext
{
    public bool IsLocalDir { get; set; }
    public List<string> AdditionalDirs { get; set; } = [];
    public string BaseBranch { get; set; } = "";
    public string BaseCommit { get; set; } = "";
    public string BranchName { get; set; } = "";
    public string WorktreePath { get; set; } = string.Empty;

    /// <summary>
    ///     사용자가 수동으로 붙여넣은 PR 링크. null 이면 미지정.
    ///     ⚠ PR #245 재발 방지: 이 필드는 <b>반드시 사용자 수동 입력</b>으로만 채워져야 한다.
    ///     Seoro 가 어시스턴트 메시지에서 자동 파싱하거나 gh API 로 폴링해서 채우는 것은 금지.
    ///     과거에 PrUrl/PrNumber 를 자동 추적하려 했던 기능이 런타임 와이어링 없이 죽은 코드로 남아
    ///     #245 에서 통째로 제거된 이력이 있다. 자동 감지 코드를 여기에 추가하려면 그 교훈을 먼저 검토할 것.
    /// </summary>
    public string? LastPrUrl { get; set; }

    /// <summary>
    ///     Diff 비교 기준을 반환합니다.
    ///     BaseCommit(고정 해시) → BaseBranch(브랜치명) → HEAD 순으로 폴백합니다.
    /// </summary>
    public string GetDiffBase() =>
        !string.IsNullOrEmpty(BaseCommit) ? BaseCommit
        : !string.IsNullOrEmpty(BaseBranch) ? BaseBranch
        : "HEAD";
}