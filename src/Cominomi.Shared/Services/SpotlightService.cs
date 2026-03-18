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

        if (state == null || string.IsNullOrEmpty(state.RepoDir) || string.IsNullOrEmpty(state.OriginalBranch))
        {
            TryDeleteStateFile();
            return;
        }

        if (!Directory.Exists(state.RepoDir))
        {
            _logger.LogWarning("Spotlight recovery: repo dir {RepoDir} no longer exists", state.RepoDir);
            TryDeleteStateFile();
            return;
        }

        _logger.LogWarning("Recovering from spotlight crash for session {SessionId} in {RepoDir}",
            state.SessionId, state.RepoDir);

        try
        {
            await RunGitAsync("checkout -- .", state.RepoDir);
            await RunGitAsync("clean -fd", state.RepoDir);
            await RunGitAsync($"checkout \"{state.OriginalBranch}\"", state.RepoDir);

            var stashMarker = $"cominomi-spotlight-{state.SessionId}";
            var stashList = await RunGitAsync("stash list", state.RepoDir);
            if (stashList.Success && stashList.Output.Contains(stashMarker))
            {
                await RunGitAsync("stash pop", state.RepoDir);
            }

            _logger.LogInformation("Spotlight recovery completed for session {SessionId}", state.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Spotlight recovery failed for session {SessionId}", state.SessionId);
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

        // Record original branch so we can restore later
        var originalBranch = await _gitService.GetCurrentBranchAsync(repoDir);
        if (originalBranch == null)
            throw new InvalidOperationException("Could not detect current branch in repository.");

        // Persist state BEFORE making changes so crash recovery can clean up
        await PersistStateAsync(session.Id, originalBranch, repoDir);

        var stashMarker = $"cominomi-spotlight-{session.Id}";

        // Stash any uncommitted changes in the repo root
        await RunGitAsync($"stash push -m \"{stashMarker}\"", repoDir);

        // Checkout the session branch in the repo root
        var checkoutResult = await RunGitAsync($"checkout \"{session.Git.BranchName}\"", repoDir);
        if (!checkoutResult.Success)
        {
            // Restore stash if checkout failed
            await RunGitAsync("stash pop", repoDir);
            TryDeleteStateFile();
            throw new InvalidOperationException($"Failed to checkout branch: {checkoutResult.Error}");
        }

        // Sync uncommitted changes from worktree to repo root
        await SyncFilesAsync(session.Git.WorktreePath, repoDir);

        // Start watching worktree for changes
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
            OriginalBranch = originalBranch,
            RepoDir = repoDir,
            WorktreePath = session.Git.WorktreePath,
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
                await SyncFilesAsync(spotlightSession.WorktreePath, spotlightSession.RepoDir);
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
        _logger.LogInformation("Spotlight started for session {SessionId}", session.Id);
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

        // Discard changes in repo root and restore original branch
        await RunGitAsync("checkout -- .", session.RepoDir);
        await RunGitAsync("clean -fd", session.RepoDir);
        await RunGitAsync($"checkout \"{session.OriginalBranch}\"", session.RepoDir);

        // Restore stashed changes if any
        var stashMarker = $"cominomi-spotlight-{sessionId}";
        var stashList = await RunGitAsync("stash list", session.RepoDir);
        if (stashList.Success && stashList.Output.Contains(stashMarker))
        {
            await RunGitAsync("stash pop", session.RepoDir);
        }

        TryDeleteStateFile();
        _logger.LogInformation("Spotlight stopped for session {SessionId}", sessionId);
    }

    private async Task PersistStateAsync(string sessionId, string originalBranch, string repoDir)
    {
        var state = new SpotlightPersistedState
        {
            SessionId = sessionId,
            OriginalBranch = originalBranch,
            RepoDir = repoDir
        };
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        await AtomicFileWriter.WriteAsync(StateFilePath, json);
    }

    private void TryDeleteStateFile()
    {
        try { if (File.Exists(StateFilePath)) File.Delete(StateFilePath); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete spotlight state file"); }
    }

    private async Task SyncFilesAsync(string worktreePath, string repoDir)
    {
        // Only sync files that have actually changed (modified, added, deleted, untracked)
        var statusResult = await RunGitAsync("status --porcelain -u", worktreePath);
        if (!statusResult.Success) return;

        foreach (var line in statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4) continue;

            var statusCode = line[..2];
            var filePath = line[3..].Trim();

            // Handle renamed files (old -> new)
            if (filePath.Contains(" -> "))
                filePath = filePath.Split(" -> ")[1];

            var sourcePath = Path.Combine(worktreePath, filePath);
            var destPath = Path.Combine(repoDir, filePath);

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
        public required string OriginalBranch { get; init; }
        public required string RepoDir { get; init; }
        public required string WorktreePath { get; init; }
        public required FileSystemWatcher Watcher { get; init; }
        public Timer? SyncTimer { get; set; }
        public int Dirty; // 0 = clean, 1 = needs sync; accessed via Volatile/Interlocked
        public SemaphoreSlim SyncLock { get; } = new(1, 1);
    }

    private class SpotlightPersistedState
    {
        public string SessionId { get; set; } = "";
        public string OriginalBranch { get; set; } = "";
        public string RepoDir { get; set; } = "";
    }
}
