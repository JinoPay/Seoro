using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Sessions;

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
            logger.LogDebug(ex, "{RepoPath}에 대한 Fetch 실패, 캐시된 브랜치로 계속 진행", repoLocalPath);
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