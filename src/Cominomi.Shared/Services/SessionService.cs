using System.Text.Json;
using System.Text.RegularExpressions;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public partial class SessionService : ISessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IGitService _gitService;
    private readonly IGhService _ghService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ISettingsService _settingsService;
    private readonly IContextService _contextService;
    private readonly IHooksEngine _hooksEngine;
    private readonly ILogger<SessionService> _logger;
    private readonly string _sessionsDir;
    private readonly string _archiveDir;

    public SessionService(IGitService gitService, IGhService ghService, IWorkspaceService workspaceService,
        ISettingsService settingsService, IContextService contextService, IHooksEngine hooksEngine,
        ILogger<SessionService> logger)
    {
        _gitService = gitService;
        _ghService = ghService;
        _workspaceService = workspaceService;
        _settingsService = settingsService;
        _contextService = contextService;
        _hooksEngine = hooksEngine;
        _logger = logger;
        _sessionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cominomi", "sessions");
        _archiveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cominomi", "archived-contexts");
        Directory.CreateDirectory(_sessionsDir);
        Directory.CreateDirectory(_archiveDir);
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
                var session = JsonSerializer.Deserialize<Session>(json, JsonOptions);
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
                        CityName = session.CityName,
                        Status = session.Status,
                        ErrorMessage = session.ErrorMessage,
                        PrUrl = session.PrUrl,
                        PrNumber = session.PrNumber,
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

        var branchName = $"cominomi/{DateTime.Now:yyyyMMdd-HHmmss}";
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

    public async Task<Session> InitializeWorktreeAsync(string sessionId, string baseBranch)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Session '{sessionId}' not found.");

        var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{session.WorkspaceId}' not found.");

        var branchName = $"cominomi/{DateTime.Now:yyyyMMdd-HHmmss}";
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
        var session = JsonSerializer.Deserialize<Session>(json, JsonOptions);
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
        var json = JsonSerializer.Serialize(session, JsonOptions);
        await File.WriteAllTextAsync(path, json);
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

        session.Status = SessionStatus.Archived;
        await SaveSessionAsync(session);
        _logger.LogInformation("Session {SessionId} archived", sessionId);

        await _hooksEngine.FireAsync(HookEvent.OnSessionArchive, new Dictionary<string, string>
        {
            ["COMINOMI_SESSION_ID"] = session.Id,
            ["COMINOMI_CITY_NAME"] = session.CityName
        });
    }

    public async Task<bool> CheckMergeStatusAsync(string sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null || session.Status != SessionStatus.Ready)
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
            await SaveSessionAsync(session);
        }

        return isMerged;
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

            _ = _hooksEngine.FireAsync(HookEvent.OnBranchPush, new Dictionary<string, string>
            {
                ["COMINOMI_SESSION_ID"] = session.Id,
                ["COMINOMI_BRANCH"] = session.BranchName
            });
        }
        else
        {
            session.ErrorMessage = result.Error;
        }

        await SaveSessionAsync(session);
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

        await SaveSessionAsync(session);
        return session;
    }

    public async Task<Session> MergePrAsync(string sessionId, string mergeMethod = "squash", CancellationToken ct = default)
    {
        var (session, workspace) = await LoadSessionAndWorkspaceAsync(sessionId);

        if (session.PrNumber == null)
        {
            // Try to find PR by branch name
            var prInfo = await _ghService.GetPrForBranchAsync(workspace.RepoLocalPath, session.BranchName, ct);
            if (prInfo == null)
            {
                session.ErrorMessage = "PR not found for this branch.";
                await SaveSessionAsync(session);
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

        await SaveSessionAsync(session);
        return session;
    }

    public async Task RetryAfterConflictResolveAsync(string sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Session '{sessionId}' not found.");

        session.Status = SessionStatus.Ready;
        session.ErrorMessage = null;
        session.ConflictFiles = null;
        await SaveSessionAsync(session);
    }

    private async Task<(Session session, Workspace workspace)> LoadSessionAndWorkspaceAsync(string sessionId)
    {
        var session = await LoadSessionAsync(sessionId)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found.");
        var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId)
            ?? throw new InvalidOperationException($"Workspace '{session.WorkspaceId}' not found.");
        return (session, workspace);
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

        return $"cominomi/{(string.IsNullOrEmpty(slug) ? DateTime.Now.ToString("yyyyMMdd-HHmmss") : slug)}";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex NonSlugRegex();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultiHyphenRegex();
}
