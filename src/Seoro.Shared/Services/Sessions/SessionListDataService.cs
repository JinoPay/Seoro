using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Sessions;

public class SessionListDataService : IDisposable
{
    private readonly IGitService _gitService;
    private readonly ILogger<SessionListDataService> _logger;
    private readonly ISessionService _sessionService;
    private readonly IWorkspaceService _workspaceService;

    public SessionListDataService(
        ISessionService sessionService,
        IGitService gitService, IWorkspaceService workspaceService,
        ILogger<SessionListDataService> logger)
    {
        _sessionService = sessionService;
        _gitService = gitService;
        _workspaceService = workspaceService;
        _logger = logger;

        _workspaceService.OnWorkspaceSaved += HandleWorkspaceSaved;
    }

    public Dictionary<string, (int Additions, int Deletions)> DiffStatsCache { get; } = new();

    public Dictionary<string, List<Session>> SessionCache { get; } = new();
    public List<(Session Session, Workspace Workspace)> OrderedSessions { get; } = [];
    public List<Workspace> Workspaces { get; private set; } = [];

    public event Action? OnDataChanged;

    public void Dispose()
    {
        _workspaceService.OnWorkspaceSaved -= HandleWorkspaceSaved;
        OnDataChanged = null;
    }

    public static IEnumerable<Session> FilterSessions(List<Session> sessions, string? filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            return sessions;

        var filter = filterText.Trim();
        return sessions.Where(s =>
            s.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || s.Git.BranchName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || s.CityName.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetProjectName(Workspace ws)
    {
        if (!string.IsNullOrEmpty(ws.Name))
            return ws.Name;

        if (!string.IsNullOrEmpty(ws.RepoUrl))
        {
            var url = ws.RepoUrl.TrimEnd('/');
            if (url.EndsWith(".git"))
                url = url[..^4];

            var colonIdx = url.LastIndexOf(':');
            var slashIdx = url.LastIndexOf('/');
            if (slashIdx >= 0)
                return url[(slashIdx + 1)..];
            if (colonIdx >= 0)
                return url[(colonIdx + 1)..];
        }

        if (!string.IsNullOrEmpty(ws.RepoLocalPath))
            return Path.GetFileName(ws.RepoLocalPath.TrimEnd(Path.DirectorySeparatorChar, '/'));

        return ws.Id;
    }

    public IEnumerable<Workspace> GetFilteredWorkspaces(string? filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            return Workspaces;

        var filter = filterText.Trim();
        return Workspaces.Where(w =>
        {
            if (GetProjectName(w).Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
            if (w.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
            if (SessionCache.TryGetValue(w.Id, out var sessions))
                return sessions.Any(s => s.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                         || s.Git.BranchName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                         || s.CityName.Contains(filter, StringComparison.OrdinalIgnoreCase));
            return false;
        });
    }

    public async Task LoadAllSessionCountsAsync()
    {
        foreach (var ws in Workspaces)
            if (!SessionCache.ContainsKey(ws.Id))
                try
                {
                    var sessions = await _sessionService.GetSessionsByWorkspaceAsync(ws.Id);
                    SessionCache[ws.Id] = sessions;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to pre-load sessions for workspace {WorkspaceId}", ws.Id);
                }

        RebuildOrderedSessions();
        OnDataChanged?.Invoke();
    }

    public async Task LoadDiffStatsForWorkspaceAsync(Workspace ws, List<Session> sessions)
    {
        foreach (var session in sessions)
        {
            if (session.Status == SessionStatus.Pending || session.Git.IsLocalDir
                                                        || string.IsNullOrEmpty(session.Git.WorktreePath)
                                                        || !Directory.Exists(session.Git.WorktreePath)
                                                        || string.IsNullOrEmpty(session.Git.BaseBranch))
                continue;

            try
            {
                var stats = await _gitService.GetDiffStatAsync(session.Git.WorktreePath, session.Git.GetDiffBase());
                if (stats.Additions > 0 || stats.Deletions > 0)
                {
                    DiffStatsCache[session.Id] = stats;
                    OnDataChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to load diff stats for session {SessionId}", session.Id);
            }
        }
    }

    public async Task LoadSessionsForProjectAsync(string projectName)
    {
        var workspacesInGroup = Workspaces.Where(w => GetProjectName(w) == projectName);
        foreach (var ws in workspacesInGroup)
            if (!SessionCache.ContainsKey(ws.Id))
            {
                var sessions = await _sessionService.GetSessionsByWorkspaceAsync(ws.Id);
                SessionCache[ws.Id] = sessions;
                _ = LoadDiffStatsForWorkspaceAsync(ws, sessions);
            }

        RebuildOrderedSessions();
    }

    public async Task LoadWorkspacesAsync()
    {
        Workspaces = await _workspaceService.GetWorkspacesAsync();
    }

    public async Task RefreshDiffStatsAsync(string sessionId)
    {
        var session = OrderedSessions.FirstOrDefault(o => o.Session.Id == sessionId).Session;
        if (session == null || session.Git.IsLocalDir
                            || string.IsNullOrEmpty(session.Git.WorktreePath)
                            || !Directory.Exists(session.Git.WorktreePath)
                            || string.IsNullOrEmpty(session.Git.BaseBranch))
            return;

        try
        {
            var stats = await _gitService.GetDiffStatAsync(session.Git.WorktreePath, session.Git.GetDiffBase());
            DiffStatsCache[sessionId] = stats;
            OnDataChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh diff stats for session {SessionId}", sessionId);
        }
    }

    public async Task RefreshWorkspacesAsync()
    {
        Workspaces = await _workspaceService.GetWorkspacesAsync();
        var validIds = new HashSet<string>(Workspaces.Select(w => w.Id));
        var staleKeys = SessionCache.Keys.Where(k => !validIds.Contains(k)).ToList();
        foreach (var key in staleKeys) SessionCache.Remove(key);
        RebuildOrderedSessions();
        OnDataChanged?.Invoke();
    }

    public async Task ReloadWorkspacesAsync(IEnumerable<string> expandedProjects)
    {
        Workspaces = await _workspaceService.GetWorkspacesAsync();
        foreach (var projectName in expandedProjects)
        {
            var workspacesInGroup = Workspaces.Where(w => GetProjectName(w) == projectName);
            foreach (var ws in workspacesInGroup)
            {
                var sessions = await _sessionService.GetSessionsByWorkspaceAsync(ws.Id);
                SessionCache[ws.Id] = sessions;
            }
        }

        RebuildOrderedSessions();
    }

    public void RebuildOrderedSessions()
    {
        OrderedSessions.Clear();
        foreach (var group in Workspaces.GroupBy(GetProjectName))
        {
            var workspacesInGroup = group.OrderBy(w => w.SortIndex).ThenByDescending(w => w.UpdatedAt).ToList();
            foreach (var ws in workspacesInGroup)
                if (SessionCache.TryGetValue(ws.Id, out var sessions))
                    foreach (var s in sessions)
                        OrderedSessions.Add((s, ws));
        }
    }

    public async Task ReorderProjectGroupsAsync(List<string> projectNameOrder)
    {
        // Workspaces 스냅샷 — SaveWorkspaceAsync가 HandleWorkspaceSaved를 트리거하여
        // 열거 중 컬렉션이 변경되는 것을 방지
        var snapshot = Workspaces.ToList();
        var index = 0;
        foreach (var projectName in projectNameOrder)
        {
            var workspacesInGroup = snapshot.Where(w => GetProjectName(w) == projectName).ToList();
            foreach (var ws in workspacesInGroup)
            {
                ws.SortIndex = index;
                await _workspaceService.SaveWorkspaceAsync(ws);
            }

            index++;
        }

        Workspaces = Workspaces.OrderBy(w => w.SortIndex).ThenByDescending(w => w.UpdatedAt).ToList();
        RebuildOrderedSessions();
        OnDataChanged?.Invoke();
    }

    private void HandleWorkspaceSaved(Workspace updated)
    {
        var index = Workspaces.FindIndex(w => w.Id == updated.Id);
        if (index >= 0)
        {
            Workspaces[index] = updated;
            RebuildOrderedSessions();
            OnDataChanged?.Invoke();
        }
    }
}