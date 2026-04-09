using System.Collections.Concurrent;

namespace Seoro.Shared.Services.Chat;

public class ChatEventBus : IChatEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly Lock _lock = new();

    public event Action? OnAny;

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

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose()
        {
            onDispose();
        }
    }
}