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
    ///     세션이 추적 중인 GitHub PR 상태.
    ///     GitHub 이외 원격에서는 null 유지.
    /// </summary>
    public TrackedPullRequest? TrackedPr { get; set; }

    /// <summary>
    ///     기존 UI/마이그레이션 호환용 별칭.
    /// </summary>
    public string? LastPrUrl
    {
        get => string.IsNullOrWhiteSpace(TrackedPr?.Url) ? null : TrackedPr.Url;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                TrackedPr = null;
                return;
            }

            TrackedPr ??= new TrackedPullRequest();
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
