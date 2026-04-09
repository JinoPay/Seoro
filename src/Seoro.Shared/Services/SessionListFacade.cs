using Seoro.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace Seoro.Shared.Services;

public class SessionListFacade(
    IChatState chatState,
    ISessionService sessionService,
    IOptionsMonitor<AppSettings> appSettings,
    ISettingsService settingsService,
    SessionListDataService dataService,
    IDialogService dialogService,
    ISnackbar snackbar,
    ISkillRegistry skillRegistry,
    IClaudeService claudeService,
    INotificationHistoryService notificationHistory,
    IWorkspaceService workspaceService,
    IGitService gitService,
    ILogger<SessionListFacade> logger)
    : ISessionListFacade
{
    public async Task CleanupSessionAsync(Session session)
    {
        await sessionService.CleanupSessionAsync(session.Id);
        session.TransitionStatus(SessionStatus.Archived);
    }

    public async Task SelectSessionAsync(Session session, Workspace? ws, List<Workspace> workspaces)
    {
        ws ??= workspaces.FirstOrDefault(w => w.Id == session.WorkspaceId);
        if (ws != null)
            await SwitchWorkspaceAsync(ws);

        // LoadSessionAsync checks the active session registry first,
        // so we always get the authoritative in-memory instance if one exists.
        var fullSess = await sessionService.LoadSessionAsync(session.Id);
        chatState.SetSession(fullSess ?? session);
        notificationHistory.MarkSessionAsRead(session.Id);

        await SaveLastSelectionAsync(session.WorkspaceId, session.Id);
    }

    public Task<(Workspace? Workspace, Session? Session, string? ProjectName)> RestoreLastSelectionAsync(
        List<Workspace> workspaces,
        Dictionary<string, List<Session>> sessionCache)
    {
        var settings = appSettings.CurrentValue;
        var workspace = workspaces.FirstOrDefault(w => w.Id == settings.LastWorkspaceId)
                        ?? workspaces.FirstOrDefault();

        if (workspace == null)
            return Task.FromResult<(Workspace?, Session?, string?)>((null, null, null));

        var projectName = SessionListDataService.GetProjectName(workspace);

        Session? lastSession = null;
        if (!string.IsNullOrEmpty(settings.LastSessionId)
            && sessionCache.TryGetValue(workspace.Id, out var cached))
            lastSession = cached.FirstOrDefault(s => s.Id == settings.LastSessionId);

        return Task.FromResult<(Workspace?, Session?, string?)>((workspace, lastSession, projectName));
    }

    public async Task<bool> DeleteSessionAsync(Session session)
    {
        var isStreaming = chatState.IsSessionStreaming(session.Id);
        var confirmMessage = isStreaming
            ? $"'{session.Title}' 세션이 현재 진행 중입니다. Claude 프로세스를 종료하고 삭제하시겠습니까?"
            : $"'{session.Title}' 세션을 삭제하시겠습니까?";

        var result = await dialogService.ShowMessageBoxAsync(
            "세션 삭제", confirmMessage,
            "삭제", cancelText: "취소");

        if (result != true) return false;

        // Stop Claude process before deletion
        claudeService.Cancel(session.Id);

        await sessionService.DeleteSessionAsync(session.Id);
        notificationHistory.MarkSessionAsRead(session.Id);

        if (dataService.SessionCache.TryGetValue(session.WorkspaceId, out var sessions))
            sessions.RemoveAll(s => s.Id == session.Id);

        if (chatState.CurrentSession?.Id == session.Id)
            chatState.SetSession(null!);
        else
            chatState.NotifyStateChanged(); // Refresh LandingPage recent chats list

        dataService.DiffStatsCache.Remove(session.Id);
        dataService.RebuildOrderedSessions();
        snackbar.SessionDeleted();

        return true;
    }

    public async Task<bool> DeleteWorkspaceAsync(Workspace workspace)
    {
        var result = await dialogService.ShowMessageBoxAsync(
            "워크스페이스 삭제",
            $"'{workspace.Name}' 워크스페이스와 모든 세션이 삭제됩니다. 계속하시겠습니까?",
            "삭제", cancelText: "취소");

        if (result != true) return false;

        try
        {
            // Stop streaming and clean up caches for all sessions
            if (dataService.SessionCache.TryGetValue(workspace.Id, out var cachedSessions))
                foreach (var session in cachedSessions)
                {
                    claudeService.Cancel(session.Id);
                    notificationHistory.MarkSessionAsRead(session.Id);
                    dataService.DiffStatsCache.Remove(session.Id);
                }

            // Delete all sessions
            var sessions = await sessionService.GetSessionsByWorkspaceAsync(workspace.Id);
            foreach (var session in sessions) await sessionService.DeleteSessionAsync(session.Id);

            // Delete the workspace
            await workspaceService.DeleteWorkspaceAsync(workspace.Id);

            // Clear ChatState if this workspace is active
            if (chatState.CurrentWorkspace?.Id == workspace.Id)
            {
                chatState.SetSession(null!);
                chatState.SetWorkspace(null!);
            }

            // Close settings panel if open for this workspace
            if (chatState.SettingsWorkspaceId == workspace.Id)
                chatState.CloseSettings();

            // Refresh workspace list (fires OnDataChanged → sidebar rebuilds)
            await dataService.RefreshWorkspacesAsync();

            snackbar.WorkspaceDeleted(workspace.Name);
            return true;
        }
        catch (Exception)
        {
            snackbar.Add("삭제 중 오류가 발생했습니다.", Severity.Error);
            return false;
        }
    }

    public async Task<Session> CreateSessionAsync(Workspace ws, bool localDir = false)
    {
        // Workspace override > App default
        var model = !string.IsNullOrEmpty(ws.DefaultModel)
            ? ws.DefaultModel
            : appSettings.CurrentValue.DefaultModel;

        var session = localDir
            ? await sessionService.CreateLocalDirSessionAsync(model, ws.Id)
            : await sessionService.CreatePendingSessionAsync(model, ws.Id);

        await SwitchWorkspaceAsync(ws);
        chatState.SetSession(session);

        if (dataService.SessionCache.TryGetValue(ws.Id, out var sessions))
            sessions.Insert(0, session);

        await SaveLastSelectionAsync(ws.Id, session.Id);

        dataService.RebuildOrderedSessions();

        // Eagerly create worktree in background for non-localDir sessions
        if (!localDir)
            _ = InitializeWorktreeInBackgroundAsync(session, ws);

        return session;
    }

    private async Task InitializeWorktreeInBackgroundAsync(Session session, Workspace ws)
    {
        try
        {
            var defaultBranch = await gitService.DetectDefaultBranchAsync(ws.RepoLocalPath) ?? "main";
            var updated = await sessionService.InitializeWorktreeAsync(session.Id, defaultBranch);

            session.Git.WorktreePath = updated.Git.WorktreePath;
            session.Git.BranchName = updated.Git.BranchName;
            session.Git.BaseBranch = updated.Git.BaseBranch;
            session.Git.BaseCommit = updated.Git.BaseCommit;
            session.SetInitialStatus(updated.Status);
            session.Error = updated.Error;

            chatState.NotifyStateChanged();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background worktree init failed for session {SessionId}", session.Id);
            session.TransitionStatus(SessionStatus.Error);
            session.Error = AppError.FromException(ErrorCode.WorktreeCreationFailed, ex);
            chatState.NotifyStateChanged();
        }
    }

    private async Task SaveLastSelectionAsync(string workspaceId, string sessionId)
    {
        var settings = appSettings.CurrentValue;
        settings.LastWorkspaceId = workspaceId;
        settings.LastSessionId = sessionId;
        await settingsService.SaveAsync(settings);
    }

    private async Task SwitchWorkspaceAsync(Workspace ws)
    {
        var previousId = chatState.CurrentWorkspace?.Id;
        chatState.SetWorkspace(ws);

        // Reload custom skills when workspace changes (different project path)
        if (previousId != ws.Id)
            await skillRegistry.LoadCustomCommandsAsync(ws.RepoLocalPath);
    }
}