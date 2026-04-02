namespace Cominomi.Shared.Models;

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
    ///     Diff 비교 기준을 반환합니다.
    ///     BaseCommit(고정 해시) → BaseBranch(브랜치명) → HEAD 순으로 폴백합니다.
    /// </summary>
    public string GetDiffBase() =>
        !string.IsNullOrEmpty(BaseCommit) ? BaseCommit
        : !string.IsNullOrEmpty(BaseBranch) ? BaseBranch
        : "HEAD";
}