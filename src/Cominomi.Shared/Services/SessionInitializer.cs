using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SessionInitializer : ISessionInitializer
{
    private readonly IGitService _gitService;
    private readonly IClaudeService _claudeService;
    private readonly ILogger<SessionInitializer> _logger;

    public SessionInitializer(
        IGitService gitService,
        IClaudeService claudeService,
        ILogger<SessionInitializer> logger)
    {
        _gitService = gitService;
        _claudeService = claudeService;
        _logger = logger;
    }

    public async Task<(List<BranchGroup> Groups, string DefaultBranch)> LoadBranchesAsync(string repoLocalPath)
    {
        try { await _gitService.FetchAllAsync(repoLocalPath); }
        catch { /* Continue with cached branches if offline */ }

        var branchGroupsTask = _gitService.ListAllBranchesGroupedAsync(repoLocalPath);
        var defaultBranchTask = _gitService.DetectDefaultBranchAsync(repoLocalPath);
        await Task.WhenAll(branchGroupsTask, defaultBranchTask);

        var groups = branchGroupsTask.Result;
        var defaultBranch = defaultBranchTask.Result
            ?? groups.SelectMany(g => g.Branches).FirstOrDefault()
            ?? "";

        return (groups, defaultBranch);
    }

    public async Task<(string? Title, string? NewBranchName)> SummarizeAndRenameBranchAsync(
        Session session, string userText)
    {
        try
        {
            var summary = await _claudeService.SummarizeAsync(userText, session.Git.WorktreePath);
            if (string.IsNullOrEmpty(summary))
                return (null, null);

            string? newBranchName = null;
            if (!session.Git.IsLocalDir)
            {
                var candidate = SessionService.GenerateBranchName(summary);
                var renameResult = await _gitService.RenameBranchAsync(
                    session.Git.WorktreePath, session.Git.BranchName, candidate);
                if (renameResult.Success)
                    newBranchName = candidate;
            }

            return (summary, newBranchName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Haiku summarization failed, will use fallback title");
            return (null, null);
        }
    }
}
