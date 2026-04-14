namespace Seoro.Shared.Services.Git;

public record PullRequestMergeResult(bool Success, TrackedPullRequest? PullRequest, string ErrorMessage = "");

public interface IPullRequestService
{
    Task<TrackedPullRequest?> TryCaptureCreatedPrAsync(Session session, ChatMessage assistantMessage,
        CancellationToken ct = default);

    Task<TrackedPullRequest?> GetPrForBranchAsync(Session session, CancellationToken ct = default);

    Task<TrackedPullRequest?> RefreshAsync(Session session, CancellationToken ct = default);

    Task<PullRequestMergeResult> MergeAsync(Session session, PullRequestMergeStrategy strategy,
        CancellationToken ct = default);

    Task<bool> IsGhAvailableAsync(CancellationToken ct = default);
}
