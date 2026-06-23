using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Sessions;

/// <summary>
///     세션 목록에 표시할 Git diff 통계(추가/삭제 줄 수)를 계산·캐싱한다.
///     "세션 목록 상태"(SessionListState)에서 Git 작업 책임을 분리한 서비스.
/// </summary>
public interface ISessionDiffStatsService
{
    bool TryGetDiffStats(string sessionId, out (int Additions, int Deletions) stats);
    Task LoadForWorkspaceAsync(List<Session> sessions);
    Task RefreshAsync(Session session);
    void Remove(string sessionId);
    event Action? OnChanged;
}

public class SessionListDiffStatsService(IGitService gitService, ILogger<SessionListDiffStatsService> logger)
    : ISessionDiffStatsService
{
    private readonly Dictionary<string, (int Additions, int Deletions)> _cache = new();

    public event Action? OnChanged;

    public bool TryGetDiffStats(string sessionId, out (int Additions, int Deletions) stats)
        => _cache.TryGetValue(sessionId, out stats);

    public void Remove(string sessionId) => _cache.Remove(sessionId);

    public async Task LoadForWorkspaceAsync(List<Session> sessions)
    {
        foreach (var session in sessions)
        {
            if (session.Status == SessionStatus.Pending || session.Git.IsLocalDir
                                                        || string.IsNullOrEmpty(session.Git.WorktreePath)
                                                        || !Directory.Exists(session.Git.WorktreePath)
                                                        || string.IsNullOrEmpty(session.Git.BaseBranch))
                continue;

            try
            {
                var stats = await gitService.GetDiffStatAsync(session.Git.WorktreePath, session.Git.GetDiffBase());
                if (stats.Additions > 0 || stats.Deletions > 0)
                {
                    _cache[session.Id] = stats;
                    OnChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to load diff stats for session {SessionId}", session.Id);
            }
        }
    }

    public async Task RefreshAsync(Session session)
    {
        if (session.Git.IsLocalDir
            || string.IsNullOrEmpty(session.Git.WorktreePath)
            || !Directory.Exists(session.Git.WorktreePath)
            || string.IsNullOrEmpty(session.Git.BaseBranch))
            return;

        try
        {
            var stats = await gitService.GetDiffStatAsync(session.Git.WorktreePath, session.Git.GetDiffBase());
            _cache[session.Id] = stats;
            OnChanged?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to refresh diff stats for session {SessionId}", session.Id);
        }
    }
}
