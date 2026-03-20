using System.Collections.Concurrent;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SessionSyncService : ISessionSyncService
{
    private readonly ISessionService _sessionService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IGitService _gitService;
    private readonly IGhService _ghService;
    private readonly IChatEventBus _eventBus;
    private readonly IChatState _chatState;
    private readonly ILogger<SessionSyncService> _logger;

    private readonly ConcurrentDictionary<string, DateTime> _lastSyncTimes = new();
    private const int CooldownSeconds = 30;

    public SessionSyncService(
        ISessionService sessionService,
        IWorkspaceService workspaceService,
        IGitService gitService,
        IGhService ghService,
        IChatEventBus eventBus,
        IChatState chatState,
        ILogger<SessionSyncService> logger)
    {
        _sessionService = sessionService;
        _workspaceService = workspaceService;
        _gitService = gitService;
        _ghService = ghService;
        _eventBus = eventBus;
        _chatState = chatState;
        _logger = logger;
    }

    public async Task<SessionSyncResult> SyncAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var session = await _sessionService.LoadSessionAsync(sessionId);
            if (session == null || session.Git.IsLocalDir)
                return SessionSyncResult.Skipped;

            if (session.Status is SessionStatus.Initializing or SessionStatus.Pending or SessionStatus.Archived or SessionStatus.Merged)
                return SessionSyncResult.Skipped;

            if (_chatState.IsSessionStreaming(sessionId))
                return SessionSyncResult.Skipped;

            // Cooldown: skip if synced within last 30 seconds
            if (_lastSyncTimes.TryGetValue(sessionId, out var lastSync)
                && (DateTime.UtcNow - lastSync).TotalSeconds < CooldownSeconds)
                return SessionSyncResult.Skipped;

            var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
            if (workspace == null)
                return SessionSyncResult.Skipped;

            var repoDir = workspace.RepoLocalPath;
            var branchName = session.Git.BranchName;
            var baseBranch = !string.IsNullOrEmpty(session.Git.BaseBranch)
                ? session.Git.BaseBranch
                : await _gitService.DetectDefaultBranchAsync(repoDir) ?? "main";

            // Step 1: git fetch
            bool fetched = false;
            var fetchResult = await _gitService.FetchAsync(repoDir, ct);
            fetched = fetchResult.Success;
            if (!fetched)
                _logger.LogWarning("Git fetch failed for session {SessionId}: {Error}", sessionId, fetchResult.Error);

            // Step 2: Check if branch is already merged into base
            if (session.Status is SessionStatus.Ready or SessionStatus.Pushed or SessionStatus.PrOpen)
            {
                var isMerged = await _gitService.IsBranchMergedAsync(repoDir, branchName, baseBranch, ct);
                if (isMerged)
                {
                    session.TransitionStatus(SessionStatus.Merged);
                    await _sessionService.SaveSessionAsync(session);
                    _lastSyncTimes[sessionId] = DateTime.UtcNow;

                    var result = new SessionSyncResult(SessionStatus.Merged, MergeReadiness.Merged, fetched, "MERGED", null);
                    _eventBus.Publish(new SessionSyncCompletedEvent(sessionId, result));
                    _logger.LogInformation("Session {SessionId} branch merged externally, status updated to Merged", sessionId);
                    return result;
                }
            }

            // Step 3: Check PR state for sessions with PR context
            if (session.Status is SessionStatus.PrOpen or SessionStatus.Pushed
                && (session.Pr.PrNumber != null || !string.IsNullOrEmpty(branchName)))
            {
                var prInfo = await _ghService.GetPrForBranchAsync(repoDir, branchName, ct);

                if (prInfo != null)
                {
                    // Update PR metadata if needed
                    if (session.Pr.PrNumber != prInfo.Number || session.Pr.PrUrl != prInfo.Url)
                    {
                        session.Pr.PrNumber = prInfo.Number;
                        session.Pr.PrUrl = prInfo.Url;
                        await _sessionService.SaveSessionAsync(session);
                    }

                    if (prInfo.State is "MERGED" or "merged")
                    {
                        session.TransitionStatus(SessionStatus.Merged);
                        await _sessionService.SaveSessionAsync(session);
                        _lastSyncTimes[sessionId] = DateTime.UtcNow;

                        var result = new SessionSyncResult(SessionStatus.Merged, MergeReadiness.Merged, fetched, "MERGED", null);
                        _eventBus.Publish(new SessionSyncCompletedEvent(sessionId, result));
                        _logger.LogInformation("Session {SessionId} PR merged externally, status updated to Merged", sessionId);
                        return result;
                    }

                    if (prInfo.State is "CLOSED" or "closed")
                    {
                        session.TransitionStatus(SessionStatus.Ready);
                        session.Pr.PrUrl = null;
                        session.Pr.PrNumber = null;
                        session.Error = null;
                        await _sessionService.SaveSessionAsync(session);
                        _lastSyncTimes[sessionId] = DateTime.UtcNow;

                        var result = new SessionSyncResult(SessionStatus.Ready, null, fetched, "CLOSED", null);
                        _eventBus.Publish(new SessionSyncCompletedEvent(sessionId, result));
                        _logger.LogInformation("Session {SessionId} PR closed externally, status reverted to Ready", sessionId);
                        return result;
                    }

                    // PR is still open — check merge readiness
                    if (session.Status == SessionStatus.PrOpen && session.Pr.PrNumber != null)
                    {
                        var readiness = await CheckReadinessAsync(repoDir, session.Pr.PrNumber.Value, ct);
                        _lastSyncTimes[sessionId] = DateTime.UtcNow;

                        var result = new SessionSyncResult(null, readiness, fetched, "OPEN", null);
                        _eventBus.Publish(new SessionSyncCompletedEvent(sessionId, result));
                        return result;
                    }
                }
            }

            // No state change detected
            _lastSyncTimes[sessionId] = DateTime.UtcNow;
            var noChangeResult = new SessionSyncResult(null, null, fetched, null, null);
            _eventBus.Publish(new SessionSyncCompletedEvent(sessionId, noChangeResult));
            return noChangeResult;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session sync failed for {SessionId}", sessionId);
            var errorResult = new SessionSyncResult(null, null, false, null, ex.Message);
            _eventBus.Publish(new SessionSyncCompletedEvent(sessionId, errorResult));
            return errorResult;
        }
    }

    private async Task<MergeReadiness> CheckReadinessAsync(string repoDir, int prNumber, CancellationToken ct)
    {
        try
        {
            var checkResult = await _ghService.GetChecksStatusAsync(repoDir, prNumber, ct);
            if (checkResult.AllPassed)
                return MergeReadiness.Mergeable;
            if (checkResult.HasPending)
                return MergeReadiness.ChecksPending;
            return MergeReadiness.ChecksFailed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check CI status for PR #{PrNumber}", prNumber);
            return MergeReadiness.Unknown;
        }
    }
}
