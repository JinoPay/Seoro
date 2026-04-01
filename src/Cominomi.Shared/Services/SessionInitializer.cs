using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SessionInitializer : ISessionInitializer
{
    private readonly IGitService _gitService;
    private readonly ILogger<SessionInitializer> _logger;

    public SessionInitializer(
        IGitService gitService,
        ILogger<SessionInitializer> logger)
    {
        _gitService = gitService;
        _logger = logger;
    }

    public async Task<(List<BranchGroup> Groups, string DefaultBranch)> LoadBranchesAsync(string repoLocalPath)
    {
        try { await _gitService.FetchAllAsync(repoLocalPath); }
        catch (Exception ex) { _logger.LogDebug(ex, "Fetch failed for {RepoPath}, continuing with cached branches", repoLocalPath); }

        var branchGroupsTask = _gitService.ListAllBranchesGroupedAsync(repoLocalPath);
        var defaultBranchTask = _gitService.DetectDefaultBranchAsync(repoLocalPath);
        await Task.WhenAll(branchGroupsTask, defaultBranchTask);

        var groups = branchGroupsTask.Result;
        var defaultBranch = defaultBranchTask.Result
            ?? groups.SelectMany(g => g.Branches).FirstOrDefault()
            ?? "";

        return (groups, defaultBranch);
    }
}
