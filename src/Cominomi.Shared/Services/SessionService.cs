using System.Text.Json;
using System.Text.RegularExpressions;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public partial class SessionService : ISessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IGitService _gitService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ISettingsService _settingsService;
    private readonly string _sessionsDir;

    public SessionService(IGitService gitService, IWorkspaceService workspaceService, ISettingsService settingsService)
    {
        _gitService = gitService;
        _workspaceService = workspaceService;
        _settingsService = settingsService;
        _sessionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cominomi", "sessions");
        Directory.CreateDirectory(_sessionsDir);
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
                        Model = ModelDefinitions.NormalizeModelId(session.Model),
                        WorkspaceId = session.WorkspaceId,
                        PermissionMode = session.PermissionMode,
                        Status = session.Status,
                        ErrorMessage = session.ErrorMessage,
                        CreatedAt = session.CreatedAt,
                        UpdatedAt = session.UpdatedAt
                    });
                }
            }
            catch
            {
                // skip corrupted files
            }
        }

        return sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    public async Task<List<Session>> GetSessionsByWorkspaceAsync(string workspaceId)
    {
        var all = await GetSessionsAsync();
        return all.Where(s => s.WorkspaceId == workspaceId).ToList();
    }

    public async Task<Session> CreateSessionAsync(string model, string workspaceId)
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
            Status = SessionStatus.Initializing
        };

        session.WorktreePath = Path.Combine(worktreesDir, session.Id);

        try
        {
            var result = await _gitService.AddWorktreeAsync(
                workspace.RepoLocalPath, session.WorktreePath, branchName, workspace.BaseBranch);

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
            session.Status = SessionStatus.Error;
            session.ErrorMessage = ex.Message;
        }

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
            session.Model = ModelDefinitions.NormalizeModelId(session.Model);
        return session;
    }

    public async Task SaveSessionAsync(Session session)
    {
        session.UpdatedAt = DateTime.UtcNow;

        // Auto-generate title from first user message
        if (session.Title == "New Chat" && session.Messages.Count > 0)
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

        // Remove worktree
        if (!string.IsNullOrEmpty(session.WorktreePath))
        {
            try
            {
                await _gitService.RemoveWorktreeAsync(workspace.RepoLocalPath, session.WorktreePath);
            }
            catch { }
        }

        // Delete branch
        if (!string.IsNullOrEmpty(session.BranchName))
        {
            try
            {
                await _gitService.DeleteBranchAsync(workspace.RepoLocalPath, session.BranchName);
            }
            catch { }
        }

        session.Status = SessionStatus.Archived;
        await SaveSessionAsync(session);
    }

    public async Task<bool> CheckMergeStatusAsync(string sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null || session.Status != SessionStatus.Ready)
            return false;

        var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
        if (workspace == null)
            return false;

        var isMerged = await _gitService.IsBranchMergedAsync(
            workspace.RepoLocalPath, session.BranchName, workspace.BaseBranch);

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
            try { await CleanupSessionAsync(sessionId); } catch { }
        }

        var path = Path.Combine(_sessionsDir, $"{sessionId}.json");
        if (File.Exists(path))
            File.Delete(path);
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
