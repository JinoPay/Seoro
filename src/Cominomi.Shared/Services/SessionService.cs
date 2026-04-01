using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

using Cominomi.Shared;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services.Migration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Services;

public partial class SessionService : ISessionService
{
    private readonly IGitService _gitService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IOptionsMonitor<AppSettings> _appSettings;
    private readonly IContextService _contextService;
    private readonly IHooksEngine _hooksEngine;
    private readonly IActiveSessionRegistry _activeSessionRegistry;
    private readonly IWorktreeSyncService _worktreeSyncService;
    private readonly ILogger<SessionService> _logger;
    private readonly string _sessionsDir = AppPaths.Sessions;
    private readonly string _archiveDir = AppPaths.ArchivedContexts;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _worktreeInitLocks = new();

    // In-memory metadata cache: avoids re-reading all files on every call
    private readonly ConcurrentDictionary<string, Session> _metadataCache = new();
    private volatile bool _cacheInitialized;

    // Full session cache: avoids redundant disk reads for the same session within a short window
    private readonly ConcurrentDictionary<string, (Session Session, DateTime LoadedAt)> _sessionCache = new();
    private static readonly TimeSpan SessionCacheTtl = TimeSpan.FromSeconds(2);
    private const int MaxSessionCacheEntries = 64;
    private static readonly TimeSpan ScavengeInterval = TimeSpan.FromSeconds(30);
    private DateTime _lastScavengeUtc = DateTime.UtcNow;

    public SessionService(IGitService gitService, IWorkspaceService workspaceService,
        IOptionsMonitor<AppSettings> appSettings, IContextService contextService, IHooksEngine hooksEngine,
        IActiveSessionRegistry activeSessionRegistry, IWorktreeSyncService worktreeSyncService,
        ILogger<SessionService> logger)
    {
        _gitService = gitService;
        _workspaceService = workspaceService;
        _appSettings = appSettings;
        _contextService = contextService;
        _hooksEngine = hooksEngine;
        _activeSessionRegistry = activeSessionRegistry;
        _worktreeSyncService = worktreeSyncService;
        _logger = logger;
    }

    public async Task<List<Session>> GetSessionsAsync()
    {
        await EnsureCacheLoadedAsync();
        return _metadataCache.Values
            .OrderByDescending(s => s.UpdatedAt)
            .ToList();
    }

    public async Task<List<Session>> GetSessionsByWorkspaceAsync(string workspaceId)
    {
        await EnsureCacheLoadedAsync();
        return _metadataCache.Values
            .Where(s => s.WorkspaceId == workspaceId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToList();
    }

    private async Task EnsureCacheLoadedAsync()
    {
        if (_cacheInitialized)
            return;

        if (!Directory.Exists(_sessionsDir))
        {
            _cacheInitialized = true;
            return;
        }

        var files = Directory.GetFiles(_sessionsDir, "*.json")
            .Where(f => !f.EndsWith(".messages.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Parallel file reads for initial load
        var tasks = files.Select(async file =>
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var needsMigration = NeedsSchemaUpgrade(json);
                var session = JsonSerializer.Deserialize<Session>(json, JsonDefaults.Options);
                if (session != null)
                {
                    session.Model = ModelDefinitions.NormalizeModelId(session.Model);
                    session.Messages.Clear();
                    // Write back if schema was outdated (adds $schemaVersion)
                    if (needsMigration)
                    {
                        var upgraded = JsonSerializer.Serialize(session, JsonDefaults.Options);
                        await AtomicFileWriter.WriteAsync(file, upgraded);
                    }
                    return session;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping corrupted session file: {File}", file);
            }
            return null;
        });

        var results = await Task.WhenAll(tasks);
        foreach (var session in results)
        {
            if (session != null)
                _metadataCache[session.Id] = session;
        }

        _cacheInitialized = true;
        _logger.LogDebug("Session metadata cache initialized: {Count} sessions loaded", _metadataCache.Count);
    }

    public async Task<Session> CreateSessionAsync(string model, string workspaceId, string baseBranch)
    {
        Guard.NotNullOrWhiteSpace(model, nameof(model));
        Guard.NotNullOrWhiteSpace(workspaceId, nameof(workspaceId));
        Guard.NotNullOrWhiteSpace(baseBranch, nameof(baseBranch));

        var workspace = await _workspaceService.LoadWorkspaceAsync(workspaceId);
        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{workspaceId}' not found.");

        await EnforceSessionLimitAsync(workspaceId);

        var branchName = $"{CominomiConstants.BranchPrefix}{DateTime.Now:yyyyMMdd-HHmmss}";
        var worktreesDir = await _workspaceService.GetWorktreesDirAsync();

        var settings = _appSettings.CurrentValue;
        var session = new Session
        {
            Model = model,
            WorkspaceId = workspaceId,
            Git = { BranchName = branchName, BaseBranch = baseBranch },
            EffortLevel = settings.DefaultEffortLevel,
            PermissionMode = settings.DefaultPermissionMode
        };
        session.TransitionStatus(SessionStatus.Initializing);

        session.Git.WorktreePath = Path.Combine(worktreesDir, session.Id);

        try
        {
            var result = await _gitService.AddWorktreeAsync(
                workspace.RepoLocalPath, session.Git.WorktreePath, branchName, baseBranch);

            if (!result.Success)
            {
                session.TransitionStatus(SessionStatus.Error);
                session.Error = AppError.WorktreeCreation(result.Error);
            }
            else
            {
                session.TransitionStatus(SessionStatus.Ready);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session worktree for workspace {WorkspaceId}", workspaceId);
            session.TransitionStatus(SessionStatus.Error);
            session.Error = AppError.FromException(ErrorCode.WorktreeCreationFailed, ex);
        }

        return session;
    }

    public async Task<Session> CreatePendingSessionAsync(string model, string workspaceId)
    {
        Guard.NotNullOrWhiteSpace(model, nameof(model));
        Guard.NotNullOrWhiteSpace(workspaceId, nameof(workspaceId));

        var workspace = await _workspaceService.LoadWorkspaceAsync(workspaceId);
        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{workspaceId}' not found.");

        await EnforceSessionLimitAsync(workspaceId);

        var settings = _appSettings.CurrentValue;
        var cityName = CityNames.GetRandom();
        var session = new Session
        {
            Model = model,
            WorkspaceId = workspaceId,
            CityName = cityName,
            Title = cityName,
            EffortLevel = settings.DefaultEffortLevel,
            PermissionMode = settings.DefaultPermissionMode
        };
        session.TransitionStatus(SessionStatus.Pending);

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
        Guard.NotNullOrWhiteSpace(model, nameof(model));
        Guard.NotNullOrWhiteSpace(workspaceId, nameof(workspaceId));

        var workspace = await _workspaceService.LoadWorkspaceAsync(workspaceId);
        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{workspaceId}' not found.");

        var settings = _appSettings.CurrentValue;
        var cityName = CityNames.GetRandom();
        var branch = await _gitService.GetCurrentBranchAsync(workspace.RepoLocalPath);
        var session = new Session
        {
            Model = model,
            WorkspaceId = workspaceId,
            CityName = cityName,
            Title = cityName,
            Git = { IsLocalDir = true, WorktreePath = workspace.RepoLocalPath, BranchName = branch ?? "" },
            EffortLevel = settings.DefaultEffortLevel,
            PermissionMode = settings.DefaultPermissionMode
        };
        session.TransitionStatus(SessionStatus.Ready);

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
        Guard.NotNullOrWhiteSpace(sessionId, nameof(sessionId));
        Guard.NotNullOrWhiteSpace(baseBranch, nameof(baseBranch));

        var semaphore = _worktreeInitLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            // Invalidate cache so we read the latest state from disk after waiting on the lock
            _sessionCache.TryRemove(sessionId, out _);

            var session = await LoadSessionAsync(sessionId);
            if (session == null)
                throw new InvalidOperationException($"Session '{sessionId}' not found.");

            // Guard: if another call already initialized this session, return as-is
            if (session.Status != SessionStatus.Pending)
            {
                _logger.LogDebug("Skipping worktree init for session {SessionId}: status is {Status}", sessionId, session.Status);
                return session;
            }

            var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
            if (workspace == null)
                throw new InvalidOperationException($"Workspace '{session.WorkspaceId}' not found.");

            var branchName = $"{CominomiConstants.BranchPrefix}{DateTime.Now:yyyyMMdd-HHmmss}";
            var worktreesDir = await _workspaceService.GetWorktreesDirAsync();

            session.Git.BranchName = branchName;
            session.Git.BaseBranch = baseBranch;
            session.Git.WorktreePath = Path.Combine(worktreesDir, session.Id);
            session.TransitionStatus(SessionStatus.Initializing);

            try
            {
                var result = await _gitService.AddWorktreeAsync(
                    workspace.RepoLocalPath, session.Git.WorktreePath, branchName, baseBranch);

                if (!result.Success)
                {
                    session.TransitionStatus(SessionStatus.Error);
                    session.Error = AppError.WorktreeCreation(result.Error);
                }
                else
                {
                    session.TransitionStatus(SessionStatus.Ready);
                    // Initialize .context/ directory for collaboration
                    await _contextService.EnsureContextDirectoryAsync(session.Git.WorktreePath);
                    _logger.LogInformation("Worktree initialized for session {SessionId} on branch {Branch}", sessionId, branchName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize worktree for session {SessionId}", sessionId);
                session.TransitionStatus(SessionStatus.Error);
                session.Error = AppError.FromException(ErrorCode.WorktreeCreationFailed, ex);
            }

            await SaveSessionAsync(session);
            return session;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<Session?> LoadSessionAsync(string sessionId)
    {
        // Active sessions (currently streaming) are authoritative — return the same instance
        var active = _activeSessionRegistry.Get(sessionId);
        if (active != null)
            return active;

        ScavengeExpiredSessions();

        // Return cached session if still fresh (avoids redundant disk I/O in pipelines)
        if (_sessionCache.TryGetValue(sessionId, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < SessionCacheTtl)
        {
            return cached.Session;
        }

        var path = Path.Combine(_sessionsDir, $"{sessionId}.json");
        if (!File.Exists(path))
        {
            _logger.LogDebug("Session file not found: {SessionId}", sessionId);
            return null;
        }

        var json = await File.ReadAllTextAsync(path);
        var needsMigration = NeedsSchemaUpgrade(json);
        var session = JsonSerializer.Deserialize<Session>(json, JsonDefaults.Options);
        if (session == null) return null;

        session.Model = ModelDefinitions.NormalizeModelId(session.Model);

        // Write back if schema was outdated (adds $schemaVersion)
        if (needsMigration)
        {
            string upgraded;
            lock (session.MessagesLock)
            {
                var messages = session.Messages;
                session.Messages = [];
                upgraded = JsonSerializer.Serialize(session, JsonDefaults.Options);
                session.Messages = messages;
            }
            await AtomicFileWriter.WriteAsync(path, upgraded);
        }

        // Load messages from separate file (new format)
        var messagesPath = Path.Combine(_sessionsDir, $"{sessionId}.messages.json");
        if (File.Exists(messagesPath))
        {
            var messagesJson = await File.ReadAllTextAsync(messagesPath);
            var messages = JsonSerializer.Deserialize<List<ChatMessage>>(messagesJson, JsonDefaults.Options);
            if (messages != null)
                session.Messages = messages;
        }
        // else: old format — messages are already inline from the main JSON

        foreach (var msg in session.Messages)
            msg.MigrateToParts();

        _sessionCache[sessionId] = (session, DateTime.UtcNow);
        EnforceSessionCacheCapacity();
        _logger.LogDebug("Loaded session {SessionId}: {Title}", sessionId, session.Title);
        return session;
    }

    private void ScavengeExpiredSessions()
    {
        var now = DateTime.UtcNow;
        if (now - _lastScavengeUtc < ScavengeInterval)
            return;
        _lastScavengeUtc = now;

        var removed = 0;
        foreach (var kvp in _sessionCache)
        {
            if (now - kvp.Value.LoadedAt >= SessionCacheTtl)
            {
                if (_sessionCache.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }

        if (removed > 0)
            _logger.LogDebug("Session cache scavenged: removed {Removed}, {Remaining} entries remaining", removed, _sessionCache.Count);
    }

    private void EnforceSessionCacheCapacity()
    {
        if (_sessionCache.Count <= MaxSessionCacheEntries)
            return;

        var toEvict = _sessionCache
            .OrderBy(kv => kv.Value.LoadedAt)
            .Take(_sessionCache.Count - MaxSessionCacheEntries)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toEvict)
            _sessionCache.TryRemove(key, out _);

        _logger.LogDebug("Session cache capacity enforced: evicted {Count}, limit {Max}", toEvict.Count, MaxSessionCacheEntries);
    }

    private const int MaxToolOutputLength = 2000;

    public async Task SaveSessionAsync(Session session)
    {
        var semaphore = _sessionLocks.GetOrAdd(session.Id, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            _logger.LogDebug("Saving session {SessionId} ({Title})", session.Id, session.Title);
            session.UpdatedAt = DateTime.UtcNow;

            // Fallback title: only if title is still the initial city name (Haiku summary not yet applied)
            string metadataJson;
            List<ChatMessage> messages;
            lock (session.MessagesLock)
            {
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

                // Save metadata (without messages) — swap under lock to prevent
                // concurrent readers (e.g. Blazor renderer) from seeing an empty list
                messages = session.Messages;
                session.Messages = [];
                metadataJson = JsonSerializer.Serialize(session, JsonDefaults.Options);
                session.Messages = messages;
            }

            var metadataPath = Path.Combine(_sessionsDir, $"{session.Id}.json");
            await AtomicFileWriter.WriteAsync(metadataPath, metadataJson);

            // Invalidate full session cache so next LoadSessionAsync re-reads from disk
            _sessionCache[session.Id] = (session, DateTime.UtcNow);

            // Update in-memory cache with a metadata-only clone
            var cached = JsonSerializer.Deserialize<Session>(metadataJson, JsonDefaults.Options);
            if (cached != null)
            {
                cached.Model = ModelDefinitions.NormalizeModelId(cached.Model);
                _metadataCache[session.Id] = cached;
            }

            // Save messages separately (with tool output truncation)
            if (messages.Count > 0)
            {
                var truncated = TruncateToolOutputs(messages);
                var messagesJson = JsonSerializer.Serialize(truncated, JsonDefaults.Options);
                var messagesPath = Path.Combine(_sessionsDir, $"{session.Id}.messages.json");
                await AtomicFileWriter.WriteAsync(messagesPath, messagesJson);
            }

            _logger.LogDebug("Session {SessionId} saved: {MessageCount} messages", session.Id, messages.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save session {SessionId}", session.Id);
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static List<ChatMessage> TruncateToolOutputs(List<ChatMessage> messages)
    {
        return messages.Select(msg =>
        {
            bool needsTruncation = msg.ToolCalls.Any(tc => tc.Output.Length > MaxToolOutputLength)
                || msg.Parts.Any(p => p.ToolCall != null && p.ToolCall.Output.Length > MaxToolOutputLength);

            if (!needsTruncation)
                return msg;

            return new ChatMessage
            {
                Id = msg.Id,
                Role = msg.Role,
                Text = msg.Text,
                Timestamp = msg.Timestamp,
                IsStreaming = msg.IsStreaming,
                StreamingStartedAt = msg.StreamingStartedAt,
                StreamingFinishedAt = msg.StreamingFinishedAt,
                Attachments = msg.Attachments,
                ToolCalls = msg.ToolCalls.Select(TruncateToolCall).ToList(),
                Parts = msg.Parts.Select(p => p.ToolCall != null
                    ? new ContentPart { Type = p.Type, Text = p.Text, ToolCall = TruncateToolCall(p.ToolCall) }
                    : p).ToList()
            };
        }).ToList();
    }

    private static ToolCall TruncateToolCall(ToolCall tc)
    {
        if (tc.Output.Length <= MaxToolOutputLength) return tc;
        return new ToolCall
        {
            Id = tc.Id,
            Name = tc.Name,
            Input = tc.Input,
            Output = tc.Output[..MaxToolOutputLength] + $"\n[...truncated, {tc.Output.Length} chars total]",
            IsError = tc.IsError,
            IsComplete = tc.IsComplete
        };
    }

    public async Task RenameBranchAsync(string sessionId, string newBranchName)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null || session.Status != SessionStatus.Ready)
            return;

        var oldBranch = session.Git.BranchName;
        if (oldBranch == newBranchName)
            return;

        var result = await _gitService.RenameBranchAsync(session.Git.WorktreePath, oldBranch, newBranchName);
        if (result.Success)
        {
            session.Git.BranchName = newBranchName;
            await SaveSessionAsync(session);
            _logger.LogInformation("Session {SessionId} branch renamed: {OldBranch} -> {NewBranch}", sessionId, oldBranch, newBranchName);
        }
        else
        {
            _logger.LogWarning("Failed to rename branch for session {SessionId}: {OldBranch} -> {NewBranch}: {Error}", sessionId, oldBranch, newBranchName, result.Error);
        }
    }

    public async Task CleanupSessionAsync(string sessionId)
    {
        // Auto-unsync if this session is currently synced to the local dir
        if (_worktreeSyncService.IsSessionSynced(sessionId))
        {
            try { await _worktreeSyncService.StopSyncAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to auto-unsync session {SessionId} during cleanup", sessionId); }
        }

        var session = await LoadSessionAsync(sessionId);
        if (session == null)
            return;

        var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
        if (workspace == null)
            return;

        // Archive .context/ before removing worktree
        if (!string.IsNullOrEmpty(session.Git.WorktreePath) && Directory.Exists(session.Git.WorktreePath))
        {
            try
            {
                var baseName = SanitizePathSegment(
                    !string.IsNullOrEmpty(session.CityName) ? session.CityName : session.Id);
                var archiveName = $"{baseName}_{session.Id[..Math.Min(8, session.Id.Length)]}";
                var archivePath = Path.Combine(_archiveDir, SanitizePathSegment(workspace.Name), archiveName);
                await _contextService.ArchiveContextAsync(session.Git.WorktreePath, archivePath);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to archive context for session {SessionId}", sessionId); }
        }

        // Skip worktree/branch cleanup for local-dir sessions (the directory is the user's real repo)
        if (!session.Git.IsLocalDir)
        {
            // Remove worktree
            if (!string.IsNullOrEmpty(session.Git.WorktreePath))
            {
                try
                {
                    await _gitService.RemoveWorktreeAsync(workspace.RepoLocalPath, session.Git.WorktreePath);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to remove worktree for session {SessionId}", sessionId); }
            }

            // Delete branch
            if (!string.IsNullOrEmpty(session.Git.BranchName))
            {
                try
                {
                    await _gitService.DeleteBranchAsync(workspace.RepoLocalPath, session.Git.BranchName);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete branch {Branch} for session {SessionId}", session.Git.BranchName, sessionId); }
            }
        }

        session.TransitionStatus(SessionStatus.Archived);
        await SaveSessionAsync(session);
        _sessionCache.TryRemove(sessionId, out _);
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
        if (session != null && session.Status is SessionStatus.Ready)
        {
            try { await CleanupSessionAsync(sessionId); }
            catch (Exception ex) { _logger.LogWarning(ex, "Cleanup failed during session delete: {SessionId}", sessionId); }
        }

        try
        {
            var path = Path.Combine(_sessionsDir, $"{sessionId}.json");
            if (File.Exists(path))
                File.Delete(path);

            var messagesPath = Path.Combine(_sessionsDir, $"{sessionId}.messages.json");
            if (File.Exists(messagesPath))
                File.Delete(messagesPath);
        }
        finally
        {
            // Ensure caches are purged even if file deletion fails
            _metadataCache.TryRemove(sessionId, out _);
            _sessionCache.TryRemove(sessionId, out _);
            _sessionLocks.TryRemove(sessionId, out _);
            _worktreeInitLocks.TryRemove(sessionId, out _);
        }

        _logger.LogInformation("Session {SessionId} deleted", sessionId);
    }

    /// <summary>
    /// Checks if a JSON string is missing the current $schemaVersion — meaning it needs migration/upgrade.
    /// </summary>
    private bool NeedsSchemaUpgrade(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject doc) return false;
            var version = SchemaVersion.Read(doc);
            var migrator = SchemaMigratorRegistry.GetMigrator<Session>();
            return migrator != null && version < migrator.CurrentVersion;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check schema upgrade for session JSON");
            return false;
        }
    }

    private async Task EnforceSessionLimitAsync(string workspaceId)
    {
        var active = await GetSessionsByWorkspaceAsync(workspaceId);
        var activeCount = active.Count(s => s.Status is not SessionStatus.Archived and not SessionStatus.Error);
        if (activeCount >= CominomiConstants.MaxActiveSessionsPerWorkspace)
            throw new InvalidOperationException(
                $"Workspace has {activeCount} active sessions (limit: {CominomiConstants.MaxActiveSessionsPerWorkspace}). Archive or delete existing sessions first.");
    }

    private static string SanitizePathSegment(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }

}
