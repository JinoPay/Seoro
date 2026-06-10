using Seoro.Shared.UiKit;

namespace Seoro.Shared.Services.Ui;

/// <summary>
/// IToastService 구현. 호스트(ToastHost) 부착 전에도 안전하게 큐잉되며
/// (이벤트 구독자가 없어도 리스트에 누적), 표시 시간 경과 후 자동 제거한다.
/// </summary>
public sealed class ToastService : IToastService, IDisposable
{
    public const int MaxVisible = 3;

    private readonly object _lock = new();
    private readonly List<ToastInstance> _toasts = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _timers = [];

    public event Action? OnToastsUpdated;

    public IReadOnlyList<ToastInstance> Toasts
    {
        get
        {
            lock (_lock)
            {
                return _toasts.Take(MaxVisible).ToList();
            }
        }
    }

    public ToastInstance? Add(string message, ToastSeverity severity = ToastSeverity.Normal,
        Action<ToastOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var options = new ToastOptions();
        configure?.Invoke(options);

        ToastInstance toast;
        lock (_lock)
        {
            // 중복 방지 (Mud PreventDuplicates 동작 복제)
            if (_toasts.Any(t => t.Message == message && t.Severity == severity))
                return null;

            toast = new ToastInstance { Message = message, Severity = severity, Options = options };
            _toasts.Add(toast);

            var cts = new CancellationTokenSource();
            _timers[toast.Id] = cts;
            _ = ExpireAsync(toast, options.VisibleStateDuration, cts.Token);
        }

        OnToastsUpdated?.Invoke();
        return toast;
    }

    public void Remove(ToastInstance toast)
    {
        lock (_lock)
        {
            if (!_toasts.Remove(toast))
                return;
            if (_timers.Remove(toast.Id, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        OnToastsUpdated?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _toasts.Clear();
            foreach (var cts in _timers.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _timers.Clear();
        }

        OnToastsUpdated?.Invoke();
    }

    public void Dispose() => Clear();

    private async Task ExpireAsync(ToastInstance toast, int durationMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(Math.Max(durationMs, 500), ct);
            Remove(toast);
        }
        catch (OperationCanceledException)
        {
            // 수동 제거됨
        }
    }
}
