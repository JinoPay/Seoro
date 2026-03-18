using Cominomi.Shared;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SessionGitWorkflowService : ISessionGitWorkflowService
{
    private readonly ISessionService _sessionService;
    private readonly IGitService _gitService;
    private readonly IGhService _ghService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IHooksEngine _hooksEngine;
    private readonly ILogger<SessionGitWorkflowService> _logger;

    public SessionGitWorkflowService(
        ISessionService sessionService,
        IGitService gitService,
        IGhService ghService,
        IWorkspaceService workspaceService,
        IHooksEngine hooksEngine,
        ILogger<SessionGitWorkflowService> logger)
    {
        _sessionService = sessionService;
        _gitService = gitService;
        _ghService = ghService;
        _workspaceService = workspaceService;
        _hooksEngine = hooksEngine;
        _logger = logger;
    }

    public async Task<bool> CheckMergeStatusAsync(string sessionId)
    {
        var session = await _sessionService.LoadSessionAsync(sessionId);
        if (session == null || session.Status != SessionStatus.Ready || session.IsLocalDir)
            return false;

        var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
        if (workspace == null)
            return false;

        var baseBranch = !string.IsNullOrEmpty(session.BaseBranch)
            ? session.BaseBranch
            : await _gitService.DetectDefaultBranchAsync(workspace.RepoLocalPath) ?? "main";

        var isMerged = await _gitService.IsBranchMergedAsync(
            workspace.RepoLocalPath, session.BranchName, baseBranch);

        if (isMerged)
        {
            session.Status = SessionStatus.Merged;
            await _sessionService.SaveSessionAsync(session);
        }

        return isMerged;
    }

    public async Task<Session> PushBranchAsync(string sessionId, bool force = false, CancellationToken ct = default)
    {
        var (session, workspace) = await LoadSessionAndWorkspaceAsync(sessionId);

        GitResult result;
        if (force)
            result = await _gitService.PushForceBranchAsync(workspace.RepoLocalPath, session.BranchName, ct);
        else
            result = await _gitService.PushBranchAsync(workspace.RepoLocalPath, session.BranchName, ct);

        if (result.Success)
        {
            session.Status = SessionStatus.Pushed;
            session.ErrorMessage = null;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _hooksEngine.FireAsync(HookEvent.OnBranchPush, new Dictionary<string, string>
                    {
                        ["COMINOMI_SESSION_ID"] = session.Id,
                        ["COMINOMI_BRANCH"] = session.BranchName
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Hook fire failed for OnBranchPush");
                }
            });
        }
        else
        {
            session.ErrorMessage = result.Error;
        }

        await _sessionService.SaveSessionAsync(session);
        return session;
    }

    public async Task<Session> CreatePrAsync(string sessionId, string title, string body, CancellationToken ct = default)
    {
        var (session, workspace) = await LoadSessionAndWorkspaceAsync(sessionId);

        var baseBranch = !string.IsNullOrEmpty(session.BaseBranch)
            ? session.BaseBranch
            : await _gitService.DetectDefaultBranchAsync(workspace.RepoLocalPath) ?? "main";

        var result = await _ghService.CreatePrAsync(
            workspace.RepoLocalPath, session.BranchName, baseBranch, title, body, ct);

        if (result.Success)
        {
            session.Status = SessionStatus.PrOpen;
            session.ErrorMessage = null;

            // Parse PR URL from output (gh pr create prints the URL)
            session.PrUrl = result.Output.Trim();

            // Try to get PR number
            var prInfo = await _ghService.GetPrForBranchAsync(workspace.RepoLocalPath, session.BranchName, ct);
            if (prInfo != null)
            {
                session.PrNumber = prInfo.Number;
                session.PrUrl = prInfo.Url;
            }
        }
        else
        {
            session.ErrorMessage = result.Error;
        }

        await _sessionService.SaveSessionAsync(session);
        return session;
    }

    public async Task<Session> MergePrAsync(string sessionId, string mergeMethod = CominomiConstants.DefaultMergeStrategy, CancellationToken ct = default)
    {
        var (session, workspace) = await LoadSessionAndWorkspaceAsync(sessionId);

        if (session.PrNumber == null)
        {
            // Try to find PR by branch name
            var prInfo = await _ghService.GetPrForBranchAsync(workspace.RepoLocalPath, session.BranchName, ct);
            if (prInfo == null)
            {
                session.ErrorMessage = "PR not found for this branch.";
                await _sessionService.SaveSessionAsync(session);
                return session;
            }
            session.PrNumber = prInfo.Number;
            session.PrUrl = prInfo.Url;
        }

        var result = await _ghService.MergePrAsync(workspace.RepoLocalPath, session.PrNumber.Value, mergeMethod, ct);

        if (result.Success)
        {
            session.Status = SessionStatus.Merged;
            session.ErrorMessage = null;
        }
        else
        {
            // Check if it's a conflict error
            var errorLower = (result.Error + result.Output).ToLowerInvariant();
            if (errorLower.Contains("conflict") || errorLower.Contains("merge") || errorLower.Contains("not mergeable"))
            {
                session.Status = SessionStatus.ConflictDetected;
                session.ErrorMessage = result.Error;
            }
            else
            {
                session.ErrorMessage = result.Error;
            }
        }

        await _sessionService.SaveSessionAsync(session);
        return session;
    }

    public async Task<Session> MergeAllAsync(string sessionId, string mergeMethod = CominomiConstants.DefaultMergeStrategy, string? prBodyTemplate = null, CancellationToken ct = default)
    {
        var (session, workspace) = await LoadSessionAndWorkspaceAsync(sessionId);

        // Step 1: Push (if not already pushed)
        if (session.Status == SessionStatus.Ready)
        {
            session = await PushBranchAsync(sessionId, force: false, ct);
            if (session.Status != SessionStatus.Pushed)
                return session; // push failed, ErrorMessage set
        }

        // Step 2: Create PR (if not already created)
        if (session.Status == SessionStatus.Pushed)
        {
            var baseBranch = !string.IsNullOrEmpty(session.BaseBranch)
                ? session.BaseBranch
                : await _gitService.DetectDefaultBranchAsync(workspace.RepoLocalPath) ?? "main";

            var title = session.Title is "New Chat" or "" ? session.BranchName : session.Title;

            string body;
            if (!string.IsNullOrEmpty(prBodyTemplate))
            {
                body = prBodyTemplate
                    .Replace("{branchName}", session.BranchName)
                    .Replace("{baseBranch}", baseBranch)
                    .Replace("{sessionTitle}", session.Title);
            }
            else
            {
                var logResult = await _gitService.GetCommitLogAsync(workspace.RepoLocalPath, baseBranch, ct);
                body = logResult.Success ? logResult.Output.Trim() : "";
            }

            if (session.IssueNumber != null && !body.Contains($"#{session.IssueNumber}"))
            {
                body = $"Closes #{session.IssueNumber}\n\n{body}";
            }

            session = await CreatePrAsync(sessionId, title, body, ct);
            if (session.Status != SessionStatus.PrOpen)
                return session; // PR creation failed
        }

        // Step 3: Merge
        if (session.Status == SessionStatus.PrOpen)
        {
            session = await MergePrAsync(sessionId, mergeMethod, ct);
        }

        return session;
    }

    public async Task RetryAfterConflictResolveAsync(string sessionId)
    {
        var session = await _sessionService.LoadSessionAsync(sessionId)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found.");

        session.Status = SessionStatus.Ready;
        session.ErrorMessage = null;
        session.ConflictFiles = null;
        await _sessionService.SaveSessionAsync(session);
    }

    private async Task<(Session session, Workspace workspace)> LoadSessionAndWorkspaceAsync(string sessionId)
    {
        var session = await _sessionService.LoadSessionAsync(sessionId)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found.");
        var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId)
            ?? throw new InvalidOperationException($"Workspace '{session.WorkspaceId}' not found.");
        return (session, workspace);
    }
}
