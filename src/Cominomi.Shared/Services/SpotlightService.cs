using System.Collections.Concurrent;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SpotlightService : ISpotlightService, IDisposable
{
    private readonly IGitService _gitService;
    private readonly ILogger<SpotlightService> _logger;
    private readonly ConcurrentDictionary<string, SpotlightSession> _sessions = new();

    public SpotlightService(IGitService gitService, ILogger<SpotlightService> logger)
    {
        _gitService = gitService;
        _logger = logger;
    }

    public bool IsActive(string sessionId) => _sessions.ContainsKey(sessionId);

    public async Task StartAsync(Workspace workspace, Session session)
    {
        if (_sessions.ContainsKey(session.Id))
            return;

        if (session.IsLocalDir || string.IsNullOrEmpty(session.BranchName))
            throw new InvalidOperationException("Spotlight is not supported for local directory sessions.");

        var repoDir = workspace.RepoLocalPath;
        if (string.IsNullOrEmpty(repoDir) || !Directory.Exists(repoDir))
            throw new InvalidOperationException("Repository path not found.");

        // Record original branch so we can restore later
        var originalBranch = await _gitService.GetCurrentBranchAsync(repoDir);
        if (originalBranch == null)
            throw new InvalidOperationException("Could not detect current branch in repository.");

        // Stash any uncommitted changes in the repo root
        await RunGitAsync("stash push -m \"cominomi-spotlight-backup\"", repoDir);

        // Checkout the session branch in the repo root
        var checkoutResult = await RunGitAsync($"checkout \"{session.BranchName}\"", repoDir);
        if (!checkoutResult.Success)
        {
            // Restore stash if checkout failed
            await RunGitAsync("stash pop", repoDir);
            throw new InvalidOperationException($"Failed to checkout branch: {checkoutResult.Error}");
        }

        // Sync uncommitted changes from worktree to repo root
        await SyncFilesAsync(session.WorktreePath, repoDir);

        // Start watching worktree for changes
        var watcher = new FileSystemWatcher(session.WorktreePath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        var spotlightSession = new SpotlightSession
        {
            SessionId = session.Id,
            OriginalBranch = originalBranch,
            RepoDir = repoDir,
            WorktreePath = session.WorktreePath,
            Watcher = watcher
        };

        // Debounced sync on file changes
        var debounceTimer = new System.Timers.Timer(500) { AutoReset = false };
        debounceTimer.Elapsed += async (_, _) =>
        {
            if (!spotlightSession.SyncLock.Wait(0))
                return; // Another sync is in progress; next change will trigger a fresh sync
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
        };
        spotlightSession.DebounceTimer = debounceTimer;

        void OnChange(object sender, FileSystemEventArgs e)
        {
            // Skip .git directory changes
            if (e.FullPath.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar) ||
                e.FullPath.EndsWith(Path.DirectorySeparatorChar + ".git"))
                return;

            debounceTimer.Stop();
            debounceTimer.Start();
        }

        watcher.Changed += OnChange;
        watcher.Created += OnChange;
        watcher.Deleted += OnChange;
        watcher.Renamed += (s, e) => OnChange(s, e);

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
        session.DebounceTimer?.Dispose();
        session.SyncLock.Dispose();

        // Discard changes in repo root and restore original branch
        await RunGitAsync("checkout -- .", session.RepoDir);
        await RunGitAsync("clean -fd", session.RepoDir);
        await RunGitAsync($"checkout \"{session.OriginalBranch}\"", session.RepoDir);

        // Restore stashed changes if any
        var stashList = await RunGitAsync("stash list", session.RepoDir);
        if (stashList.Success && stashList.Output.Contains("cominomi-spotlight-backup"))
        {
            await RunGitAsync("stash pop", session.RepoDir);
        }

        _logger.LogInformation("Spotlight stopped for session {SessionId}", sessionId);
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
            session.DebounceTimer?.Dispose();
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
        public System.Timers.Timer? DebounceTimer { get; set; }
        public SemaphoreSlim SyncLock { get; } = new(1, 1);
    }
}
