using System.Collections.Concurrent;
using System.Text.Json;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SpotlightService : ISpotlightService, IDisposable
{
    private readonly IGitService _gitService;
    private readonly ILogger<SpotlightService> _logger;
    private readonly ConcurrentDictionary<string, SpotlightSession> _sessions = new();
    private static readonly string StateFilePath = Path.Combine(AppPaths.Settings, "spotlight-state.json");
    private const int ThrottleMs = 500;

    public SpotlightService(IGitService gitService, ILogger<SpotlightService> logger)
    {
        _gitService = gitService;
        _logger = logger;
    }

    public bool IsActive(string sessionId) => _sessions.ContainsKey(sessionId);

    public string? GetSpotlightPath(string sessionId)
        => _sessions.TryGetValue(sessionId, out var s) ? s.SpotlightWorktreePath : null;

    public async Task RecoverAsync()
    {
        if (!File.Exists(StateFilePath))
            return;

        SpotlightPersistedState? state = null;
        try
        {
            var json = await File.ReadAllTextAsync(StateFilePath);
            state = JsonSerializer.Deserialize<SpotlightPersistedState>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read spotlight state file, deleting");
            TryDeleteStateFile();
            return;
        }

        if (state == null || string.IsNullOrEmpty(state.RepoDir) || string.IsNullOrEmpty(state.SpotlightWorktreePath))
        {
            TryDeleteStateFile();
            return;
        }

        _logger.LogWarning("Recovering from spotlight crash for session {SessionId}", state.SessionId);

        try
        {
            // Remove the spotlight worktree if it still exists
            if (Directory.Exists(state.SpotlightWorktreePath))
            {
                await RunGitAsync($"worktree remove \"{state.SpotlightWorktreePath}\" --force", state.RepoDir);
            }

            _logger.LogInformation("Spotlight recovery completed for session {SessionId}", state.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Spotlight recovery failed for session {SessionId}", state.SessionId);
            // Force-clean the directory if git worktree remove failed
            try
            {
                if (Directory.Exists(state.SpotlightWorktreePath))
                    Directory.Delete(state.SpotlightWorktreePath, recursive: true);
                await RunGitAsync("worktree prune", state.RepoDir);
            }
            catch (Exception cleanEx)
            {
                _logger.LogDebug(cleanEx, "Spotlight recovery cleanup also failed");
            }
        }
        finally
        {
            TryDeleteStateFile();
        }
    }

    public async Task StartAsync(Workspace workspace, Session session)
    {
        if (_sessions.ContainsKey(session.Id))
            return;

        // Guard: only one Spotlight at a time
        if (!_sessions.IsEmpty)
        {
            var existingId = _sessions.Keys.First();
            _logger.LogWarning("Stopping existing spotlight {ExistingId} before starting {NewId}", existingId, session.Id);
            await StopAsync(existingId);
        }

        if (session.Git.IsLocalDir || string.IsNullOrEmpty(session.Git.BranchName))
            throw new InvalidOperationException("Spotlight is not supported for local directory sessions.");

        var repoDir = workspace.RepoLocalPath;
        if (string.IsNullOrEmpty(repoDir) || !Directory.Exists(repoDir))
            throw new InvalidOperationException("Repository path not found.");

        // Create a dedicated spotlight worktree (sibling of the main repo)
        var repoName = Path.GetFileName(repoDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var spotlightDir = Path.Combine(
            Path.GetDirectoryName(repoDir)!,
            $".cominomi-spotlight-{repoName}");

        // Clean up stale spotlight worktree if it exists
        if (Directory.Exists(spotlightDir))
        {
            await RunGitAsync($"worktree remove \"{spotlightDir}\" --force", repoDir);
            if (Directory.Exists(spotlightDir))
                Directory.Delete(spotlightDir, recursive: true);
            await RunGitAsync("worktree prune", repoDir);
        }

        // Persist state BEFORE making changes so crash recovery can clean up
        await PersistStateAsync(session.Id, repoDir, spotlightDir);

        // Create a detached worktree from the session branch
        var addResult = await RunGitAsync(
            $"worktree add --detach \"{spotlightDir}\" \"{session.Git.BranchName}\"", repoDir);
        if (!addResult.Success)
        {
            TryDeleteStateFile();
            throw new InvalidOperationException($"Failed to create spotlight worktree: {addResult.Error}");
        }

        // Sync uncommitted changes from session worktree to spotlight worktree
        await SyncFilesAsync(session.Git.WorktreePath, spotlightDir);

        // Start watching session worktree for changes
        var watcher = new FileSystemWatcher(session.Git.WorktreePath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 65_536, // 64 KB (default 8 KB overflows during rapid changes on Windows)
            EnableRaisingEvents = true
        };

        var spotlightSession = new SpotlightSession
        {
            SessionId = session.Id,
            RepoDir = repoDir,
            SessionWorktreePath = session.Git.WorktreePath,
            SpotlightWorktreePath = spotlightDir,
            Watcher = watcher
        };

        // Throttled sync: periodic timer checks a dirty flag instead of
        // restarting a timer per event (avoids Stop/Start storm on burst changes).
        var syncTimer = new Timer(async _ =>
        {
            if (Interlocked.Exchange(ref spotlightSession.Dirty, 0) == 0)
                return;

            if (!spotlightSession.SyncLock.Wait(0))
            {
                // Another sync in progress; re-mark so next tick retries
                Volatile.Write(ref spotlightSession.Dirty, 1);
                return;
            }
            try
            {
                await SyncFilesAsync(spotlightSession.SessionWorktreePath, spotlightSession.SpotlightWorktreePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Spotlight sync failed for session {SessionId}", session.Id);
            }
            finally
            {
                spotlightSession.SyncLock.Release();
            }
        }, null, ThrottleMs, ThrottleMs);
        spotlightSession.SyncTimer = syncTimer;

        void OnChange(object sender, FileSystemEventArgs e)
        {
            // Skip .git directory changes
            if (e.FullPath.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar) ||
                e.FullPath.EndsWith(Path.DirectorySeparatorChar + ".git"))
                return;

            Volatile.Write(ref spotlightSession.Dirty, 1);
        }

        watcher.Changed += OnChange;
        watcher.Created += OnChange;
        watcher.Deleted += OnChange;
        watcher.Renamed += (s, e) => OnChange(s, e);
        watcher.Error += (_, e) =>
            _logger.LogWarning(e.GetException(), "FileSystemWatcher buffer overflow for session {SessionId}", session.Id);

        _sessions[session.Id] = spotlightSession;
        _logger.LogInformation("Spotlight started for session {SessionId} at {Path}", session.Id, spotlightDir);
    }

    public async Task StopAsync(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
            return;

        // Stop watching
        session.Watcher.EnableRaisingEvents = false;
        session.Watcher.Dispose();
        session.SyncTimer?.Dispose();
        session.SyncLock.Dispose();

        // Remove the spotlight worktree
        try
        {
            await RunGitAsync($"worktree remove \"{session.SpotlightWorktreePath}\" --force", session.RepoDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove spotlight worktree, cleaning up manually");
            try
            {
                if (Directory.Exists(session.SpotlightWorktreePath))
                    Directory.Delete(session.SpotlightWorktreePath, recursive: true);
                await RunGitAsync("worktree prune", session.RepoDir);
            }
            catch (Exception cleanEx)
            {
                _logger.LogDebug(cleanEx, "Manual spotlight cleanup also failed");
            }
        }

        TryDeleteStateFile();
        _logger.LogInformation("Spotlight stopped for session {SessionId}", sessionId);
    }

    private async Task PersistStateAsync(string sessionId, string repoDir, string spotlightWorktreePath)
    {
        var state = new SpotlightPersistedState
        {
            SessionId = sessionId,
            RepoDir = repoDir,
            SpotlightWorktreePath = spotlightWorktreePath
        };
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        await AtomicFileWriter.WriteAsync(StateFilePath, json);
    }

    private void TryDeleteStateFile()
    {
        try { if (File.Exists(StateFilePath)) File.Delete(StateFilePath); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete spotlight state file"); }
    }

    private async Task SyncFilesAsync(string sourceWorktreePath, string destWorktreePath)
    {
        // Only sync files that have actually changed (modified, added, deleted, untracked)
        var statusResult = await RunGitAsync("status --porcelain -u", sourceWorktreePath);
        if (!statusResult.Success) return;

        foreach (var line in statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4) continue;

            var statusCode = line[..2];
            var filePath = line[3..].Trim();

            // Handle renamed files (old -> new)
            if (filePath.Contains(" -> "))
                filePath = filePath.Split(" -> ")[1];

            var sourcePath = Path.Combine(sourceWorktreePath, filePath);
            var destPath = Path.Combine(destWorktreePath, filePath);

            if (statusCode.Contains('D'))
            {
                // File was deleted in worktree
                if (File.Exists(destPath))
                {
                    try { File.Delete(destPath); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete spotlight file: {Path}", destPath); }
                }
            }
            else if (File.Exists(sourcePath))
            {
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null)
                    Directory.CreateDirectory(destDir);

                try
                {
                    File.Copy(sourcePath, destPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to copy spotlight file: {Path}", filePath);
                }
            }
        }
    }

    private Task<GitResult> RunGitAsync(string arguments, string workingDir, CancellationToken ct = default)
        => _gitService.RunAsync(arguments, workingDir, ct);

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Watcher.Dispose();
            session.SyncTimer?.Dispose();
            session.SyncLock.Dispose();
        }
        _sessions.Clear();
    }

    private class SpotlightSession
    {
        public required string SessionId { get; init; }
        public required string RepoDir { get; init; }
        public required string SessionWorktreePath { get; init; }
        public required string SpotlightWorktreePath { get; init; }
        public required FileSystemWatcher Watcher { get; init; }
        public Timer? SyncTimer { get; set; }
        public int Dirty; // 0 = clean, 1 = needs sync; accessed via Volatile/Interlocked
        public SemaphoreSlim SyncLock { get; } = new(1, 1);
    }

    private class SpotlightPersistedState
    {
        public string SessionId { get; set; } = "";
        public string RepoDir { get; set; } = "";
        public string SpotlightWorktreePath { get; set; } = "";
    }
}
