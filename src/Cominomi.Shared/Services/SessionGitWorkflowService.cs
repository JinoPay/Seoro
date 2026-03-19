using Cominomi.Shared;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Services;

public class SessionGitWorkflowService : ISessionGitWorkflowService
{
    private readonly ISessionService _sessionService;
    private readonly IGitService _gitService;
    private readonly IGhService _ghService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IHooksEngine _hooksEngine;
    private readonly IOptionsMonitor<AppSettings> _appSettings;
    private readonly ILogger<SessionGitWorkflowService> _logger;

    public SessionGitWorkflowService(
        ISessionService sessionService,
        IGitService gitService,
        IGhService ghService,
        IWorkspaceService workspaceService,
        IHooksEngine hooksEngine,
        IOptionsMonitor<AppSettings> appSettings,
        ILogger<SessionGitWorkflowService> logger)
    {
        _sessionService = sessionService;
        _gitService = gitService;
        _ghService = ghService;
        _workspaceService = workspaceService;
        _hooksEngine = hooksEngine;
        _appSettings = appSettings;
        _logger = logger;
    }

    public async Task<bool> CheckMergeStatusAsync(string sessionId)
    {
        var session = await _sessionService.LoadSessionAsync(sessionId);
        if (session == null || session.Status != SessionStatus.Ready || session.Git.IsLocalDir)
            return false;

        var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
        if (workspace == null)
            return false;

        var baseBranch = ResolveBaseBranch(session,
            await _gitService.DetectDefaultBranchAsync(workspace.RepoLocalPath));

        var isMerged = await _gitService.IsBranchMergedAsync(
            workspace.RepoLocalPath, session.Git.BranchName, baseBranch);

        if (isMerged)
        {
            session.TransitionStatus(SessionStatus.Merged);
            await _sessionService.SaveSessionAsync(session);
        }

        return isMerged;
    }

    public async Task<Session> PushBranchAsync(string sessionId, bool force = false, CancellationToken ct = default)
    {
        var (session, workspace) = await LoadSessionAndWorkspaceAsync(sessionId);
        return await PushBranchInternalAsync(session, workspace, force, ct);
    }

    public async Task<Session> CreatePrAsync(string sessionId, string title, string body, CancellationToken ct = default)
    {
        var (session, workspace) = await LoadSessionAndWorkspaceAsync(sessionId);
        return await CreatePrInternalAsync(session, workspace, title, body, ct);
    }

    public async Task<Session> MergePrAsync(string sessionId, string mergeMethod = CominomiConstants.DefaultMergeStrategy, CancellationToken ct = default)
    {
        var (session, workspace) = await LoadSessionAndWorkspaceAsync(sessionId);
        return await MergePrInternalAsync(session, workspace, mergeMethod, ct);
    }

    public async Task<Session> RebaseOntoBaseAsync(string sessionId, CancellationToken ct = default)
    {
        var (session, workspace) = await LoadSessionAndWorkspaceAsync(sessionId);
        return await RebaseInternalAsync(session, workspace, ct);
    }

    public async Task<Session> ClosePrAsync(string sessionId, CancellationToken ct = default)
    {
        var (session, workspace) = await LoadSessionAndWorkspaceAsync(sessionId);
        return await ClosePrInternalAsync(session, workspace, ct);
    }

    public async Task<Session> MergeAllAsync(string sessionId, string mergeMethod = CominomiConstants.DefaultMergeStrategy, string? prBodyTemplate = null, CancellationToken ct = default)
    {
        // Single load for the entire pipeline
        var (session, workspace) = await LoadSessionAndWorkspaceAsync(sessionId);

        // Step 1: Push (if not already pushed)
        if (session.Status == SessionStatus.Ready)
        {
            session = await PushBranchInternalAsync(session, workspace, force: false, ct);
            if (session.Status != SessionStatus.Pushed)
                return session; // push failed, ErrorMessage set
        }

        // Step 2: Create PR (if not already created)
        if (session.Status == SessionStatus.Pushed)
        {
            var baseBranch = ResolveBaseBranch(session,
                await _gitService.DetectDefaultBranchAsync(workspace.RepoLocalPath));

            var title = session.Title is "New Chat" or "" ? session.Git.BranchName : session.Title;

            string body;
            if (!string.IsNullOrEmpty(prBodyTemplate))
            {
                body = prBodyTemplate
                    .Replace("{branchName}", session.Git.BranchName)
                    .Replace("{baseBranch}", baseBranch)
                    .Replace("{sessionTitle}", session.Title);
            }
            else
            {
                var logResult = await _gitService.GetCommitLogAsync(workspace.RepoLocalPath, baseBranch, ct);
                body = logResult.Success ? logResult.Output.Trim() : "";
            }

            if (session.Pr.IssueNumber != null && !body.Contains($"#{session.Pr.IssueNumber}"))
            {
                body = $"Closes #{session.Pr.IssueNumber}\n\n{body}";
            }

            session = await CreatePrInternalAsync(session, workspace, title, body, ct);
            if (session.Status != SessionStatus.PrOpen)
                return session; // PR creation failed
        }

        // Step 3: Merge
        if (session.Status == SessionStatus.PrOpen)
        {
            session = await MergePrInternalAsync(session, workspace, mergeMethod, ct);

            // Step 3b: Auto-rebase on conflict, then retry merge
            if (session.Status == SessionStatus.ConflictDetected)
            {
                _logger.LogInformation("Merge conflict detected for session {SessionId}, attempting auto-rebase", sessionId);

                session = await RebaseInternalAsync(session, workspace, ct);

                if (session.Status == SessionStatus.Ready)
                {
                    // Rebase succeeded — re-push and retry merge
                    session = await PushBranchInternalAsync(session, workspace, force: true, ct);
                    if (session.Status == SessionStatus.Pushed)
                    {
                        // Transition back to PrOpen since PR already exists
                        session.TransitionStatus(SessionStatus.PrOpen);
                        await _sessionService.SaveSessionAsync(session);

                        session = await MergePrInternalAsync(session, workspace, mergeMethod, ct);
                    }
                }
            }
        }

        return session;
    }

    public async Task RetryAfterConflictResolveAsync(string sessionId)
    {
        var session = await _sessionService.LoadSessionAsync(sessionId)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found.");

        session.TransitionStatus(SessionStatus.Ready);
        session.Error = null;
        session.Pr.ConflictFiles = null;
        await _sessionService.SaveSessionAsync(session);
    }

    // ── Internal methods that accept pre-loaded session/workspace ──

    private async Task<Session> PushBranchInternalAsync(Session session, Workspace workspace, bool force, CancellationToken ct)
    {
        // Fetch latest remote state to avoid push conflicts from others' changes
        await _gitService.FetchAsync(workspace.RepoLocalPath, ct);

        GitResult result;
        if (force)
            result = await _gitService.PushForceBranchAsync(workspace.RepoLocalPath, session.Git.BranchName, ct);
        else
            result = await _gitService.PushBranchAsync(workspace.RepoLocalPath, session.Git.BranchName, ct);

        if (result.Success)
        {
            session.TransitionStatus(SessionStatus.Pushed);
            session.Error = null;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _hooksEngine.FireAsync(HookEvent.OnBranchPush, new Dictionary<string, string>
                    {
                        ["COMINOMI_SESSION_ID"] = session.Id,
                        ["COMINOMI_BRANCH"] = session.Git.BranchName
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
            session.Error = AppError.ClassifyPushError(result.Error);
        }

        await _sessionService.SaveSessionAsync(session);
        return session;
    }

    private async Task<Session> CreatePrInternalAsync(Session session, Workspace workspace, string title, string body, CancellationToken ct)
    {
        var baseBranch = ResolveBaseBranch(session,
            await _gitService.DetectDefaultBranchAsync(workspace.RepoLocalPath));

        var result = await _ghService.CreatePrAsync(
            workspace.RepoLocalPath, session.Git.BranchName, baseBranch, title, body, ct);

        if (result.Success)
        {
            session.TransitionStatus(SessionStatus.PrOpen);
            session.Error = null;

            // Parse PR URL from output (gh pr create prints the URL)
            session.Pr.PrUrl = result.Output.Trim();

            // Try to get PR number
            var prInfo = await _ghService.GetPrForBranchAsync(workspace.RepoLocalPath, session.Git.BranchName, ct);
            if (prInfo != null)
            {
                session.Pr.PrNumber = prInfo.Number;
                session.Pr.PrUrl = prInfo.Url;
            }
        }
        else
        {
            session.Error = AppError.PrCreation(result.Error);
        }

        await _sessionService.SaveSessionAsync(session);
        return session;
    }

    private async Task<Session> MergePrInternalAsync(Session session, Workspace workspace, string mergeMethod, CancellationToken ct)
    {
        if (session.Pr.PrNumber == null)
        {
            // Try to find PR by branch name
            var prInfo = await _ghService.GetPrForBranchAsync(workspace.RepoLocalPath, session.Git.BranchName, ct);
            if (prInfo == null)
            {
                session.Error = AppError.PrNotFoundError("PR not found for this branch.");
                await _sessionService.SaveSessionAsync(session);
                return session;
            }
            session.Pr.PrNumber = prInfo.Number;
            session.Pr.PrUrl = prInfo.Url;
        }

        // Wait for CI checks if configured
        var settings = _appSettings.CurrentValue;
        if (settings.WaitForCiBeforeMerge)
        {
            var timeout = TimeSpan.FromSeconds(settings.CiCheckTimeoutSeconds);
            _logger.LogInformation("Waiting for CI checks on PR #{PrNumber} (timeout={Timeout}s)", session.Pr.PrNumber.Value, timeout.TotalSeconds);

            var checkResult = await _ghService.WaitForChecksAsync(workspace.RepoLocalPath, session.Pr.PrNumber.Value, timeout, ct);
            if (!checkResult.AllPassed)
            {
                session.Error = AppError.CiChecksFailed(checkResult.Summary);
                await _sessionService.SaveSessionAsync(session);
                return session;
            }
        }

        var result = await _ghService.MergePrAsync(workspace.RepoLocalPath, session.Pr.PrNumber.Value, mergeMethod, ct);

        if (result.Success)
        {
            session.TransitionStatus(SessionStatus.Merged);
            session.Error = null;
        }
        else
        {
            var error = AppError.ClassifyMergeError(result.Error, result.Output);
            session.Error = error;

            if (error.Code == ErrorCode.PrMergeConflict)
                session.TransitionStatus(SessionStatus.ConflictDetected);
        }

        await _sessionService.SaveSessionAsync(session);
        return session;
    }

    private async Task<Session> RebaseInternalAsync(Session session, Workspace workspace, CancellationToken ct)
    {
        var baseBranch = ResolveBaseBranch(session,
            await _gitService.DetectDefaultBranchAsync(workspace.RepoLocalPath));

        // Fetch latest before rebase
        await _gitService.FetchAsync(workspace.RepoLocalPath, ct);

        var worktreePath = session.Git.WorktreePath ?? workspace.RepoLocalPath;
        var result = await _gitService.RebaseAsync(worktreePath, baseBranch, ct);

        if (result.Success)
        {
            _logger.LogInformation("Rebase succeeded for session {SessionId}", session.Id);
            session.TransitionStatus(SessionStatus.Ready);
            session.Error = null;
            session.Pr.ConflictFiles = null;
        }
        else
        {
            _logger.LogWarning("Rebase failed for session {SessionId}: {Error}", session.Id, result.Error);
            session.Error = AppError.General($"Rebase failed: {result.Error}");
        }

        await _sessionService.SaveSessionAsync(session);
        return session;
    }

    private async Task<Session> ClosePrInternalAsync(Session session, Workspace workspace, CancellationToken ct)
    {
        if (session.Pr.PrNumber == null)
        {
            var prInfo = await _ghService.GetPrForBranchAsync(workspace.RepoLocalPath, session.Git.BranchName, ct);
            if (prInfo == null)
            {
                session.Error = AppError.PrNotFoundError("PR not found for this branch.");
                await _sessionService.SaveSessionAsync(session);
                return session;
            }
            session.Pr.PrNumber = prInfo.Number;
        }

        var result = await _ghService.ClosePrAsync(workspace.RepoLocalPath, session.Pr.PrNumber.Value, ct);

        if (result.Success)
        {
            session.TransitionStatus(SessionStatus.Ready);
            session.Pr.PrUrl = null;
            session.Pr.PrNumber = null;
            session.Error = null;
            _logger.LogInformation("PR #{PrNumber} closed for session {SessionId}", session.Pr.PrNumber, session.Id);
        }
        else
        {
            session.Error = AppError.General($"Failed to close PR: {result.Error}");
        }

        await _sessionService.SaveSessionAsync(session);
        return session;
    }

    private static string ResolveBaseBranch(Session session, string? detectedDefault)
    {
        return !string.IsNullOrEmpty(session.Git.BaseBranch)
            ? session.Git.BaseBranch
            : detectedDefault ?? "main";
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
