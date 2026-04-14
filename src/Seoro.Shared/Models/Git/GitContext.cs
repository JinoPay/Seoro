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
    ///     자동 추적 PR 상태. AI 응답에서 자동 캡처되거나 사용자가 "PR 확인" 버튼으로 설정.
    ///     세션 JSON 에 영속화됨 (v5 스키마).
    /// </summary>
    public TrackedPullRequest? TrackedPr { get; set; }

    /// <summary>
    ///     하위 호환 프로퍼티. TrackedPr.Url 과 양방향 동기화된다.
    ///     ⚠ PR #245 경고: 이 프로퍼티를 직접 set 하는 경로는 (1) TrackedPr 동기화,
    ///     (2) 사용자 수동 입력, (3) JSON 역직렬화 뿐이어야 한다.
    /// </summary>
    public string? LastPrUrl
    {
        get => TrackedPr?.Url;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                TrackedPr = null;
                return;
            }

            if (TrackedPr == null)
                TrackedPr = new TrackedPullRequest { Url = value };
            else
                TrackedPr.Url = value;
        }
    }

    /// <summary>
    ///     Diff 비교 기준을 반환합니다.
    ///     BaseCommit(고정 해시) → BaseBranch(브랜치명) → HEAD 순으로 폴백합니다.
    /// </summary>
    public string GetDiffBase() =>
        !string.IsNullOrEmpty(BaseCommit) ? BaseCommit
        : !string.IsNullOrEmpty(BaseBranch) ? BaseBranch
        : "HEAD";
}