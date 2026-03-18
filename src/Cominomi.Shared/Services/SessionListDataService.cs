using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class SessionListDataService : IDisposable
{
    private readonly ISessionService _sessionService;
    private readonly ISessionGitWorkflowService _gitWorkflow;
    private readonly IGitService _gitService;
    private readonly IGhService _ghService;
    private readonly IWorkspaceService _workspaceService;

    public Dictionary<string, List<Session>> SessionCache { get; } = new();
    public Dictionary<string, (int Additions, int Deletions)> DiffStatsCache { get; } = new();
    public List<(Session Session, Workspace Workspace)> OrderedSessions { get; } = [];
    public List<Workspace> Workspaces { get; private set; } = [];

    public event Action? OnDataChanged;

    public SessionListDataService(
        ISessionService sessionService, ISessionGitWorkflowService gitWorkflow,
        IGitService gitService, IGhService ghService, IWorkspaceService workspaceService)
    {
        _sessionService = sessionService;
        _gitWorkflow = gitWorkflow;
        _gitService = gitService;
        _ghService = ghService;
        _workspaceService = workspaceService;
    }

    public async Task LoadWorkspacesAsync()
    {
        Workspaces = await _workspaceService.GetWorkspacesAsync();
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

    public async Task LoadSessionsForProjectAsync(string projectName)
    {
        var workspacesInGroup = Workspaces.Where(w => GetProjectName(w) == projectName);
        foreach (var ws in workspacesInGroup)
        {
            if (!SessionCache.ContainsKey(ws.Id))
            {
                var sessions = await _sessionService.GetSessionsByWorkspaceAsync(ws.Id);
                SessionCache[ws.Id] = sessions;
                _ = CheckMergeStatusesAsync(ws.Id);
                _ = LoadDiffStatsForWorkspaceAsync(ws, sessions);
            }
        }
        RebuildOrderedSessions();
    }

    public async Task LoadDiffStatsForWorkspaceAsync(Workspace ws, List<Session> sessions)
    {
        foreach (var session in sessions)
        {
            if (session.Status == SessionStatus.Pending || session.IsLocalDir
                || string.IsNullOrEmpty(session.WorktreePath) || string.IsNullOrEmpty(session.BaseBranch))
                continue;

            try
            {
                var stats = await _gitService.GetDiffStatAsync(session.WorktreePath, session.BaseBranch);
                if (stats.Additions > 0 || stats.Deletions > 0)
                {
                    DiffStatsCache[session.Id] = stats;
                    OnDataChanged?.Invoke();
                }
            }
            catch { }
        }
    }

    public async Task RefreshDiffStatsAsync(string sessionId)
    {
        var session = OrderedSessions.FirstOrDefault(o => o.Session.Id == sessionId).Session;
        if (session == null || session.IsLocalDir
            || string.IsNullOrEmpty(session.WorktreePath) || string.IsNullOrEmpty(session.BaseBranch))
            return;

        try
        {
            var stats = await _gitService.GetDiffStatAsync(session.WorktreePath, session.BaseBranch);
            DiffStatsCache[sessionId] = stats;
            OnDataChanged?.Invoke();
        }
        catch { }
    }

    public async Task CheckMergeStatusesAsync(string workspaceId)
    {
        if (!SessionCache.TryGetValue(workspaceId, out var sessions)) return;

        var targetSessions = sessions
            .Where(s => s.Status is SessionStatus.Ready or SessionStatus.Pushed)
            .ToList();

        foreach (var session in targetSessions)
        {
            try
            {
                if (session.Status == SessionStatus.Ready)
                {
                    var merged = await _gitWorkflow.CheckMergeStatusAsync(session.Id);
                    if (merged)
                    {
                        session.TransitionStatus(SessionStatus.Merged);
                        OnDataChanged?.Invoke();
                        continue;
                    }
                }

                if (session.PrNumber == null && !session.IsLocalDir
                    && !string.IsNullOrEmpty(session.BranchName))
                {
                    await CheckAndUpdatePrForSessionAsync(session);
                }
            }
            catch { }
        }
    }

    public async Task CheckAndUpdatePrForSessionAsync(Session session)
    {
        var workspace = Workspaces.FirstOrDefault(w => w.Id == session.WorkspaceId);
        if (workspace == null) return;

        var prInfo = await _ghService.GetPrForBranchAsync(workspace.RepoLocalPath, session.BranchName);
        if (prInfo != null && prInfo.State is "OPEN" or "open")
        {
            session.TransitionStatus(SessionStatus.PrOpen);
            session.PrUrl = prInfo.Url;
            session.PrNumber = prInfo.Number;
            await _sessionService.SaveSessionAsync(session);
            OnDataChanged?.Invoke();
        }
    }

    public void RebuildOrderedSessions()
    {
        OrderedSessions.Clear();
        foreach (var group in Workspaces.GroupBy(GetProjectName))
        {
            var workspacesInGroup = group.OrderByDescending(w => w.UpdatedAt).ToList();
            foreach (var ws in workspacesInGroup)
            {
                if (SessionCache.TryGetValue(ws.Id, out var sessions))
                {
                    foreach (var s in sessions)
                        OrderedSessions.Add((s, ws));
                }
            }
        }
    }

    public static string GetProjectName(Workspace ws)
    {
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

        return ws.Name;
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
                    || s.BranchName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || s.CityName.Contains(filter, StringComparison.OrdinalIgnoreCase));
            return false;
        });
    }

    public static IEnumerable<Session> FilterSessions(List<Session> sessions, string? filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            return sessions;

        var filter = filterText.Trim();
        return sessions.Where(s =>
            s.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || s.BranchName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || s.CityName.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        OnDataChanged = null;
    }
}
