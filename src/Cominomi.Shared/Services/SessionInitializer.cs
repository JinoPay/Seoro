using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SessionInitializer(
    IGitService gitService,
    ILogger<SessionInitializer> logger)
    : ISessionInitializer
{
    public async Task<(List<BranchGroup> Groups, string DefaultBranch)> LoadBranchesAsync(string repoLocalPath)
    {
        try
        {
            await gitService.FetchAllAsync(repoLocalPath);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Fetch failed for {RepoPath}, continuing with cached branches", repoLocalPath);
        }

        var branchGroupsTask = gitService.ListAllBranchesGroupedAsync(repoLocalPath);
        var defaultBranchTask = gitService.DetectDefaultBranchAsync(repoLocalPath);
        await Task.WhenAll(branchGroupsTask, defaultBranchTask);

        var groups = branchGroupsTask.Result;
        var defaultBranch = defaultBranchTask.Result
                            ?? groups.SelectMany(g => g.Branches).FirstOrDefault()
                            ?? "";

        return (groups, defaultBranch);
    }
}