using Cominomi.Shared;

namespace Cominomi.Shared.Services;

public record PrInfo(int Number, string Url, string State);
public record IssueInfo(int Number, string Url, string Title, string State);

public interface IGhService
{
    Task<GitResult> CreatePrAsync(string repoDir, string head, string baseBranch, string title, string body, CancellationToken ct = default);
    Task<GitResult> MergePrAsync(string repoDir, int prNumber, string mergeMethod = CominomiConstants.DefaultMergeStrategy, CancellationToken ct = default);
    Task<PrInfo?> GetPrForBranchAsync(string repoDir, string branchName, CancellationToken ct = default);
    Task<bool> IsAuthenticatedAsync(CancellationToken ct = default);
    Task<GitResult> CreateIssueAsync(string repoDir, string title, string body, CancellationToken ct = default);
    Task<List<IssueInfo>> ListIssuesAsync(string repoDir, string state = "open", int limit = 30, CancellationToken ct = default);
    Task<IssueInfo?> GetIssueAsync(string repoDir, int issueNumber, CancellationToken ct = default);
}
