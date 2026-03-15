using System.Collections.Concurrent;
using System.Diagnostics;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class SpotlightService : ISpotlightService, IDisposable
{
    private readonly IGitService _gitService;
    private readonly ConcurrentDictionary<string, SpotlightSession> _sessions = new();

    public SpotlightService(IGitService gitService)
    {
        _gitService = gitService;
    }

    public bool IsActive(string sessionId) => _sessions.ContainsKey(sessionId);

    public async Task StartAsync(Workspace workspace, Session session)
    {
        if (_sessions.ContainsKey(session.Id))
            return;

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
            try
            {
                await SyncFilesAsync(spotlightSession.WorktreePath, spotlightSession.RepoDir);
            }
            catch
            {
                // Best effort sync
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
    }

    public async Task StopAsync(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
            return;

        // Stop watching
        session.Watcher.EnableRaisingEvents = false;
        session.Watcher.Dispose();
        session.DebounceTimer?.Dispose();

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
    }

    private async Task SyncFilesAsync(string worktreePath, string repoDir)
    {
        // Get list of git-tracked files in the worktree
        var result = await RunGitAsync("ls-files", worktreePath);
        if (!result.Success) return;

        var trackedFiles = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrEmpty(f));

        // Also get modified/untracked files that are not ignored
        var statusResult = await RunGitAsync("status --porcelain -u", worktreePath);
        var modifiedFiles = new HashSet<string>();
        if (statusResult.Success)
        {
            foreach (var line in statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 3)
                {
                    var filePath = line[3..].Trim();
                    // Handle renamed files (old -> new)
                    if (filePath.Contains(" -> "))
                        filePath = filePath.Split(" -> ")[1];
                    modifiedFiles.Add(filePath);
                }
            }
        }

        // Combine tracked and modified files
        var allFiles = new HashSet<string>(trackedFiles);
        foreach (var f in modifiedFiles) allFiles.Add(f);

        foreach (var relativePath in allFiles)
        {
            var sourcePath = Path.Combine(worktreePath, relativePath);
            var destPath = Path.Combine(repoDir, relativePath);

            if (File.Exists(sourcePath))
            {
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null)
                    Directory.CreateDirectory(destDir);

                try
                {
                    File.Copy(sourcePath, destPath, overwrite: true);
                }
                catch
                {
                    // Skip files that can't be copied (locked, etc.)
                }
            }
            else if (File.Exists(destPath))
            {
                // File was deleted in worktree, delete from repo root too
                try { File.Delete(destPath); } catch { }
            }
        }
    }

    private static async Task<GitResult> RunGitAsync(string arguments, string workingDir, CancellationToken ct = default)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Environment =
                {
                    ["GIT_TERMINAL_PROMPT"] = "0",
                    ["NO_COLOR"] = "1"
                }
            }
        };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var result = new GitResult(process.ExitCode == 0, stdout.Trim(), stderr.Trim());
        process.Dispose();
        return result;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Watcher.Dispose();
            session.DebounceTimer?.Dispose();
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
    }
}
