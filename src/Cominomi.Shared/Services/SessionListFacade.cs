using Cominomi.Shared.Models;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace Cominomi.Shared.Services;

public class SessionListFacade : ISessionListFacade
{
    private readonly IChatState _chatState;
    private readonly ISessionService _sessionService;
    private readonly IActiveSessionRegistry _activeSessionRegistry;
    private readonly IOptionsMonitor<AppSettings> _appSettings;
    private readonly ISettingsService _settingsService;
    private readonly SessionListDataService _dataService;
    private readonly IDialogService _dialogService;
    private readonly ISnackbar _snackbar;
    private readonly ISkillRegistry _skillRegistry;
    private readonly IClaudeService _claudeService;
    private readonly INotificationHistoryService _notificationHistory;

    public SessionListFacade(
        IChatState chatState,
        ISessionService sessionService,
        IActiveSessionRegistry activeSessionRegistry,
        IOptionsMonitor<AppSettings> appSettings,
        ISettingsService settingsService,
        SessionListDataService dataService,
        IDialogService dialogService,
        ISnackbar snackbar,
        ISkillRegistry skillRegistry,
        IClaudeService claudeService,
        INotificationHistoryService notificationHistory)
    {
        _chatState = chatState;
        _sessionService = sessionService;
        _activeSessionRegistry = activeSessionRegistry;
        _appSettings = appSettings;
        _settingsService = settingsService;
        _dataService = dataService;
        _dialogService = dialogService;
        _snackbar = snackbar;
        _skillRegistry = skillRegistry;
        _claudeService = claudeService;
        _notificationHistory = notificationHistory;
    }

    public Task<(Workspace? Workspace, Session? Session, string? ProjectName)> RestoreLastSelectionAsync(
        List<Workspace> workspaces,
        Dictionary<string, List<Session>> sessionCache)
    {
        var settings = _appSettings.CurrentValue;
        var workspace = workspaces.FirstOrDefault(w => w.Id == settings.LastWorkspaceId)
                        ?? workspaces.FirstOrDefault();

        if (workspace == null)
            return Task.FromResult<(Workspace?, Session?, string?)>((null, null, null));

        var projectName = SessionListDataService.GetProjectName(workspace);

        Session? lastSession = null;
        if (!string.IsNullOrEmpty(settings.LastSessionId)
            && sessionCache.TryGetValue(workspace.Id, out var cached))
        {
            lastSession = cached.FirstOrDefault(s => s.Id == settings.LastSessionId);
        }

        return Task.FromResult<(Workspace?, Session?, string?)>((workspace, lastSession, projectName));
    }

    public async Task<Session> CreateSessionAsync(Workspace ws, bool localDir = false)
    {
        // Workspace override > App default
        var model = !string.IsNullOrEmpty(ws.DefaultModel)
            ? ws.DefaultModel
            : _appSettings.CurrentValue.DefaultModel;

        var session = localDir
            ? await _sessionService.CreateLocalDirSessionAsync(model, ws.Id)
            : await _sessionService.CreatePendingSessionAsync(model, ws.Id);

        await SwitchWorkspaceAsync(ws);
        _chatState.SetSession(session);

        if (_dataService.SessionCache.TryGetValue(ws.Id, out var sessions))
            sessions.Insert(0, session);

        await SaveLastSelectionAsync(ws.Id, session.Id);

        _dataService.RebuildOrderedSessions();
        return session;
    }

    public async Task SelectSessionAsync(Session session, Workspace? ws, List<Workspace> workspaces)
    {
        ws ??= workspaces.FirstOrDefault(w => w.Id == session.WorkspaceId);
        if (ws != null)
            await SwitchWorkspaceAsync(ws);

        // LoadSessionAsync checks the active session registry first,
        // so we always get the authoritative in-memory instance if one exists.
        var fullSess = await _sessionService.LoadSessionAsync(session.Id);
        _chatState.SetSession(fullSess ?? session);
        _notificationHistory.MarkSessionAsRead(session.Id);

        await SaveLastSelectionAsync(session.WorkspaceId, session.Id);
    }

    public async Task CleanupSessionAsync(Session session)
    {
        await _sessionService.CleanupSessionAsync(session.Id);
        session.TransitionStatus(SessionStatus.Archived);
    }

    public async Task<bool> DeleteSessionAsync(Session session)
    {
        var isStreaming = _chatState.IsSessionStreaming(session.Id);
        var confirmMessage = isStreaming
            ? $"'{session.Title}' 세션이 현재 진행 중입니다. Claude 프로세스를 종료하고 삭제하시겠습니까?"
            : $"'{session.Title}' 세션을 삭제하시겠습니까?";

        var result = await _dialogService.ShowMessageBoxAsync(
            "세션 삭제", confirmMessage,
            yesText: "삭제", cancelText: "취소");

        if (result != true) return false;

        // Stop Claude process before deletion
        _claudeService.Cancel(session.Id);

        await _sessionService.DeleteSessionAsync(session.Id);
        _notificationHistory.MarkSessionAsRead(session.Id);

        if (_dataService.SessionCache.TryGetValue(session.WorkspaceId, out var sessions))
            sessions.RemoveAll(s => s.Id == session.Id);

        if (_chatState.CurrentSession?.Id == session.Id)
            _chatState.SetSession(null!);
        else
            _chatState.NotifyStateChanged(); // Refresh LandingPage recent chats list

        _dataService.DiffStatsCache.Remove(session.Id);
        _dataService.RebuildOrderedSessions();
        _snackbar.SessionDeleted();

        return true;
    }

    private async Task SwitchWorkspaceAsync(Workspace ws)
    {
        var previousId = _chatState.CurrentWorkspace?.Id;
        _chatState.SetWorkspace(ws);

        // Reload custom skills when workspace changes (different project path)
        if (previousId != ws.Id)
            await _skillRegistry.LoadCustomCommandsAsync(ws.RepoLocalPath);
    }

    private async Task SaveLastSelectionAsync(string workspaceId, string sessionId)
    {
        var settings = _appSettings.CurrentValue;
        settings.LastWorkspaceId = workspaceId;
        settings.LastSessionId = sessionId;
        await _settingsService.SaveAsync(settings);
    }
}
