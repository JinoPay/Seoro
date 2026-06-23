using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Sessions;

/// <summary>
///     세션의 Git 워크트리 수명주기(생성·리베이스)를 담당한다. 세션 메타데이터의
///     로드/저장은 <see cref="ISessionService"/>에 위임하고, 워크트리 작업은
///     <see cref="IGitService"/>로 수행한다. 세션 CRUD에서 분리된 책임.
/// </summary>
public interface ISessionWorktreeManager
{
    Task<Session> InitializeWorktreeAsync(string sessionId, string baseBranch);
    Task<Session> RebaseWorktreeAsync(string sessionId, string newBaseBranch);
}

public class SessionWorktreeManager(
    IGitService gitService,
    IWorkspaceService workspaceService,
    IContextService contextService,
    ISessionService sessionService,
    ILogger<SessionWorktreeManager> logger) : ISessionWorktreeManager
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _worktreeInitLocks =
        new();

    public async Task<Session> InitializeWorktreeAsync(string sessionId, string baseBranch)
    {
        Guard.NotNullOrWhiteSpace(sessionId, nameof(sessionId));
        Guard.NotNullOrWhiteSpace(baseBranch, nameof(baseBranch));

        var semaphore = _worktreeInitLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            // Invalidate cache so we read the latest state from disk after waiting on the lock
            sessionService.InvalidateSessionCache(sessionId);

            var session = await sessionService.LoadSessionAsync(sessionId);
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
                    await CopyLocalSettingsToWorktreeAsync(workspace.RepoLocalPath, session.Git.WorktreePath);
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

            await sessionService.SaveSessionAsync(session);
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
            sessionService.InvalidateSessionCache(sessionId);

            var session = await sessionService.LoadSessionAsync(sessionId);
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
                    await CopyLocalSettingsToWorktreeAsync(workspace.RepoLocalPath, session.Git.WorktreePath);
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

            await sessionService.SaveSessionAsync(session);
            return session;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task CopyLocalSettingsToWorktreeAsync(string repoPath, string worktreePath)
    {
        var source = Path.Combine(repoPath, ".claude", "settings.local.json");
        if (!File.Exists(source))
            return;

        var destDir = Path.Combine(worktreePath, ".claude");
        Directory.CreateDirectory(destDir);

        var dest = Path.Combine(destDir, "settings.local.json");
        await Task.Run(() => File.Copy(source, dest, overwrite: true));
    }
}
