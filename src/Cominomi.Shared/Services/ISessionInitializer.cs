using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISessionInitializer
{
    /// <summary>
    /// Fetch remote branches and return grouped list + default branch name.
    /// </summary>
    Task<(List<BranchGroup> Groups, string DefaultBranch)> LoadBranchesAsync(string repoLocalPath);

    /// <summary>
    /// Summarize the first user message with Haiku and rename the worktree branch accordingly.
    /// Returns (title, newBranchName) — newBranchName is null if rename was skipped or failed.
    /// </summary>
    Task<(string? Title, string? NewBranchName)> SummarizeAndRenameBranchAsync(
        Session session, string userText);
}
