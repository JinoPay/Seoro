using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IChatPrWorkflowService
{
    /// <summary>
    /// Build the AI prompt for PR creation (including workspace preferences and issue linking).
    /// </summary>
    Task<string> BuildCreatePrPromptAsync(Session session);

    /// <summary>
    /// Merge an existing PR. Returns (status, error).
    /// </summary>
    Task<(SessionStatus Status, AppError? Error)> MergePrAsync(Session session);

    /// <summary>
    /// Force-push the branch. Returns (status, error).
    /// </summary>
    Task<(SessionStatus Status, AppError? Error)> ForcePushAsync(Session session);

    /// <summary>
    /// Reset conflict state and load full session for rebase prompt.
    /// Returns (fullSession, rebasePrompt).
    /// </summary>
    Task<(Session? FullSession, string RebasePrompt)> ResolveConflictsAsync(Session session);

    /// <summary>
    /// Check if a PR exists for the session's branch.
    /// Returns (prNumber, prUrl) if found and open, null otherwise.
    /// </summary>
    Task<(int? PrNumber, string? PrUrl)?> CheckPrStatusAsync(Session session);

    /// <summary>
    /// Determines the merge readiness of a session's PR (CI status, conflict state).
    /// </summary>
    Task<MergeReadiness> CheckMergeReadinessAsync(Session session);
}
