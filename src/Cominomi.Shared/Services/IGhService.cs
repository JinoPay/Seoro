using Cominomi.Shared;

namespace Cominomi.Shared.Services;

public record PrInfo(int Number, string Url, string State);
public record IssueInfo(int Number, string Url, string Title, string State);
public record PrCheckResult(bool AllPassed, bool HasPending, string Summary);

public interface IGhService
{
    Task<GitResult> CreatePrAsync(string repoDir, string head, string baseBranch, string title, string body, CancellationToken ct = default);
    Task<GitResult> MergePrAsync(string repoDir, int prNumber, string mergeMethod = CominomiConstants.DefaultMergeStrategy, CancellationToken ct = default);
    Task<GitResult> ClosePrAsync(string repoDir, int prNumber, CancellationToken ct = default);
    Task<PrInfo?> GetPrForBranchAsync(string repoDir, string branchName, CancellationToken ct = default);
    Task<bool> IsAuthenticatedAsync(CancellationToken ct = default);
    Task<GitResult> CreateIssueAsync(string repoDir, string title, string body, CancellationToken ct = default);
    Task<List<IssueInfo>> ListIssuesAsync(string repoDir, string state = "open", int limit = CominomiConstants.GhDefaultIssueLimit, CancellationToken ct = default);
    Task<IssueInfo?> GetIssueAsync(string repoDir, int issueNumber, CancellationToken ct = default);

    /// <summary>
    /// Polls PR checks until all pass, any fail, or the timeout elapses.
    /// </summary>
    Task<PrCheckResult> WaitForChecksAsync(string repoDir, int prNumber, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// Single-shot: returns current CI check state without polling.
    /// </summary>
    Task<PrCheckResult> GetChecksStatusAsync(string repoDir, int prNumber, CancellationToken ct = default);
}
