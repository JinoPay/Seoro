using System.Collections.Concurrent;

namespace Cominomi.Shared.Services;

public class ChatEventBus : IChatEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _lock = new();

    public event Action? OnAny;

    public void Publish<T>(T evt) where T : ChatEvent
    {
        // Typed subscribers
        if (_handlers.TryGetValue(typeof(T), out var list))
        {
            Delegate[] snapshot;
            lock (_lock)
            {
                snapshot = list.ToArray();
            }
            foreach (var handler in snapshot)
                ((Action<T>)handler)(evt);
        }

        // Legacy bridge
        OnAny?.Invoke();
    }

    public IDisposable Subscribe<T>(Action<T> handler) where T : ChatEvent
    {
        var list = _handlers.GetOrAdd(typeof(T), _ => new List<Delegate>());
        lock (_lock)
        {
            list.Add(handler);
        }
        return new Subscription(() =>
        {
            lock (_lock)
            {
                list.Remove(handler);
            }
        });
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
