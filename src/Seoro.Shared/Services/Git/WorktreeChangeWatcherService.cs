using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Git;

/// <summary>
///     활성 세션 워크트리의 파일 변경을 감시하는 공유 서비스.
///     GitView/SidebarExplorer 가 각자 운영하던 FileSystemWatcher 를 하나로 통합해
///     변경 1회당 FSW 콜백을 한 계통으로 줄인다. debounce 후
///     ① GitService 의 status 캐시를 무효화하고 ② <see cref="FilesChanged"/> 를 발생시킨다.
///     <see cref="ConflictWatcherService"/>의 세션 추적/debounce 패턴을 따른다.
/// </summary>
public interface IWorktreeChangeWatcherService : IDisposable
{
    /// <summary>워크트리 파일 변경이 감지되면 debounce(1.5초) 후 발생. 인자: 워크트리 경로.</summary>
    event Action<string>? FilesChanged;
}

public class WorktreeChangeWatcherService : IWorktreeChangeWatcherService
{
    private const int DebounceMs = 1500;

    private readonly IGitService _gitService;
    private readonly ILogger<WorktreeChangeWatcherService> _logger;
    private readonly IDisposable _sessionChangeSub;
    private readonly object _lock = new();
    private volatile bool _disposed;

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private string? _watchedDir;

    public WorktreeChangeWatcherService(
        IChatEventBus eventBus,
        IGitService gitService,
        ILogger<WorktreeChangeWatcherService> logger)
    {
        _gitService = gitService;
        _logger = logger;

        _sessionChangeSub = eventBus.Subscribe<SessionChangedEvent>(evt =>
        {
            if (evt.NewSession != null)
                Watch(evt.NewSession);
            else
                Unwatch();
        });
    }

    public event Action<string>? FilesChanged;

    public void Dispose()
    {
        _disposed = true;
        _sessionChangeSub.Dispose();
        Unwatch();
    }

    private void Watch(Session session)
    {
        var workDir = session.Git.WorktreePath;
        if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
        {
            Unwatch();
            return;
        }

        lock (_lock)
        {
            if (_watchedDir == workDir)
                return;

            StopWatchingLocked();
            _watchedDir = workDir;

            try
            {
                _watcher = new FileSystemWatcher(workDir)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += OnFsEvent;
                _watcher.Created += OnFsEvent;
                _watcher.Deleted += OnFsEvent;
                _watcher.Renamed += OnFsEvent;
                // 버퍼 오버플로(npm install 등 대량 변경) — 이벤트 유실이므로 변경으로 간주해 트리거
                _watcher.Error += (_, _) => ScheduleEmit();

                _logger.LogDebug("WorktreeChangeWatcher: 감시 시작 — {Dir}", workDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WorktreeChangeWatcher 초기화 실패 — {Dir}", workDir);
                _watchedDir = null;
            }
        }
    }

    private void Unwatch()
    {
        lock (_lock)
        {
            StopWatchingLocked();
            _watchedDir = null;
        }
    }

    private void StopWatchingLocked()
    {
        if (_watcher != null)
        {
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
            catch
            {
                // best-effort
            }

            _watcher = null;
        }

        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // .git 내부 변경은 제외 (status 재질의 루프 방지)
        if (e.FullPath.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar) ||
            e.FullPath.EndsWith(Path.DirectorySeparatorChar + ".git"))
            return;

        ScheduleEmit();
    }

    private void ScheduleEmit()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => Emit(), null, DebounceMs, Timeout.Infinite);
        }
    }

    private void Emit()
    {
        if (_disposed)
            return;

        string? dir;
        lock (_lock)
        {
            dir = _watchedDir;
        }

        if (dir == null)
            return;

        try
        {
            // 캐시 무효화를 먼저 — 구독자가 즉시 재질의해도 신선한 결과를 받도록
            _ = _gitService.InvalidateStatusCacheAsync(dir);
            FilesChanged?.Invoke(dir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WorktreeChangeWatcher 이벤트 발행 실패 — {Dir}", dir);
        }
    }
}
