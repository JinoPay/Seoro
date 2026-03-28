using System.Globalization;
using System.Text.RegularExpressions;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public interface IGitBranchWatcherService : IDisposable
{
    void Watch(Session session);
    void Unwatch();
    Task RefreshBranchAsync(Session session);
}

public partial class GitBranchWatcherService : IGitBranchWatcherService
{
    private readonly IChatState _chatState;
    private readonly IGitService _gitService;
    private readonly ILogger<GitBranchWatcherService> _logger;
    private readonly IDisposable _sessionChangeSub;

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private Session? _watchedSession;
    private const int DebounceMs = 200;

    public GitBranchWatcherService(
        IChatState chatState,
        IChatEventBus eventBus,
        IGitService gitService,
        ILogger<GitBranchWatcherService> logger)
    {
        _chatState = chatState;
        _gitService = gitService;
        _logger = logger;

        _sessionChangeSub = eventBus.Subscribe<SessionChangedEvent>(evt =>
        {
            if (evt.NewSession != null)
                Watch(evt.NewSession);
            else
                Unwatch();
        });
    }

    public void Watch(Session session)
    {
        Unwatch();

        var workDir = session.Git.WorktreePath;
        if (string.IsNullOrEmpty(workDir))
            return;

        var gitDir = ResolveGitDir(workDir);
        if (gitDir == null)
            return;

        var headPath = Path.Combine(gitDir, "HEAD");
        if (!File.Exists(headPath))
            return;

        _watchedSession = session;

        // Read initial branch from HEAD file
        UpdateBranchFromHeadFile(headPath, session);

        try
        {
            _watcher = new FileSystemWatcher(gitDir, "HEAD")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnHeadChanged;

            _logger.LogDebug("Watching git HEAD at {GitDir} for session {SessionId}", gitDir, session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize git HEAD watcher for {GitDir}", gitDir);
        }
    }

    public void Unwatch()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        if (_watcher != null)
        {
            _watcher.Changed -= OnHeadChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        _watchedSession = null;
    }

    public async Task RefreshBranchAsync(Session session)
    {
        var workDir = session.Git.WorktreePath;
        if (string.IsNullOrEmpty(workDir))
            return;

        try
        {
            var branch = await _gitService.GetCurrentBranchAsync(workDir);
            if (!string.IsNullOrEmpty(branch) && branch != session.Git.BranchName)
            {
                session.Git.BranchName = branch;
                ApplyDerivedTitle(session, branch);
                _chatState.NotifyStateChanged();
                _logger.LogDebug("Branch refreshed to {Branch} for session {SessionId}", branch, session.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh branch for session {SessionId}", session.Id);
        }
    }

    private void OnHeadChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: git operations can write HEAD multiple times in quick succession
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ =>
        {
            var session = _watchedSession;
            if (session == null)
                return;

            UpdateBranchFromHeadFile(e.FullPath, session);
        }, null, DebounceMs, Timeout.Infinite);
    }

    private void UpdateBranchFromHeadFile(string headPath, Session session)
    {
        try
        {
            var content = File.ReadAllText(headPath).Trim();
            string? branch;

            if (content.StartsWith("ref: refs/heads/"))
                branch = content["ref: refs/heads/".Length..];
            else if (content.Length >= 7)
                branch = content[..7]; // detached HEAD → short SHA
            else
                return;

            if (!string.IsNullOrEmpty(branch) && branch != session.Git.BranchName)
            {
                session.Git.BranchName = branch;
                ApplyDerivedTitle(session, branch);
                _chatState.NotifyStateChanged();
                _logger.LogDebug("Branch changed to {Branch} for session {SessionId}", branch, session.Id);
            }
        }
        catch (IOException)
        {
            // File may be locked by git, will catch on next event
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read HEAD file at {HeadPath}", headPath);
        }
    }

    private void ApplyDerivedTitle(Session session, string branch)
    {
        var title = DeriveTitleFromBranch(branch);
        if (title != null)
        {
            session.Title = title;
            _chatState.Tabs.UpdateChatTabTitle(title);
        }
    }

    internal static string? DeriveTitleFromBranch(string branch)
    {
        var suffix = branch.StartsWith(CominomiConstants.BranchPrefix)
            ? branch[CominomiConstants.BranchPrefix.Length..]
            : branch;

        if (string.IsNullOrEmpty(suffix) || TimestampBranchRegex().IsMatch(suffix))
            return null;

        var title = suffix.Replace('-', ' ').Trim();
        if (string.IsNullOrEmpty(title))
            return null;

        if (title.All(c => c <= 127))
            title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title);

        return title;
    }

    [GeneratedRegex(@"^\d{8}-\d{6}$")]
    private static partial Regex TimestampBranchRegex();

    private static string? ResolveGitDir(string worktreePath)
    {
        var dotGit = Path.Combine(worktreePath, ".git");

        if (Directory.Exists(dotGit))
            return dotGit;

        if (File.Exists(dotGit))
        {
            // Worktree: .git is a file containing "gitdir: <path>"
            try
            {
                var content = File.ReadAllText(dotGit).Trim();
                if (content.StartsWith("gitdir: "))
                {
                    var gitdir = content["gitdir: ".Length..].Trim();
                    if (!Path.IsPathRooted(gitdir))
                        gitdir = Path.GetFullPath(Path.Combine(worktreePath, gitdir));
                    return Directory.Exists(gitdir) ? gitdir : null;
                }
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public void Dispose()
    {
        Unwatch();
        _sessionChangeSub.Dispose();
    }
}
