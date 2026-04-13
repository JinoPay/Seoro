namespace Seoro.Shared.Models.Git;

public enum PullRequestLifecycleState
{
    Unknown,
    Open,
    Closed,
    Merged
}

public enum PullRequestMergeStrategy
{
    Merge,
    Squash,
    Rebase
}

public class TrackedPullRequest
{
    public string Url { get; set; } = string.Empty;
    public int? Number { get; set; }
    public string BaseBranch { get; set; } = string.Empty;
    public string HeadBranch { get; set; } = string.Empty;
    public PullRequestLifecycleState State { get; set; }
    public bool IsDraft { get; set; }
    public bool? IsMergeable { get; set; }
    public bool IsMerged { get; set; }
    public string MergeStateStatus { get; set; } = string.Empty;
    public string ReviewDecision { get; set; } = string.Empty;
    public string ChecksSummary { get; set; } = string.Empty;
    public DateTime? LastCheckedAtUtc { get; set; }
    public DateTime? MergedAtUtc { get; set; }
    public string LastMergeCommitSha { get; set; } = string.Empty;
}
