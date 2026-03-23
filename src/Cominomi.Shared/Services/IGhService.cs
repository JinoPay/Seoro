using Cominomi.Shared;

namespace Cominomi.Shared.Services;

public record IssueInfo(int Number, string Url, string Title, string State);

public interface IGhService
{
    Task<bool> IsAuthenticatedAsync(CancellationToken ct = default);
    Task<GitResult> CreateIssueAsync(string repoDir, string title, string body, CancellationToken ct = default);
    Task<List<IssueInfo>> ListIssuesAsync(string repoDir, string state = "open", int limit = CominomiConstants.GhDefaultIssueLimit, CancellationToken ct = default);
    Task<IssueInfo?> GetIssueAsync(string repoDir, int issueNumber, CancellationToken ct = default);
}
