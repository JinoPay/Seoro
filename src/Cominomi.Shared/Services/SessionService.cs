using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cominomi.Shared;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public partial class SessionService : ISessionService
{
    private readonly IGitService _gitService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ISettingsService _settingsService;
    private readonly IContextService _contextService;
    private readonly IHooksEngine _hooksEngine;
    private readonly ILogger<SessionService> _logger;
    private readonly string _sessionsDir = AppPaths.Sessions;
    private readonly string _archiveDir = AppPaths.ArchivedContexts;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();

    public SessionService(IGitService gitService, IWorkspaceService workspaceService,
        ISettingsService settingsService, IContextService contextService, IHooksEngine hooksEngine,
        ILogger<SessionService> logger)
    {
        _gitService = gitService;
        _workspaceService = workspaceService;
        _settingsService = settingsService;
        _contextService = contextService;
        _hooksEngine = hooksEngine;
        _logger = logger;
    }

    public async Task<List<Session>> GetSessionsAsync()
    {
        var sessions = new List<Session>();
        if (!Directory.Exists(_sessionsDir))
            return sessions;

        foreach (var file in Directory.GetFiles(_sessionsDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var session = JsonSerializer.Deserialize<Session>(json, JsonDefaults.Options);
                if (session != null)
                {
                    // Don't load full messages for the list view
                    sessions.Add(new Session
                    {
                        Id = session.Id,
                        Title = session.Title,
                        WorktreePath = session.WorktreePath,
                        BranchName = session.BranchName,
                        BaseBranch = session.BaseBranch,
                        Model = ModelDefinitions.NormalizeModelId(session.Model),
                        WorkspaceId = session.WorkspaceId,
                        PermissionMode = session.PermissionMode,
                        AgentType = session.AgentType,
                        IsLocalDir = session.IsLocalDir,
                        CityName = session.CityName,
                        Status = session.Status,
                        ErrorMessage = session.ErrorMessage,
                        PrUrl = session.PrUrl,
                        PrNumber = session.PrNumber,
                        IssueNumber = session.IssueNumber,
                        IssueUrl = session.IssueUrl,
                        ConflictFiles = session.ConflictFiles,
                        CreatedAt = session.CreatedAt,
                        UpdatedAt = session.UpdatedAt
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping corrupted session file: {File}", file);
            }
        }

        return sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    public async Task<List<Session>> GetSessionsByWorkspaceAsync(string workspaceId)
    {
        var all = await GetSessionsAsync();
        return all.Where(s => s.WorkspaceId == workspaceId).ToList();
    }

    public async Task<Session> CreateSessionAsync(string model, string workspaceId, string baseBranch)
    {
        var workspace = await _workspaceService.LoadWorkspaceAsync(workspaceId);
        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{workspaceId}' not found.");

        var branchName = $"{CominomiConstants.BranchPrefix}{DateTime.Now:yyyyMMdd-HHmmss}";
        var worktreesDir = await _workspaceService.GetWorktreesDirAsync();

        var session = new Session
        {
            Model = model,
            WorkspaceId = workspaceId,
            BranchName = branchName,
            BaseBranch = baseBranch,
            Status = SessionStatus.Initializing
        };

        session.WorktreePath = Path.Combine(worktreesDir, session.Id);

        try
        {
            var result = await _gitService.AddWorktreeAsync(
                workspace.RepoLocalPath, session.WorktreePath, branchName, baseBranch);

            if (!result.Success)
            {
                session.Status = SessionStatus.Error;
                session.ErrorMessage = result.Error;
            }
            else
            {
                session.Status = SessionStatus.Ready;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session worktree for workspace {WorkspaceId}", workspaceId);
            session.Status = SessionStatus.Error;
            session.ErrorMessage = ex.Message;
        }

        return session;
    }

    public async Task<Session> CreatePendingSessionAsync(string model, string workspaceId)
    {
        var workspace = await _workspaceService.LoadWorkspaceAsync(workspaceId);
        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{workspaceId}' not found.");

        var settings = await _settingsService.LoadAsync();
        var cityName = CityNames.GetRandom();
        var session = new Session
        {
            Model = model,
            WorkspaceId = workspaceId,
            CityName = cityName,
            Title = cityName,
            Status = SessionStatus.Pending,
            EffortLevel = settings.DefaultEffortLevel,
            PermissionMode = settings.DefaultPermissionMode
        };

        await SaveSessionAsync(session);
        _logger.LogInformation("Created session {SessionId} ({CityName}) in workspace {WorkspaceId}", session.Id, cityName, workspaceId);

        await _hooksEngine.FireAsync(HookEvent.OnSessionCreate, new Dictionary<string, string>
        {
            ["COMINOMI_SESSION_ID"] = session.Id,
            ["COMINOMI_CITY_NAME"] = cityName,
            ["COMINOMI_WORKSPACE_ID"] = workspaceId
        });

        return session;
    }

    public async Task<Session> CreateLocalDirSessionAsync(string model, string workspaceId)
    {
        var workspace = await _workspaceService.LoadWorkspaceAsync(workspaceId);
        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{workspaceId}' not found.");

        var settings = await _settingsService.LoadAsync();
        var cityName = CityNames.GetRandom();
        var session = new Session
        {
            Model = model,
            WorkspaceId = workspaceId,
            CityName = cityName,
            Title = cityName,
            IsLocalDir = true,
            Status = SessionStatus.Ready,
            WorktreePath = workspace.RepoLocalPath,
            EffortLevel = settings.DefaultEffortLevel,
            PermissionMode = settings.DefaultPermissionMode
        };

        await SaveSessionAsync(session);
        _logger.LogInformation("Created local-dir session {SessionId} ({CityName}) in workspace {WorkspaceId}", session.Id, cityName, workspaceId);

        await _hooksEngine.FireAsync(HookEvent.OnSessionCreate, new Dictionary<string, string>
        {
            ["COMINOMI_SESSION_ID"] = session.Id,
            ["COMINOMI_CITY_NAME"] = cityName,
            ["COMINOMI_WORKSPACE_ID"] = workspaceId
        });

        return session;
    }

    public async Task<Session> InitializeWorktreeAsync(string sessionId, string baseBranch)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Session '{sessionId}' not found.");

        var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{session.WorkspaceId}' not found.");

        var branchName = $"{CominomiConstants.BranchPrefix}{DateTime.Now:yyyyMMdd-HHmmss}";
        var worktreesDir = await _workspaceService.GetWorktreesDirAsync();

        session.BranchName = branchName;
        session.BaseBranch = baseBranch;
        session.WorktreePath = Path.Combine(worktreesDir, session.Id);
        session.Status = SessionStatus.Initializing;

        try
        {
            var result = await _gitService.AddWorktreeAsync(
                workspace.RepoLocalPath, session.WorktreePath, branchName, baseBranch);

            if (!result.Success)
            {
                session.Status = SessionStatus.Error;
                session.ErrorMessage = result.Error;
            }
            else
            {
                session.Status = SessionStatus.Ready;
                // Initialize .context/ directory for collaboration
                await _contextService.EnsureContextDirectoryAsync(session.WorktreePath);
                _logger.LogInformation("Worktree initialized for session {SessionId} on branch {Branch}", sessionId, branchName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize worktree for session {SessionId}", sessionId);
            session.Status = SessionStatus.Error;
            session.ErrorMessage = ex.Message;
        }

        await SaveSessionAsync(session);
        return session;
    }

    public async Task<Session?> LoadSessionAsync(string sessionId)
    {
        var path = Path.Combine(_sessionsDir, $"{sessionId}.json");
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path);
        var session = JsonSerializer.Deserialize<Session>(json, JsonDefaults.Options);
        if (session != null)
        {
            session.Model = ModelDefinitions.NormalizeModelId(session.Model);
            // Migrate old messages that only have Text/ToolCalls to Parts
            foreach (var msg in session.Messages)
                msg.MigrateToParts();
        }
        return session;
    }

    public async Task SaveSessionAsync(Session session)
    {
        var semaphore = _sessionLocks.GetOrAdd(session.Id, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            session.UpdatedAt = DateTime.UtcNow;

            // Fallback title: only if title is still the initial city name (Haiku summary not yet applied)
            if (session.Title == session.CityName && session.Messages.Count > 0)
            {
                var firstMessage = session.Messages.FirstOrDefault(m => m.Role == MessageRole.User);
                if (firstMessage != null)
                {
                    session.Title = firstMessage.Text.Length > 50
                        ? firstMessage.Text[..50] + "..."
                        : firstMessage.Text;
                }
            }

            var path = Path.Combine(_sessionsDir, $"{session.Id}.json");
            var json = JsonSerializer.Serialize(session, JsonDefaults.Options);
            await AtomicFileWriter.WriteAsync(path, json);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task RenameBranchAsync(string sessionId, string newBranchName)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null || session.Status != SessionStatus.Ready)
            return;

        var oldBranch = session.BranchName;
        if (oldBranch == newBranchName)
            return;

        var result = await _gitService.RenameBranchAsync(session.WorktreePath, oldBranch, newBranchName);
        if (result.Success)
        {
            session.BranchName = newBranchName;
            await SaveSessionAsync(session);
        }
    }

    public async Task CleanupSessionAsync(string sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null)
            return;

        var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
        if (workspace == null)
            return;

        // Archive .context/ before removing worktree
        if (!string.IsNullOrEmpty(session.WorktreePath) && Directory.Exists(session.WorktreePath))
        {
            try
            {
                var archiveName = !string.IsNullOrEmpty(session.CityName) ? session.CityName : session.Id;
                var archivePath = Path.Combine(_archiveDir, workspace.Name, archiveName);
                await _contextService.ArchiveContextAsync(session.WorktreePath, archivePath);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to archive context for session {SessionId}", sessionId); }
        }

        // Skip worktree/branch cleanup for local-dir sessions (the directory is the user's real repo)
        if (!session.IsLocalDir)
        {
            // Remove worktree
            if (!string.IsNullOrEmpty(session.WorktreePath))
            {
                try
                {
                    await _gitService.RemoveWorktreeAsync(workspace.RepoLocalPath, session.WorktreePath);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to remove worktree for session {SessionId}", sessionId); }
            }

            // Delete branch
            if (!string.IsNullOrEmpty(session.BranchName))
            {
                try
                {
                    await _gitService.DeleteBranchAsync(workspace.RepoLocalPath, session.BranchName);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete branch {Branch} for session {SessionId}", session.BranchName, sessionId); }
            }
        }

        session.Status = SessionStatus.Archived;
        await SaveSessionAsync(session);
        _logger.LogInformation("Session {SessionId} archived", sessionId);

        await _hooksEngine.FireAsync(HookEvent.OnSessionArchive, new Dictionary<string, string>
        {
            ["COMINOMI_SESSION_ID"] = session.Id,
            ["COMINOMI_CITY_NAME"] = session.CityName
        });
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        // Clean up worktree/branch before deleting
        var session = await LoadSessionAsync(sessionId);
        if (session != null && session.Status is SessionStatus.Ready or SessionStatus.Merged)
        {
            try { await CleanupSessionAsync(sessionId); }
            catch (Exception ex) { _logger.LogWarning(ex, "Cleanup failed during session delete: {SessionId}", sessionId); }
        }

        var path = Path.Combine(_sessionsDir, $"{sessionId}.json");
        if (File.Exists(path))
            File.Delete(path);

        _logger.LogInformation("Session {SessionId} deleted", sessionId);
    }

    public static string GenerateBranchName(string message)
    {
        var slug = message.ToLowerInvariant().Trim();
        // Replace whitespace with hyphens
        slug = WhitespaceRegex().Replace(slug, "-");
        // Remove non-alphanumeric except hyphens
        slug = NonSlugRegex().Replace(slug, "");
        // Collapse multiple hyphens
        slug = MultiHyphenRegex().Replace(slug, "-");
        // Trim hyphens
        slug = slug.Trim('-');
        // Truncate
        if (slug.Length > 40)
            slug = slug[..40].TrimEnd('-');

        return $"{CominomiConstants.BranchPrefix}{(string.IsNullOrEmpty(slug) ? DateTime.Now.ToString("yyyyMMdd-HHmmss") : slug)}";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex NonSlugRegex();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultiHyphenRegex();
}
