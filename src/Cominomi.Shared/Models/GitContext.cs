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
    public string BranchName { get; set; } = "";
    public string WorktreePath { get; set; } = string.Empty;
}