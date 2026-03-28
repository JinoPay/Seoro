using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISessionInitializer
{
    /// <summary>
    /// Fetch remote branches and return grouped list + default branch name.
    /// </summary>
    Task<(List<BranchGroup> Groups, string DefaultBranch)> LoadBranchesAsync(string repoLocalPath);
}
