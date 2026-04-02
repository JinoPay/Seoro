using System.Text.Json;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Cominomi.Shared.Services;

public class WorktreeSyncService : IWorktreeSyncService
{
    private const int DebounceMs = 500;
    private const int FileRetryCount = 3;
    private const int FileRetryDelayMs = 200;
    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

    private const string StateFileName = "sync-state.json";
    private readonly HashSet<string> _copiedSet = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly IChatEventBus _eventBus;
    private readonly IGitService _gitService;
    private readonly ILogger<WorktreeSyncService> _logger;
    private readonly Lock _pendingLock = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _disposed;
    private FileSystemWatcher? _watcher;

    private SyncState? _state;
    private Timer? _debounceTimer;

    public WorktreeSyncService(IGitService gitService, IChatEventBus eventBus, ILogger<WorktreeSyncService> logger)
    {
        _gitService = gitService;
        _eventBus = eventBus;
        _logger = logger;
    }

    // ────────────────────── Dispose ──────────────────────

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Stop sync synchronously on dispose (app closing)
        if (_state != null)
            try
            {
                RestoreAndCleanupAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore during dispose");
            }

        StopWatching();
        _syncLock.Dispose();
    }

    public bool IsSessionSynced(string sessionId)
    {
        return _state?.SessionId == sessionId;
    }

    public bool IsSyncActive => _state != null;
    public string? SyncedSessionId => _state?.SessionId;

    public async Task RecoverFromCrashAsync()
    {
        if (!Directory.Exists(AppPaths.SyncBackups))
            return;

        foreach (var workspaceDir in Directory.GetDirectories(AppPaths.SyncBackups))
        foreach (var sessionDir in Directory.GetDirectories(workspaceDir))
        {
            var stateFile = Path.Combine(sessionDir, StateFileName);
            if (!File.Exists(stateFile))
                continue;

            _logger.LogWarning("Found orphaned sync state at {Path}, recovering...", stateFile);
            try
            {
                var json = await File.ReadAllTextAsync(stateFile);
                var state = JsonSerializer.Deserialize<SyncState>(json);
                if (state != null)
                {
                    _state = state;
                    await RestoreAndCleanupAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to recover sync state from {Path}", stateFile);
                // Best effort: delete the orphaned state
                try
                {
                    Directory.Delete(sessionDir, true);
                }
                catch
                {
                    /* ignore */
                }
            }
        }
    }

    public async Task StopSyncAsync(CancellationToken ct = default)
    {
        await _syncLock.WaitAsync(ct);
        try
        {
            await RestoreAndCleanupAsync(ct);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<bool> StartSyncAsync(Session session, Workspace workspace, CancellationToken ct = default)
    {
        await _syncLock.WaitAsync(ct);
        try
        {
            if (_state != null)
            {
                _logger.LogWarning("Sync already active for session {SessionId}", _state.SessionId);
                return false;
            }

            if (session.Status != SessionStatus.Ready ||
                string.IsNullOrEmpty(session.Git.WorktreePath) ||
                !Directory.Exists(session.Git.WorktreePath))
            {
                _logger.LogWarning("Session {SessionId} is not in a valid state for sync", session.Id);
                return false;
            }

            var repoLocal = workspace.RepoLocalPath;
            if (string.IsNullOrEmpty(repoLocal) || !Directory.Exists(repoLocal))
            {
                _logger.LogWarning("Workspace RepoLocalPath not found: {Path}", repoLocal);
                return false;
            }

            var backupDir = Path.Combine(AppPaths.SyncBackups, workspace.Id, session.Id);
            Directory.CreateDirectory(backupDir);

            var state = new SyncState
            {
                WorkspaceId = workspace.Id,
                SessionId = session.Id,
                RepoLocalPath = repoLocal,
                WorktreePath = session.Git.WorktreePath,
                BaseBranch = session.Git.BaseBranch,
                BaseCommit = session.Git.BaseCommit,
                BackupDir = backupDir
            };

            // 1. Backup local dir's dirty files
            await BackupLocalDirAsync(state, ct);

            // 2. Save state (crash recovery marker)
            await SaveStateAsync(state);

            // 3. Clean local dir (revert tracked changes, remove backed-up untracked files)
            await CleanLocalDirAsync(state, ct);

            // 4. Copy worktree changes to local dir
            await CopyWorktreeChangesAsync(state, ct);

            // 5. Update and save state
            await SaveStateAsync(state);

            // 6. Start watching worktree for live sync
            StartWatching(state);

            _state = state;
            _logger.LogInformation("Sync started: session {SessionId} → {RepoLocal}", session.Id, repoLocal);
            _eventBus.Publish(new WorktreeSyncStartedEvent(session.Id, workspace.Id));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start sync for session {SessionId}", session.Id);
            // Attempt to restore on failure
            try
            {
                await RestoreAndCleanupAsync(ct);
            }
            catch (Exception restoreEx)
            {
                _logger.LogError(restoreEx, "Failed to restore after sync start failure");
            }

            return false;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private static bool ShouldIgnorePath(string fullPath)
    {
        var sep = Path.DirectorySeparatorChar;
        return fullPath.Contains($"{sep}.git{sep}") || fullPath.EndsWith($"{sep}.git") ||
               fullPath.Contains($"{sep}.context{sep}") || fullPath.EndsWith($"{sep}.context");
    }

    private static string? ToRelativePath(string fullPath, string worktreeRoot)
    {
        if (!fullPath.StartsWith(worktreeRoot, StringComparison.OrdinalIgnoreCase))
            return null;
        var rel = fullPath[worktreeRoot.Length..].TrimStart(Path.DirectorySeparatorChar);
        return rel.Length > 0 ? rel.Replace(Path.DirectorySeparatorChar, '/') : null;
    }

    // ────────────────────── Internal: State persistence ──────────────────────

    private static async Task SaveStateAsync(SyncState state)
    {
        var stateFile = Path.Combine(state.BackupDir, StateFileName);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(stateFile, json);
    }

    private static async Task<bool> FilesAreEqualAsync(string pathA, string pathB)
    {
        var infoA = new FileInfo(pathA);
        var infoB = new FileInfo(pathB);

        if (!infoA.Exists || !infoB.Exists)
            return false;

        if (infoA.Length != infoB.Length)
            return false;

        if (infoA.Length == 0)
            return true;

        const int bufferSize = 8192;
        var bufferA = new byte[bufferSize];
        var bufferB = new byte[bufferSize];

        await using var streamA =
            new FileStream(pathA, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize);
        await using var streamB =
            new FileStream(pathB, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize);

        while (true)
        {
            var readA = await streamA.ReadAsync(bufferA);
            var readB = await streamB.ReadAsync(bufferB);

            if (readA != readB)
                return false;

            if (readA == 0)
                return true;

            if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                return false;
        }
    }

    // ────────────────────── Internal: Backup ──────────────────────

    private async Task BackupLocalDirAsync(SyncState state, CancellationToken ct = default)
    {
        var statusLines = await _gitService.GetStatusPorcelainAsync(state.RepoLocalPath, ct);

        foreach (var line in statusLines)
        {
            // porcelain format: "XY path" or "XY path -> newpath"
            if (line.Length < 4)
                continue;

            var statusCode = line[..2];
            var filePath = line[3..].Trim();

            // Handle renames: "R  old -> new"
            var arrowIdx = filePath.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIdx >= 0)
                filePath = filePath[(arrowIdx + 4)..];

            // Trim quotes that git adds for non-ASCII paths
            if (filePath.StartsWith('"') && filePath.EndsWith('"'))
                filePath = filePath[1..^1];

            var isUntracked = statusCode.Contains('?');
            var fullPath = Path.Combine(state.RepoLocalPath, filePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
                continue;

            var backupPath = Path.Combine(state.BackupDir, filePath.Replace('/', Path.DirectorySeparatorChar));
            var backupParent = Path.GetDirectoryName(backupPath);
            if (backupParent != null)
                Directory.CreateDirectory(backupParent);

            await CopyFileWithRetryAsync(fullPath, backupPath);

            state.BackedUpFiles.Add(new SyncBackupEntry
            {
                RelativePath = filePath,
                WasUntracked = isUntracked
            });
        }

        _logger.LogDebug("Backed up {Count} files from local dir", state.BackedUpFiles.Count);
    }

    // ────────────────────── Internal: Clean ──────────────────────

    private async Task CleanLocalDirAsync(SyncState state, CancellationToken ct = default)
    {
        // Revert tracked modifications
        var trackedFiles = state.BackedUpFiles
            .Where(e => !e.WasUntracked)
            .Select(e => e.RelativePath)
            .ToList();

        if (trackedFiles.Count > 0)
            await _gitService.CheckoutFilesAsync(state.RepoLocalPath, trackedFiles, ct);

        // Delete backed-up untracked files
        foreach (var entry in state.BackedUpFiles.Where(e => e.WasUntracked))
        {
            var fullPath = Path.Combine(state.RepoLocalPath,
                entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            DeleteFileWithRetry(fullPath);
        }
    }

    private async Task CopyFileWithRetryAsync(string source, string destination)
    {
        for (var i = 0; i < FileRetryCount; i++)
            try
            {
                // Use FileStream for binary-safe copy
                await using var srcStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await using var destStream =
                    new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                await srcStream.CopyToAsync(destStream);
                return;
            }
            catch (IOException) when (i < FileRetryCount - 1)
            {
                await Task.Delay(FileRetryDelayMs);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to copy file after {Retries} retries: {Source} → {Dest}", FileRetryCount,
                    source, destination);
            }
    }

    // ────────────────────── Internal: Copy worktree → local ──────────────────────

    private async Task CopyWorktreeChangesAsync(SyncState state, CancellationToken ct = default)
    {
        // git diff로 후보 파일을 빠르게 추린 뒤, 실제 파일 비교로 검증
        var diffBase = !string.IsNullOrEmpty(state.BaseCommit) ? state.BaseCommit : state.BaseBranch;
        var changedFiles = await _gitService.GetChangedFilesAsync(state.WorktreePath, diffBase, ct);
        var copied = 0;

        foreach (var relativePath in changedFiles)
        {
            ct.ThrowIfCancellationRequested();

            var srcPath = Path.Combine(state.WorktreePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var destPath = Path.Combine(state.RepoLocalPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(srcPath))
            {
                var info = new FileInfo(srcPath);
                if (info.Length > MaxFileSizeBytes)
                {
                    _logger.LogWarning("Skipping large file during initial sync: {Path} ({Size} bytes)", relativePath,
                        info.Length);
                    continue;
                }

                if (File.Exists(destPath) && await FilesAreEqualAsync(srcPath, destPath))
                    continue;

                var destParent = Path.GetDirectoryName(destPath);
                if (destParent != null)
                    Directory.CreateDirectory(destParent);

                await CopyFileWithRetryAsync(srcPath, destPath);
                TrackCopied(state, relativePath);
                copied++;
            }
            else
            {
                // File was deleted in worktree
                if (File.Exists(destPath))
                {
                    DeleteFileWithRetry(destPath);
                    if (!state.CopiedFromWorktree.Contains(relativePath))
                        state.CopiedFromWorktree.Add(relativePath);
                    copied++;
                }
            }
        }

        _logger.LogDebug("Initial sync: copied {Count} files from worktree to local dir", copied);
    }

    private async Task OnFullResyncAsync()
    {
        await _syncLock.WaitAsync();
        try
        {
            if (_state == null)
                return;
            await CopyWorktreeChangesAsync(_state);
            await SaveStateAsync(_state);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task OnWorktreeChangedAsync()
    {
        if (_state == null)
            return;

        List<string> paths;
        lock (_pendingLock)
        {
            if (_pendingPaths.Count == 0)
                return;
            paths = _pendingPaths.ToList();
            _pendingPaths.Clear();
        }

        await _syncLock.WaitAsync();
        try
        {
            if (_state == null)
                return;
            await SyncSpecificFilesAsync(_state, paths);
            await SaveStateAsync(_state);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    // ────────────────────── Internal: Restore ──────────────────────

    private async Task RestoreAndCleanupAsync(CancellationToken ct = default)
    {
        var state = _state;
        if (state == null)
            return;

        StopWatching();

        var sessionId = state.SessionId;
        var workspaceId = state.WorkspaceId;

        try
        {
            var backedUpSet = state.BackedUpFiles.ToDictionary(b => b.RelativePath, StringComparer.OrdinalIgnoreCase);

            // 1. Revert each file copied from worktree individually
            foreach (var relativePath in state.CopiedFromWorktree)
            {
                var fullPath = Path.Combine(state.RepoLocalPath,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));

                // If this file has a backup, it will be restored in step 2 — just need to
                // revert the worktree version first so the backup can overwrite cleanly.
                if (backedUpSet.ContainsKey(relativePath))
                {
                    // Revert tracked file to HEAD first; backup restore will overwrite it.
                    if (!backedUpSet[relativePath].WasUntracked)
                        await _gitService.CheckoutFilesAsync(state.RepoLocalPath, [relativePath], ct);
                    continue;
                }

                // No backup — this file wasn't in the local dir before sync.
                // Try git checkout (works for tracked files that exist in HEAD).
                if (File.Exists(fullPath))
                {
                    var result = await _gitService.CheckoutFilesAsync(state.RepoLocalPath, [relativePath], ct);
                    if (!result.Success)
                        // File doesn't exist in HEAD → it was a new file from the worktree. Delete it.
                        DeleteFileWithRetry(fullPath);
                }
            }

            // 2. Restore backed-up files (original local dir state)
            foreach (var entry in state.BackedUpFiles)
            {
                var backupPath = Path.Combine(state.BackupDir,
                    entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var restorePath = Path.Combine(state.RepoLocalPath,
                    entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(backupPath))
                    continue;

                var restoreParent = Path.GetDirectoryName(restorePath);
                if (restoreParent != null)
                    Directory.CreateDirectory(restoreParent);

                await CopyFileWithRetryAsync(backupPath, restorePath);
            }

            _logger.LogInformation("Sync stopped and local dir restored for session {SessionId}", sessionId);
        }
        finally
        {
            // 3. Clean up state and backup dir
            _state = null;
            _copiedSet.Clear();

            if (Directory.Exists(state.BackupDir))
                try
                {
                    Directory.Delete(state.BackupDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up backup dir: {Path}", state.BackupDir);
                }

            // Clean up empty parent directories
            var workspaceBackupDir = Path.GetDirectoryName(state.BackupDir);
            if (workspaceBackupDir != null && Directory.Exists(workspaceBackupDir) &&
                !Directory.EnumerateFileSystemEntries(workspaceBackupDir).Any())
                try
                {
                    Directory.Delete(workspaceBackupDir);
                }
                catch
                {
                    /* ignore */
                }

            _eventBus.Publish(new WorktreeSyncStoppedEvent(sessionId, workspaceId));
        }
    }

    // ────────────────────── Internal: Live sync (specific files) ──────────────────────

    private async Task SyncSpecificFilesAsync(SyncState state, List<string> relativePaths)
    {
        var synced = 0;
        foreach (var relativePath in relativePaths)
        {
            var srcPath = Path.Combine(state.WorktreePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var destPath = Path.Combine(state.RepoLocalPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(srcPath))
            {
                var info = new FileInfo(srcPath);
                if (info.Length > MaxFileSizeBytes)
                {
                    _logger.LogWarning("Skipping large file during live sync: {Path} ({Size} bytes)", relativePath,
                        info.Length);
                    continue;
                }

                if (File.Exists(destPath) && await FilesAreEqualAsync(srcPath, destPath))
                    continue;

                var destParent = Path.GetDirectoryName(destPath);
                if (destParent != null)
                    Directory.CreateDirectory(destParent);

                await CopyFileWithRetryAsync(srcPath, destPath);
                TrackCopied(state, relativePath);
                synced++;
            }
            else
            {
                if (File.Exists(destPath))
                {
                    DeleteFileWithRetry(destPath);
                    if (!state.CopiedFromWorktree.Contains(relativePath))
                        state.CopiedFromWorktree.Add(relativePath);
                    synced++;
                }
            }
        }

        if (synced > 0)
            _logger.LogDebug("Live-synced {Count} files from worktree to local dir", synced);
    }

    private void DeleteFileWithRetry(string path)
    {
        if (!File.Exists(path))
            return;

        for (var i = 0; i < FileRetryCount; i++)
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (i < FileRetryCount - 1)
            {
                Thread.Sleep(FileRetryDelayMs);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to delete file after {Retries} retries: {Path}", FileRetryCount, path);
            }
    }

    // ────────────────────── Internal: Live sync watcher ──────────────────────

    private void StartWatching(SyncState state)
    {
        StopWatching();

        if (!Directory.Exists(state.WorktreePath))
            return;

        var worktreeRoot = state.WorktreePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        _watcher = new FileSystemWatcher(state.WorktreePath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _debounceTimer = new Timer(DebounceMs) { AutoReset = false };
        _debounceTimer.Elapsed += async (_, _) =>
        {
            try
            {
                await OnWorktreeChangedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during live sync");
            }
        };

        void OnFsChange(object sender, FileSystemEventArgs e)
        {
            if (ShouldIgnorePath(e.FullPath))
                return;

            var rel = ToRelativePath(e.FullPath, worktreeRoot);
            if (rel == null) return;

            lock (_pendingLock)
            {
                _pendingPaths.Add(rel);
            }

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        void OnFsRename(object sender, RenamedEventArgs e)
        {
            if (!ShouldIgnorePath(e.OldFullPath))
            {
                var oldRel = ToRelativePath(e.OldFullPath, worktreeRoot);
                if (oldRel != null)
                    lock (_pendingLock)
                    {
                        _pendingPaths.Add(oldRel);
                    }
            }

            OnFsChange(sender, e);
        }

        _watcher.Changed += OnFsChange;
        _watcher.Created += OnFsChange;
        _watcher.Deleted += OnFsChange;
        _watcher.Renamed += OnFsRename;
        _watcher.Error += async (_, e) =>
        {
            _logger.LogWarning(e.GetException(), "FileSystemWatcher buffer overflow, triggering full re-sync");
            try
            {
                await OnFullResyncAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during full re-sync fallback");
            }
        };
    }

    private void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _debounceTimer?.Dispose();
        _debounceTimer = null;

        lock (_pendingLock)
        {
            _pendingPaths.Clear();
        }
    }

    // ────────────────────── Internal: File helpers ──────────────────────

    private void TrackCopied(SyncState state, string relativePath)
    {
        if (_copiedSet.Add(relativePath))
            state.CopiedFromWorktree.Add(relativePath);
    }
}