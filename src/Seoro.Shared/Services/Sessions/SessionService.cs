using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Seoro.Shared.Services.Migration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seoro.Shared.Services.Sessions;

public partial class SessionService(
    IGitService gitService,
    IWorkspaceService workspaceService,
    IOptionsMonitor<AppSettings> appSettings,
    IContextService contextService,
    IHooksEngine hooksEngine,
    IActiveSessionRegistry activeSessionRegistry,
    IWorktreeSyncService worktreeSyncService,
    ILogger<SessionService> logger)
    : ISessionService
{
    private const int MaxSessionCacheEntries = 64;

    private const int MaxToolOutputLength = 2000;
    private static readonly TimeSpan ScavengeInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SessionCacheTtl = TimeSpan.FromSeconds(2);

    // Full session cache: avoids redundant disk reads for the same session within a short window
    private readonly ConcurrentDictionary<string, (Session Session, DateTime LoadedAt)> _sessionCache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _worktreeInitLocks = new();

    // In-memory metadata cache: avoids re-reading all files on every call
    private readonly ConcurrentDictionary<string, Session> _metadataCache = new();
    // Guard: prevents fire-and-forget SaveSessionAsync from re-creating deleted files
    private readonly ConcurrentDictionary<string, byte> _deletedIds = new();
    private readonly string _archiveDir = AppPaths.ArchivedContexts;
    private readonly string _sessionsDir = AppPaths.Sessions;
    private volatile bool _cacheInitialized;
    private DateTime _lastScavengeUtc = DateTime.UtcNow;

    public async Task CleanupSessionAsync(string sessionId)
    {
        // Auto-unsync if this session is currently synced to the local dir
        if (worktreeSyncService.IsSessionSynced(sessionId))
            try
            {
                await worktreeSyncService.StopSyncAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "정리 중 세션 {SessionId} 자동 동기해제 실패", sessionId);
            }

        var session = await LoadSessionAsync(sessionId);
        if (session == null)
            return;

        var workspace = await workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
        if (workspace == null)
            return;

        // 워크트리 제거 전 .context/ 보관
        if (!string.IsNullOrEmpty(session.Git.WorktreePath) && Directory.Exists(session.Git.WorktreePath))
            try
            {
                var baseName = SanitizePathSegment(
                    !string.IsNullOrEmpty(session.CityName) ? session.CityName : session.Id);
                var archiveName = $"{baseName}_{session.Id[..Math.Min(8, session.Id.Length)]}";
                var archivePath = Path.Combine(_archiveDir, SanitizePathSegment(workspace.Name), archiveName);
                await contextService.ArchiveContextAsync(session.Git.WorktreePath, archivePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "세션 {SessionId}의 컨텍스트 보관 실패", sessionId);
            }

        // 로컬 디렉토리 세션의 워크트리/브랜치 정리 건너뛰기 (디렉토리는 사용자의 실제 저장소)
        if (!session.Git.IsLocalDir)
        {
            // 워크트리 제거
            if (!string.IsNullOrEmpty(session.Git.WorktreePath))
                try
                {
                    await gitService.RemoveWorktreeAsync(workspace.RepoLocalPath, session.Git.WorktreePath);
                    session.Git.WorktreePath = string.Empty;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "세션 {SessionId}의 워크트리 제거 실패", sessionId);
                }

            // 브랜치 삭제
            if (!string.IsNullOrEmpty(session.Git.BranchName))
                try
                {
                    await gitService.DeleteBranchAsync(workspace.RepoLocalPath, session.Git.BranchName);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "세션 {SessionId}의 브랜치 {Branch} 삭제 실패",
                        session.Git.BranchName, sessionId);
                }
        }

        session.TransitionStatus(SessionStatus.Archived);
        await SaveSessionAsync(session);
        _sessionCache.TryRemove(sessionId, out _);
        logger.LogInformation("세션 {SessionId} 보관됨", sessionId);

        await hooksEngine.FireAsync(HookEvent.OnSessionArchive, new Dictionary<string, string>
        {
            ["SEORO_SESSION_ID"] = session.Id,
            ["SEORO_CITY_NAME"] = session.CityName
        });
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        // Mark as deleted first to prevent concurrent fire-and-forget saves from re-creating files
        _deletedIds[sessionId] = 0;

        // Clean up worktree/branch before deleting
        var session = await LoadSessionAsync(sessionId);
        if (session != null && session.Status is SessionStatus.Ready)
            try
            {
                await CleanupSessionAsync(sessionId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "세션 삭제 중 정리 실패: {SessionId}", sessionId);
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

        logger.LogInformation("세션 {SessionId} 삭제됨", sessionId);
    }

    public async Task RenameBranchAsync(string sessionId, string newBranchName)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null || session.Status != SessionStatus.Ready)
            return;

        var oldBranch = session.Git.BranchName;
        if (oldBranch == newBranchName)
            return;

        var result = await gitService.RenameBranchAsync(session.Git.WorktreePath, oldBranch, newBranchName);
        if (result.Success)
        {
            session.Git.BranchName = newBranchName;
            await SaveSessionAsync(session);
            logger.LogInformation("세션 {SessionId} 브랜치 이름 변경: {OldBranch} -> {NewBranch}", sessionId,
                oldBranch, newBranchName);
        }
        else
        {
            logger.LogWarning("세션 {SessionId}의 브랜치 이름 변경 실패: {OldBranch} -> {NewBranch}: {Error}",
                sessionId, oldBranch, newBranchName, result.Error);
        }
    }

    public async Task SaveSessionAsync(Session session)
    {
        // Skip saving if this session has been deleted (race with fire-and-forget saves)
        if (_deletedIds.ContainsKey(session.Id))
            return;

        var semaphore = _sessionLocks.GetOrAdd(session.Id, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            // Re-check after acquiring the lock — deletion may have happened while waiting
            if (_deletedIds.ContainsKey(session.Id))
                return;

            logger.LogDebug("세션 {SessionId} ({Title}) 저장 중", session.Id, session.Title);
            session.UpdatedAt = DateTime.UtcNow;

            string metadataJson;
            List<ChatMessage> messages;
            lock (session.MessagesLock)
            {
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

            logger.LogDebug("세션 {SessionId} 저장됨: {MessageCount} 메시지", session.Id, messages.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "세션 {SessionId} 저장 실패", session.Id);
            throw;
        }
        finally
        {
            semaphore.Release();
        }
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

    public async Task<Session?> LoadSessionAsync(string sessionId)
    {
        // Active sessions (currently streaming) are authoritative — return the same instance
        var active = activeSessionRegistry.Get(sessionId);
        if (active != null)
            return active;

        ScavengeExpiredSessions();

        // Return cached session if still fresh (avoids redundant disk I/O in pipelines)
        if (_sessionCache.TryGetValue(sessionId, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < SessionCacheTtl)
            return cached.Session;

        var path = Path.Combine(_sessionsDir, $"{sessionId}.json");
        if (!File.Exists(path))
        {
            logger.LogDebug("세션 파일을 찾을 수 없음: {SessionId}", sessionId);
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
        logger.LogDebug("세션 {SessionId} 로드됨: {Title}", sessionId, session.Title);
        return session;
    }

    public async Task<Session> CreateLocalDirSessionAsync(string model, string workspaceId, string provider = "claude")
    {
        Guard.NotNullOrWhiteSpace(model, nameof(model));
        Guard.NotNullOrWhiteSpace(workspaceId, nameof(workspaceId));

        var workspace = await workspaceService.LoadWorkspaceAsync(workspaceId);
        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{workspaceId}' not found.");

        var settings = appSettings.CurrentValue;
        var cityName = CityNames.GetRandom();
        var branch = await gitService.GetCurrentBranchAsync(workspace.RepoLocalPath);
        var session = new Session
        {
            Model = model,
            Provider = provider,
            WorkspaceId = workspaceId,
            CityName = cityName,
            Title = cityName,
            Git = { IsLocalDir = true, WorktreePath = workspace.RepoLocalPath, BranchName = branch ?? "" },
            EffortLevel = settings.DefaultEffortLevel,
            PermissionMode = settings.DefaultPermissionMode
        };
        session.TransitionStatus(SessionStatus.Ready);

        await SaveSessionAsync(session);
        logger.LogInformation("로컬 디렉토리 세션 {SessionId} ({CityName}) 생성됨 (워크스페이스: {WorkspaceId})",
            session.Id, cityName, workspaceId);

        await hooksEngine.FireAsync(HookEvent.OnSessionCreate, new Dictionary<string, string>
        {
            ["SEORO_SESSION_ID"] = session.Id,
            ["SEORO_CITY_NAME"] = cityName,
            ["SEORO_WORKSPACE_ID"] = workspaceId
        });

        return session;
    }

    public async Task<Session> CreatePendingSessionAsync(string model, string workspaceId, string provider = "claude")
    {
        Guard.NotNullOrWhiteSpace(model, nameof(model));
        Guard.NotNullOrWhiteSpace(workspaceId, nameof(workspaceId));

        var workspace = await workspaceService.LoadWorkspaceAsync(workspaceId);
        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{workspaceId}' not found.");

        await EnforceSessionLimitAsync(workspaceId);

        var settings = appSettings.CurrentValue;
        var cityName = CityNames.GetRandom();
        var session = new Session
        {
            Model = model,
            Provider = provider,
            WorkspaceId = workspaceId,
            CityName = cityName,
            Title = cityName,
            EffortLevel = settings.DefaultEffortLevel,
            PermissionMode = settings.DefaultPermissionMode
        };
        session.TransitionStatus(SessionStatus.Pending);

        await SaveSessionAsync(session);
        logger.LogInformation("세션 {SessionId} ({CityName}) 생성됨 (워크스페이스: {WorkspaceId})", session.Id,
            cityName, workspaceId);

        await hooksEngine.FireAsync(HookEvent.OnSessionCreate, new Dictionary<string, string>
        {
            ["SEORO_SESSION_ID"] = session.Id,
            ["SEORO_CITY_NAME"] = cityName,
            ["SEORO_WORKSPACE_ID"] = workspaceId
        });

        return session;
    }

    public async Task<Session> CreateSessionAsync(string model, string workspaceId, string baseBranch, string provider = "claude")
    {
        Guard.NotNullOrWhiteSpace(model, nameof(model));
        Guard.NotNullOrWhiteSpace(workspaceId, nameof(workspaceId));
        Guard.NotNullOrWhiteSpace(baseBranch, nameof(baseBranch));

        var workspace = await workspaceService.LoadWorkspaceAsync(workspaceId);
        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{workspaceId}' not found.");

        await EnforceSessionLimitAsync(workspaceId);

        var branchName = $"{SeoroConstants.BranchPrefix}{DateTime.Now:yyyyMMdd-HHmmss}";
        var worktreesDir = await workspaceService.GetWorktreesDirAsync();

        var settings = appSettings.CurrentValue;
        var session = new Session
        {
            Model = model,
            Provider = provider,
            WorkspaceId = workspaceId,
            Git = { BranchName = branchName, BaseBranch = baseBranch },
            EffortLevel = settings.DefaultEffortLevel,
            PermissionMode = settings.DefaultPermissionMode
        };
        session.TransitionStatus(SessionStatus.Initializing);

        session.Git.WorktreePath = Path.Combine(worktreesDir, session.Id);

        try
        {
            var result = await gitService.AddWorktreeAsync(
                workspace.RepoLocalPath, session.Git.WorktreePath, branchName, baseBranch);

            if (!result.Success)
            {
                session.TransitionStatus(SessionStatus.Error);
                session.Error = AppError.WorktreeCreation(result.Error);
            }
            else
            {
                session.Git.BaseCommit =
                    await gitService.ResolveCommitHashAsync(workspace.RepoLocalPath, baseBranch) ?? "";
                session.TransitionStatus(SessionStatus.Ready);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "워크스페이스 {WorkspaceId}의 세션 워크트리 생성 실패", workspaceId);
            session.TransitionStatus(SessionStatus.Error);
            session.Error = AppError.FromException(ErrorCode.WorktreeCreationFailed, ex);
        }

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
                logger.LogDebug("세션 {SessionId}의 워크트리 초기화 건너뜀: 상태는 {Status}", sessionId,
                    session.Status);
                return session;
            }

            var workspace = await workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
            if (workspace == null)
                throw new InvalidOperationException($"Workspace '{session.WorkspaceId}' not found.");

            var branchName = $"{SeoroConstants.BranchPrefix}{DateTime.Now:yyyyMMdd-HHmmss}";
            var worktreesDir = await workspaceService.GetWorktreesDirAsync();

            session.Git.BranchName = branchName;
            session.Git.BaseBranch = baseBranch;
            session.Git.WorktreePath = Path.Combine(worktreesDir, session.Id);
            session.TransitionStatus(SessionStatus.Initializing);

            try
            {
                var result = await gitService.AddWorktreeAsync(
                    workspace.RepoLocalPath, session.Git.WorktreePath, branchName, baseBranch);

                if (!result.Success)
                {
                    session.TransitionStatus(SessionStatus.Error);
                    session.Error = AppError.WorktreeCreation(result.Error);
                }
                else
                {
                    session.Git.BaseCommit =
                        await gitService.ResolveCommitHashAsync(workspace.RepoLocalPath, baseBranch) ?? "";
                    session.TransitionStatus(SessionStatus.Ready);
                    // Initialize .context/ directory for collaboration
                    await contextService.EnsureContextDirectoryAsync(session.Git.WorktreePath);
                    logger.LogInformation("세션 {SessionId}의 워크트리 초기화됨 (브랜치: {Branch})", sessionId,
                        branchName);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "세션 {SessionId}의 워크트리 초기화 실패", sessionId);
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

    public async Task<Session> RebaseWorktreeAsync(string sessionId, string newBaseBranch)
    {
        Guard.NotNullOrWhiteSpace(sessionId, nameof(sessionId));
        Guard.NotNullOrWhiteSpace(newBaseBranch, nameof(newBaseBranch));

        var semaphore = _worktreeInitLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            _sessionCache.TryRemove(sessionId, out _);

            var session = await LoadSessionAsync(sessionId);
            if (session == null)
                throw new InvalidOperationException($"Session '{sessionId}' not found.");

            if (session.Status != SessionStatus.Ready)
            {
                logger.LogDebug("세션 {SessionId}의 워크트리 리베이스 건너뜀: 상태는 {Status}", sessionId,
                    session.Status);
                return session;
            }

            // No-op if already on the requested base branch
            if (session.Git.BaseBranch == newBaseBranch)
                return session;

            var workspace = await workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
            if (workspace == null)
                throw new InvalidOperationException($"Workspace '{session.WorkspaceId}' not found.");

            // Tear down existing worktree and branch
            var oldWorktreePath = session.Git.WorktreePath;
            var oldBranchName = session.Git.BranchName;

            if (!string.IsNullOrEmpty(oldWorktreePath))
                await gitService.RemoveWorktreeAsync(workspace.RepoLocalPath, oldWorktreePath);
            if (!string.IsNullOrEmpty(oldBranchName))
                await gitService.DeleteBranchAsync(workspace.RepoLocalPath, oldBranchName);

            // Create new worktree on the new base branch
            var branchName = $"{SeoroConstants.BranchPrefix}{DateTime.Now:yyyyMMdd-HHmmss}";
            var worktreesDir = await workspaceService.GetWorktreesDirAsync();

            session.Git.BranchName = branchName;
            session.Git.BaseBranch = newBaseBranch;
            session.Git.WorktreePath = Path.Combine(worktreesDir, session.Id);

            try
            {
                var result = await gitService.AddWorktreeAsync(
                    workspace.RepoLocalPath, session.Git.WorktreePath, branchName, newBaseBranch);

                if (!result.Success)
                {
                    session.TransitionStatus(SessionStatus.Error);
                    session.Error = AppError.WorktreeCreation(result.Error);
                }
                else
                {
                    session.Git.BaseCommit =
                        await gitService.ResolveCommitHashAsync(workspace.RepoLocalPath, newBaseBranch) ?? "";
                    await contextService.EnsureContextDirectoryAsync(session.Git.WorktreePath);
                    logger.LogInformation(
                        "세션 {SessionId}의 워크트리 리베이스됨 (브랜치: {BaseBranch})", sessionId, newBaseBranch);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "세션 {SessionId}의 워크트리 리베이스 실패", sessionId);
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

    private static List<ChatMessage> TruncateToolOutputs(List<ChatMessage> messages)
    {
        return messages.Select(msg =>
        {
            var needsTruncation = msg.ToolCalls.Any(tc => tc.Output.Length > MaxToolOutputLength)
                                  || msg.Parts.Any(p =>
                                      p.ToolCall != null && p.ToolCall.Output.Length > MaxToolOutputLength);

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

    private static string SanitizePathSegment(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
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

    /// <summary>
    ///     Checks if a JSON string is missing the current $schemaVersion — meaning it needs migration/upgrade.
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
            logger.LogWarning(ex, "세션 JSON 스키마 업그레이드 확인 실패");
            return false;
        }
    }

    private async Task EnforceSessionLimitAsync(string workspaceId)
    {
        var active = await GetSessionsByWorkspaceAsync(workspaceId);
        var activeCount = active.Count(s => s.Status is not SessionStatus.Archived and not SessionStatus.Error);
        if (activeCount >= SeoroConstants.MaxActiveSessionsPerWorkspace)
            throw new InvalidOperationException(
                $"Workspace has {activeCount} active sessions (limit: {SeoroConstants.MaxActiveSessionsPerWorkspace}). Archive or delete existing sessions first.");
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
                    // Clear stale worktree paths (crash recovery / external deletion)
                    if (!string.IsNullOrEmpty(session.Git.WorktreePath)
                        && !session.Git.IsLocalDir
                        && !Directory.Exists(session.Git.WorktreePath))
                    {
                        logger.LogWarning("세션 {Id}의 손상된 WorktreePath 제거: {Path}", session.Id,
                            session.Git.WorktreePath);
                        session.Git.WorktreePath = string.Empty;
                        needsMigration = true;
                    }

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
                logger.LogWarning(ex, "손상된 세션 파일 건너뜀: {File}", file);
            }

            return null;
        });

        var results = await Task.WhenAll(tasks);
        foreach (var session in results)
            if (session != null)
                _metadataCache[session.Id] = session;

        _cacheInitialized = true;
        logger.LogDebug("세션 메타데이터 캐시 초기화됨: {Count}개 세션 로드됨", _metadataCache.Count);
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

        logger.LogDebug("세션 캐시 용량 제한 실행: {Count}개 제거됨, 한계 {Max}", toEvict.Count,
            MaxSessionCacheEntries);
    }

    private void ScavengeExpiredSessions()
    {
        var now = DateTime.UtcNow;
        if (now - _lastScavengeUtc < ScavengeInterval)
            return;
        _lastScavengeUtc = now;

        var removed = 0;
        foreach (var kvp in _sessionCache)
            if (now - kvp.Value.LoadedAt >= SessionCacheTtl)
                if (_sessionCache.TryRemove(kvp.Key, out _))
                    removed++;

        if (removed > 0)
            logger.LogDebug("세션 캐시 정소 완료: {Removed}개 제거됨, {Remaining}개 항목 남음", removed,
                _sessionCache.Count);
    }
}