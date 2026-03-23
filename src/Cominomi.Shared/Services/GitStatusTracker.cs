using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cominomi.Shared.Services;

/// <summary>
/// Orchestrates periodic and event-driven git/PR status synchronization.
/// Triggers: session focus change, window focus, periodic timer (45s).
/// </summary>
public class GitStatusTracker : IDisposable
{
    private readonly IChatState _chatState;
    private readonly IChatEventBus _eventBus;
    private readonly ISessionSyncService _syncService;
    private readonly ILogger<GitStatusTracker> _logger;

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private IDisposable? _sessionChangedSub;
    private bool _windowFocused = true;
    private bool _started;
    private const int TickIntervalSeconds = 45;

    public GitStatusTracker(
        IChatState chatState,
        IChatEventBus eventBus,
        ISessionSyncService syncService,
        ILogger<GitStatusTracker> logger)
    {
        _chatState = chatState;
        _eventBus = eventBus;
        _syncService = syncService;
        _logger = logger;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(TickIntervalSeconds));

        _sessionChangedSub = _eventBus.Subscribe<SessionChangedEvent>(OnSessionChanged);

        _ = RunTimerLoopAsync(_cts.Token);

        _logger.LogDebug("GitStatusTracker started (interval={Interval}s)", TickIntervalSeconds);
    }

    [JSInvokable]
    public void OnWindowFocusChanged(bool focused)
    {
        _windowFocused = focused;

        if (focused)
        {
            _logger.LogDebug("Window focus gained, triggering sync");
            _ = SyncCurrentSessionAsync();
        }
    }

    private void OnSessionChanged(SessionChangedEvent evt)
    {
        if (evt.NewSession != null)
        {
            _ = SyncCurrentSessionAsync();
        }
    }

    private async Task RunTimerLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct))
            {
                if (!_windowFocused) continue;
                await SyncCurrentSessionAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitStatusTracker timer loop error");
        }
    }

    private async Task SyncCurrentSessionAsync()
    {
        var session = _chatState.CurrentSession;
        if (session == null) return;
        if (_chatState.IsSessionStreaming(session.Id)) return;

        try
        {
            await _syncService.SyncAsync(session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GitStatusTracker sync failed for session {SessionId}", session.Id);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _timer?.Dispose();
        _sessionChangedSub?.Dispose();
        _started = false;
    }
}
