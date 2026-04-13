using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Git;

/// <summary>
///     워크트리 머지 충돌 상태를 라이브로 감시하는 서비스.
///     <c>.git/MERGE_HEAD</c> 파일 생성/삭제를 <see cref="FileSystemWatcher"/>로 포착해
///     <see cref="ConflictDetectedEvent"/>를 발행한다. 상태는 저장하지 않고 매번 git 에
///     재질의하므로 정확도가 높다 (PR #245 함정 회피).
///
///     설계:
///     - <see cref="Watch(Session)"/> 는 현재 활성 세션 워크트리를 자동으로 쫓아간다
///       (<see cref="SessionChangedEvent"/> 구독).
///     - <see cref="WatchExtraPath"/> / <see cref="UnwatchExtraPath"/> 는 Alt B(헤드리스 AI 로
///       임시 클론 충돌 해결) 사전 포인트. 1단계 Alt A 에서는 호출되지 않지만 향후 확장을 위해 유지.
///     - <see cref="GitBranchWatcherService"/>의 debounce/FSW 패턴을 그대로 따른다.
/// </summary>
public interface IConflictWatcherService : IDisposable
{
    void Watch(Session session);
    void Unwatch();
    ValueTask<bool> IsInConflictAsync(string workingDir, CancellationToken ct = default);
    void WatchExtraPath(string workingDir);
    void UnwatchExtraPath(string workingDir);
}

public class ConflictWatcherService : IConflictWatcherService
{
    private const int DebounceMs = 200;

    private readonly IChatEventBus _eventBus;
    private readonly IGitService _gitService;
    private readonly ILogger<ConflictWatcherService> _logger;
    private readonly IDisposable _sessionChangeSub;

    // 감시 핸들: 경로별 watcher + 마지막 entered 상태 + debounce 타이머.
    // 동시 접근은 많지 않지만 세션 전환과 수동 호출이 경합할 수 있어 lock 으로 단순 보호.
    private readonly object _lock = new();
    private readonly Dictionary<string, WatchHandle> _watches = new(StringComparer.Ordinal);

    private string? _activeSessionWorkDir;

    public ConflictWatcherService(
        IChatEventBus eventBus,
        IGitService gitService,
        ILogger<ConflictWatcherService> logger)
    {
        _eventBus = eventBus;
        _gitService = gitService;
        _logger = logger;

        // 활성 세션이 바뀌면 자동으로 Watch/Unwatch 재설정.
        _sessionChangeSub = eventBus.Subscribe<SessionChangedEvent>(evt =>
        {
            if (evt.NewSession != null)
                Watch(evt.NewSession);
            else
                Unwatch();
        });
    }

    public void Dispose()
    {
        _sessionChangeSub.Dispose();
        lock (_lock)
        {
            foreach (var handle in _watches.Values)
                handle.Dispose();
            _watches.Clear();
            _activeSessionWorkDir = null;
        }
    }

    public void Watch(Session session)
    {
        if (session.Git.IsLocalDir || string.IsNullOrEmpty(session.Git.WorktreePath))
        {
            // 로컬 디렉터리 세션은 MergeToolbar 가 숨겨지므로 감시 불필요.
            Unwatch();
            return;
        }

        var workDir = session.Git.WorktreePath;
        lock (_lock)
        {
            if (_activeSessionWorkDir == workDir)
                return; // 이미 같은 경로 감시 중
            UnwatchInternal(_activeSessionWorkDir);
            _activeSessionWorkDir = workDir;
            StartWatchingLocked(workDir);
        }
    }

    public void Unwatch()
    {
        lock (_lock)
        {
            UnwatchInternal(_activeSessionWorkDir);
            _activeSessionWorkDir = null;
        }
    }

    public void WatchExtraPath(string workingDir)
    {
        if (string.IsNullOrWhiteSpace(workingDir))
            return;
        lock (_lock)
        {
            StartWatchingLocked(workingDir);
        }
    }

    public void UnwatchExtraPath(string workingDir)
    {
        lock (_lock)
        {
            UnwatchInternal(workingDir);
        }
    }

    public async ValueTask<bool> IsInConflictAsync(string workingDir, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workingDir))
            return false;
        lock (_lock)
        {
            if (_watches.TryGetValue(workingDir, out var handle))
                return handle.LastEntered;
        }

        // 감시 중이 아니어도 일회성 질의는 허용 — git 에 직접 물어본다.
        // sync-over-async 금지: UI 스레드 SyncContext 에서 continuation 이 돌아올 때 데드락 발생.
        try
        {
            return await _gitService.HasUnresolvedConflictsAsync(workingDir, ct).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    // ────────────────────────────────────────────────
    //  내부 구현
    // ────────────────────────────────────────────────

    private void StartWatchingLocked(string workingDir)
    {
        if (_watches.ContainsKey(workingDir))
            return;

        var gitDir = GitBranchWatcherService.ResolveGitDir(workingDir);
        if (gitDir == null)
        {
            _logger.LogDebug("ConflictWatcher: .git 경로 해석 실패 — {Dir}", workingDir);
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(gitDir, "MERGE_HEAD")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            var handle = new WatchHandle(workingDir, watcher);
            _watches[workingDir] = handle;

            // 초기 상태 반영 — 감시 시작 시점에 이미 MERGE_HEAD 가 있을 수 있다.
            _ = EmitIfChangedAsync(workingDir);

            watcher.Created += (_, _) => DebouncedCheck(workingDir);
            watcher.Changed += (_, _) => DebouncedCheck(workingDir);
            watcher.Deleted += (_, _) => DebouncedCheck(workingDir);
            watcher.Renamed += (_, _) => DebouncedCheck(workingDir);

            _logger.LogDebug("ConflictWatcher: 감시 시작 — {Dir}", workingDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConflictWatcher 감시 초기화 실패 — {Dir}", workingDir);
        }
    }

    private void UnwatchInternal(string? workingDir)
    {
        if (string.IsNullOrWhiteSpace(workingDir))
            return;
        if (_watches.Remove(workingDir, out var handle))
        {
            handle.Dispose();
            _logger.LogDebug("ConflictWatcher: 감시 종료 — {Dir}", workingDir);
        }
    }

    private void DebouncedCheck(string workingDir)
    {
        WatchHandle? handle;
        lock (_lock)
        {
            if (!_watches.TryGetValue(workingDir, out handle))
                return;
        }

        handle.Timer?.Dispose();
        handle.Timer = new Timer(_ => _ = EmitIfChangedAsync(workingDir),
            null, DebounceMs, Timeout.Infinite);
    }

    private async Task EmitIfChangedAsync(string workingDir)
    {
        try
        {
            var inConflict = await _gitService.HasUnresolvedConflictsAsync(workingDir);

            WatchHandle? handle;
            lock (_lock)
            {
                if (!_watches.TryGetValue(workingDir, out handle))
                    return;
                if (handle.LastEntered == inConflict)
                    return; // 변화 없음 → 이벤트 생략
                handle.LastEntered = inConflict;
            }

            _logger.LogInformation("ConflictWatcher: 상태 변경 — {Dir} entered={Entered}", workingDir, inConflict);
            _eventBus.Publish(new ConflictDetectedEvent(workingDir, inConflict));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConflictWatcher 상태 확인 실패 — {Dir}", workingDir);
        }
    }

    private sealed class WatchHandle : IDisposable
    {
        public WatchHandle(string workingDir, FileSystemWatcher watcher)
        {
            WorkingDir = workingDir;
            Watcher = watcher;
        }

        public string WorkingDir { get; }
        public FileSystemWatcher Watcher { get; }
        public Timer? Timer { get; set; }
        public bool LastEntered { get; set; }

        public void Dispose()
        {
            try
            {
                Watcher.EnableRaisingEvents = false;
                Watcher.Dispose();
            }
            catch
            {
                // best-effort
            }

            Timer?.Dispose();
        }
    }
}
