using Cominomi.Shared.Models;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace Cominomi.Shared.Services;

public class SessionListFacade : ISessionListFacade
{
    private readonly IChatState _chatState;
    private readonly ISessionService _sessionService;
    private readonly IOptionsMonitor<AppSettings> _appSettings;
    private readonly ISettingsService _settingsService;
    private readonly SessionListDataService _dataService;
    private readonly IDialogService _dialogService;
    private readonly ISnackbar _snackbar;
    private readonly ISkillRegistry _skillRegistry;

    public SessionListFacade(
        IChatState chatState,
        ISessionService sessionService,
        IOptionsMonitor<AppSettings> appSettings,
        ISettingsService settingsService,
        SessionListDataService dataService,
        IDialogService dialogService,
        ISnackbar snackbar,
        ISkillRegistry skillRegistry)
    {
        _chatState = chatState;
        _sessionService = sessionService;
        _appSettings = appSettings;
        _settingsService = settingsService;
        _dataService = dataService;
        _dialogService = dialogService;
        _snackbar = snackbar;
        _skillRegistry = skillRegistry;
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
        var session = localDir
            ? await _sessionService.CreateLocalDirSessionAsync(ws.DefaultModel, ws.Id)
            : await _sessionService.CreatePendingSessionAsync(ws.DefaultModel, ws.Id);

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

        var activeSession = _chatState.GetActiveSession(session.Id);
        if (activeSession != null)
        {
            _chatState.SetSession(activeSession);
        }
        else
        {
            var fullSess = await _sessionService.LoadSessionAsync(session.Id);
            _chatState.SetSession(fullSess ?? session);
        }

        await SaveLastSelectionAsync(session.WorkspaceId, session.Id);
    }

    public async Task CleanupSessionAsync(Session session)
    {
        await _sessionService.CleanupSessionAsync(session.Id);
        session.TransitionStatus(SessionStatus.Archived);
    }

    public async Task<bool> DeleteSessionAsync(Session session)
    {
        var result = await _dialogService.ShowMessageBoxAsync(
            "세션 삭제",
            $"'{session.Title}' 세션을 삭제하시겠습니까?",
            yesText: "삭제", cancelText: "취소");

        if (result != true) return false;

        await _sessionService.DeleteSessionAsync(session.Id);

        if (_dataService.SessionCache.TryGetValue(session.WorkspaceId, out var sessions))
            sessions.Remove(session);

        if (_chatState.CurrentSession?.Id == session.Id)
            _chatState.SetSession(null!);

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
