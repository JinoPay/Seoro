namespace Cominomi.Shared.Services;

public record PrInfo(int Number, string Url, string State);

public interface IGhService
{
    Task<GitResult> CreatePrAsync(string repoDir, string head, string baseBranch, string title, string body, CancellationToken ct = default);
    Task<GitResult> MergePrAsync(string repoDir, int prNumber, string mergeMethod = "squash", CancellationToken ct = default);
    Task<PrInfo?> GetPrForBranchAsync(string repoDir, string branchName, CancellationToken ct = default);
    Task<bool> IsAuthenticatedAsync(CancellationToken ct = default);
}
