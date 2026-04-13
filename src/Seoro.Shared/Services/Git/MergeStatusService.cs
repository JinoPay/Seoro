using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Git;

/// <summary>
///     세션별 머지 상태 종류. 모델로 저장하지 않고 매번 git 에서 라이브로 계산한다
///     (PR #245 교훈: 모델/폴링 상태 저장 금지).
/// </summary>
public enum MergeStatusKind
{
    /// <summary>아직 계산 전 — 초기 로딩 상태.</summary>
    Unknown,

    /// <summary>깨끗. 머지 가능하고 타겟과 충돌 없음.</summary>
    Clean,

    /// <summary>타겟 브랜치가 앞섰음 (source 가 stale). 머지 시 덮어쓰기 위험.</summary>
    BehindTarget,

    /// <summary><c>git merge-tree</c> 시뮬레이션 결과 충돌 예상.</summary>
    ConflictExpected,

    /// <summary>세션 워크트리에 미커밋 변경이 있음 — 머지 전 커밋/스태시 필요.</summary>
    UncommittedDirty,

    /// <summary>실제 머지 진행 중 (<c>.git/MERGE_HEAD</c> 존재) — ConflictWatcher 신호.</summary>
    InConflict,

    /// <summary>fetch 실패 — 캐시된 이전 값 사용 중이며 실시간 비교 불가.</summary>
    NetworkError
}

/// <summary>
///     단일 세션의 머지 상태 스냅샷. 인메모리 캐시에만 저장.
/// </summary>
public sealed record MergeStatus(
    MergeStatusKind Kind,
    int? AheadCount,
    int? BehindCount,
    IReadOnlyList<string>? ConflictingFiles,
    int UncommittedChangeCount,
    DateTime LastCheckedAt,
    string? ErrorMessage)
{
    public static MergeStatus Unknown { get; } =
        new(MergeStatusKind.Unknown, null, null, null, 0, DateTime.MinValue, null);
}

/// <summary>
///     세션별 머지 상태를 라이브 추적하는 중앙 서비스.
///     컨덕터 스타일 자동화의 핵심 — 여러 트리거(세션 전환, 스트림 종료, 브랜치 변경, 액션 직전)에서
///     자동으로 갱신되고 UI(MergeToolbar, SidebarChanges 하이라이트)가 구독한다.
/// </summary>
public interface IMergeStatusService : IDisposable
{
    /// <summary>세션의 현재 머지 상태 스냅샷. 아직 계산 전이면 <see cref="MergeStatus.Unknown"/>.</summary>
    MergeStatus GetCurrent(string sessionId);

    /// <summary>상태가 변경되면 호출되는 이벤트. MergeToolbar/SidebarChanges 가 구독.</summary>
    event Action<string>? StatusChanged;

    /// <summary>
    ///     해당 세션의 상태를 다시 계산한다. 호출자가 타겟 브랜치를 알고 있다면 <paramref name="targetBranchOverride"/>로 전달.
    ///     그렇지 않으면 <see cref="SetTargetBranch"/>로 지정된 값을 사용한다.
    /// </summary>
    Task RefreshAsync(string sessionId, string? targetBranchOverride = null,
        CancellationToken ct = default);

    /// <summary>MergeToolbar 에서 사용자가 선택한 타겟 브랜치를 세션별로 저장한다.</summary>
    void SetTargetBranch(string sessionId, string targetBranch);

    /// <summary>저장된 타겟 브랜치 (없으면 null).</summary>
    string? GetTargetBranch(string sessionId);
}

public class MergeStatusService : IMergeStatusService
{
    // 같은 세션에 대한 RefreshAsync 요청이 30초 내에 연속으로 들어오면 병합 (debounce).
    // 수동 새로고침 버튼은 이 제한을 우회하기 위해 targetBranchOverride 를 명시적으로 전달한다.
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, MergeStatus> _cache = new();
    private readonly ConcurrentDictionary<string, string> _targetBranches = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastRefresh = new();

    private readonly IGitService _gitService;
    private readonly ISessionService _sessionService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IConflictWatcherService _conflictWatcher;
    private readonly ILogger<MergeStatusService> _logger;

    private readonly IDisposable _sessionChangeSub;
    private readonly IDisposable _streamingStoppedSub;
    private readonly IDisposable _branchChangedSub;
    private readonly IDisposable _conflictSub;

    public event Action<string>? StatusChanged;

    public MergeStatusService(
        IChatEventBus eventBus,
        IGitService gitService,
        ISessionService sessionService,
        IWorkspaceService workspaceService,
        IConflictWatcherService conflictWatcher,
        ILogger<MergeStatusService> logger)
    {
        _gitService = gitService;
        _sessionService = sessionService;
        _workspaceService = workspaceService;
        _conflictWatcher = conflictWatcher;
        _logger = logger;

        // 자동 갱신 트리거: 세션 전환, 스트림 종료, 브랜치 변경, 충돌 진입/해제.
        _sessionChangeSub = eventBus.Subscribe<SessionChangedEvent>(evt =>
        {
            if (evt.NewSession != null)
                _ = RefreshAsync(evt.NewSession.Id);
        });

        _streamingStoppedSub = eventBus.Subscribe<StreamingStoppedEvent>(evt =>
        {
            // 스트리밍 종료 후 커밋이 새로 생겼을 수 있으므로 debounce 우회.
            _lastRefresh.TryRemove(evt.SessionId, out _);
            _ = RefreshAsync(evt.SessionId);
        });

        _branchChangedSub = eventBus.Subscribe<BranchChangedEvent>(evt =>
        {
            _ = RefreshAsync(evt.SessionId);
        });

        // 충돌 진입/해제 즉시 Kind 를 InConflict/직전 상태로 돌려야 하므로 debounce 우회.
        _conflictSub = eventBus.Subscribe<ConflictDetectedEvent>(evt =>
        {
            _ = HandleConflictEventAsync(evt);
        });
    }

    public void Dispose()
    {
        _sessionChangeSub.Dispose();
        _streamingStoppedSub.Dispose();
        _branchChangedSub.Dispose();
        _conflictSub.Dispose();
    }

    public MergeStatus GetCurrent(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return MergeStatus.Unknown;
        return _cache.TryGetValue(sessionId, out var status) ? status : MergeStatus.Unknown;
    }

    public void SetTargetBranch(string sessionId, string targetBranch)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(targetBranch))
            return;
        _targetBranches[sessionId] = targetBranch;
        _logger.LogDebug("MergeStatus 타겟 브랜치 설정: session={Id} target={Target}", sessionId, targetBranch);
    }

    public string? GetTargetBranch(string sessionId)
    {
        return _targetBranches.TryGetValue(sessionId, out var t) ? t : null;
    }

    public async Task RefreshAsync(string sessionId, string? targetBranchOverride = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;

        // debounce — targetBranchOverride 가 명시적으로 제공되면 우회한다 (수동 새로고침 용).
        if (targetBranchOverride == null)
        {
            var now = DateTime.UtcNow;
            if (_lastRefresh.TryGetValue(sessionId, out var last) && now - last < DebounceInterval)
            {
                _logger.LogDebug("MergeStatus Refresh debounce: session={Id} 마지막={Last}", sessionId, last);
                return;
            }
            _lastRefresh[sessionId] = now;
        }
        else
        {
            SetTargetBranch(sessionId, targetBranchOverride);
            _lastRefresh[sessionId] = DateTime.UtcNow;
        }

        try
        {
            var status = await ComputeStatusAsync(sessionId, ct);
            _cache[sessionId] = status;
            _logger.LogDebug("MergeStatus 갱신 완료: session={Id} kind={Kind} ahead={Ahead} behind={Behind} conflicts={ConflictCount} uncommitted={Uncommitted}",
                sessionId, status.Kind, status.AheadCount, status.BehindCount,
                status.ConflictingFiles?.Count ?? 0, status.UncommittedChangeCount);
            StatusChanged?.Invoke(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MergeStatus 갱신 실패: session={Id}", sessionId);
        }
    }

    /// <summary>
    ///     단일 세션에 대해 git 상태를 조합해 <see cref="MergeStatus"/>를 만든다.
    ///     컨플릭트 > stale > 충돌 예상 > uncommitted > clean 순서로 우선순위를 매긴다.
    /// </summary>
    private async Task<MergeStatus> ComputeStatusAsync(string sessionId, CancellationToken ct)
    {
        var session = await _sessionService.LoadSessionAsync(sessionId);
        if (session == null || string.IsNullOrEmpty(session.Git.WorktreePath))
            return MergeStatus.Unknown;

        var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
        if (workspace == null || string.IsNullOrEmpty(workspace.RepoLocalPath))
            return MergeStatus.Unknown;

        var now = DateTime.UtcNow;

        // 로컬 디렉토리 세션은 워크트리 머지가 아닌 origin/<branchName> 기준 push 감지만 수행.
        if (session.Git.IsLocalDir && !string.IsNullOrEmpty(session.Git.BranchName))
        {
            int localUncommitted;
            try
            {
                var uc = await _gitService.GetUncommittedChangesAsync(session.Git.WorktreePath, ct);
                localUncommitted = uc.Count;
            }
            catch { localUncommitted = 0; }

            var remoteRef = $"origin/{BranchRefNormalizer.Normalize(session.Git.BranchName)}";
            var cmp = await _gitService.FetchAndCompareAsync(
                workspace.RepoLocalPath, session.Git.BranchName, remoteRef, ct);
            if (cmp == null)
                return new MergeStatus(MergeStatusKind.NetworkError,
                    null, null, null, localUncommitted, now, "원격 fetch 에 실패했습니다.");

            var localKind = localUncommitted > 0 ? MergeStatusKind.UncommittedDirty : MergeStatusKind.Clean;
            return new MergeStatus(localKind, cmp.Value.Ahead, cmp.Value.Behind, null, localUncommitted, now, null);
        }

        if (session.Git.IsLocalDir)
            return MergeStatus.Unknown;

        // 1) 실제 머지 중인가 — 최우선. ConflictWatcher 의 캐시 활용.
        if (await _conflictWatcher.IsInConflictAsync(session.Git.WorktreePath, ct))
        {
            var conflictFiles = await GetWorktreeConflictFilesAsync(session.Git.WorktreePath, ct);
            return new MergeStatus(MergeStatusKind.InConflict,
                null, null, conflictFiles, 0, now, null);
        }

        // 2) 미커밋 변경 수 — 모든 Kind 에서 보조 정보로 쓰이므로 미리 계산.
        int uncommittedCount;
        try
        {
            var uncommitted = await _gitService.GetUncommittedChangesAsync(session.Git.WorktreePath, ct);
            uncommittedCount = uncommitted.Count;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "uncommitted 조회 실패 — 0으로 폴백");
            uncommittedCount = 0;
        }

        // 3) 타겟 브랜치가 없으면 아직 상태 계산 불가.
        var targetBranch = GetTargetBranch(sessionId);
        if (string.IsNullOrWhiteSpace(targetBranch))
        {
            // 타겟 미지정이어도 uncommitted 경고는 의미 있으므로 Clean/UncommittedDirty 중 하나로 돌린다.
            return new MergeStatus(
                uncommittedCount > 0 ? MergeStatusKind.UncommittedDirty : MergeStatusKind.Unknown,
                null, null, null, uncommittedCount, now, null);
        }

        // 4) fetch + ahead/behind (10초 타임아웃, 실패 시 NetworkError).
        var compare = await _gitService.FetchAndCompareAsync(
            workspace.RepoLocalPath, session.Git.BranchName, targetBranch, ct);
        if (compare == null)
        {
            return new MergeStatus(MergeStatusKind.NetworkError,
                null, null, null, uncommittedCount, now, "원격 fetch 에 실패했습니다.");
        }

        var ahead = compare.Value.Ahead;
        var behind = compare.Value.Behind;

        // 5) merge-tree 시뮬레이션.
        var sim = await _gitService.SimulateMergeAsync(
            workspace.RepoLocalPath, session.Git.BranchName, targetBranch, ct);

        if (sim.WouldConflict)
        {
            return new MergeStatus(MergeStatusKind.ConflictExpected,
                ahead, behind, sim.ConflictingFiles, uncommittedCount, now, null);
        }

        if (behind > 0)
        {
            return new MergeStatus(MergeStatusKind.BehindTarget,
                ahead, behind, null, uncommittedCount, now, null);
        }

        // 6) 나머지는 Clean 또는 UncommittedDirty.
        var kind = uncommittedCount > 0 ? MergeStatusKind.UncommittedDirty : MergeStatusKind.Clean;
        return new MergeStatus(kind, ahead, behind, null, uncommittedCount, now, null);
    }

    /// <summary>
    ///     충돌 상태의 워크트리에서 UU/AA 등 충돌 파일 목록을 돌려준다.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetWorktreeConflictFilesAsync(string workingDir, CancellationToken ct)
    {
        try
        {
            // GitService 에 별도 API 가 없으므로 porcelain 직접 파싱.
            var porcelain = await _gitService.GetStatusPorcelainAsync(workingDir, ct);
            var files = new List<string>();
            foreach (var line in porcelain)
            {
                if (line.Length < 3) continue;
                var code = line.AsSpan(0, 2);
                if (code is "UU" or "AA" or "DD" or "AU" or "UA" or "DU" or "UD")
                    files.Add(line[3..].Trim());
            }
            return files;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "충돌 파일 목록 조회 실패");
            return [];
        }
    }

    /// <summary>
    ///     ConflictWatcher 이벤트를 받아 현재 세션의 상태를 즉시 갱신한다.
    ///     InConflict → entered=false 로 해제되면 정상 계산 경로로 돌아간다.
    /// </summary>
    private async Task HandleConflictEventAsync(ConflictDetectedEvent evt)
    {
        // 어느 세션의 워크트리가 충돌 중인지 파악해야 한다. 가장 빠른 방법은
        // 캐시된 세션별 워크트리 경로와 비교하는 것. 현재는 단순화를 위해
        // 모든 세션에 대해 재계산하지 않고, 캐시 안에 경로가 일치하는 세션만 갱신.
        foreach (var (sessionId, status) in _cache.ToArray())
        {
            var session = await _sessionService.LoadSessionAsync(sessionId);
            if (session == null) continue;
            if (!string.Equals(session.Git.WorktreePath, evt.WorkingDir, StringComparison.Ordinal))
                continue;

            // 해당 세션 강제 재계산 (debounce 우회를 위해 lastRefresh 초기화).
            _lastRefresh.TryRemove(sessionId, out _);
            _ = RefreshAsync(sessionId);
            break;
        }
    }
}
