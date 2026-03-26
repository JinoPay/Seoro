using System.Collections.Concurrent;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class StreamingStateManager
{
    private readonly ConcurrentDictionary<string, SessionStreamingState> _streamingStates = new();
    private readonly Action _notifyChanged;
    private IActiveSessionRegistry? _activeSessionRegistry;

    public StreamingStateManager(Action notifyChanged)
    {
        _notifyChanged = notifyChanged;
    }

    /// <summary>
    /// Binds the shared active-session registry. Called once during ChatState construction
    /// after DI resolves the registry. This avoids a constructor dependency cycle.
    /// </summary>
    public void BindRegistry(IActiveSessionRegistry registry)
        => _activeSessionRegistry = registry;

    public bool IsSessionStreaming(string? sessionId)
        => sessionId != null && _streamingStates.TryGetValue(sessionId, out var s) && s.IsStreaming;

    public StreamingPhase GetSessionPhase(string sessionId)
        => _streamingStates.TryGetValue(sessionId, out var s) ? s.Phase : StreamingPhase.None;

    public string? GetSessionToolName(string sessionId)
        => _streamingStates.TryGetValue(sessionId, out var s) ? s.ActiveToolName : null;

    public bool HasAnyStreaming()
        => _streamingStates.Values.Any(s => s.IsStreaming);

    public IReadOnlyList<string> GetStreamingSessionIds()
        => _streamingStates.Where(kv => kv.Value.IsStreaming).Select(kv => kv.Key).ToList();

    public void RegisterActiveSession(Session session)
        => _activeSessionRegistry!.Register(session);

    public void UnregisterActiveSession(string sessionId)
        => _activeSessionRegistry!.Unregister(sessionId);

    public Session? GetActiveSession(string sessionId)
        => _activeSessionRegistry!.Get(sessionId);

    public bool IsSessionCompleted(string? sessionId)
        => sessionId != null && _streamingStates.TryGetValue(sessionId, out var s) && s.HasCompleted;

    public void ClearCompleted(string? sessionId)
    {
        if (sessionId == null) return;
        if (_streamingStates.TryGetValue(sessionId, out var state) && state.HasCompleted)
        {
            state.HasCompleted = false;
            _notifyChanged();
        }
    }

    public void SetStreaming(bool streaming, string? sessionId)
    {
        if (sessionId == null) return;

        var state = _streamingStates.GetOrAdd(sessionId, _ => new SessionStreamingState());
        state.IsStreaming = streaming;
        if (!streaming)
        {
            state.Phase = StreamingPhase.None;
            state.ActiveToolName = null;
            state.HasCompleted = true;
        }
        else
        {
            state.HasCompleted = false;
        }
        _notifyChanged();
    }

    public void SetPhase(StreamingPhase phase, string? toolName = null, string? sessionId = null)
    {
        if (sessionId == null) return;

        var state = _streamingStates.GetOrAdd(sessionId, _ => new SessionStreamingState());
        if (state.Phase == phase && state.ActiveToolName == toolName) return;
        state.Phase = phase;
        state.ActiveToolName = toolName;
        _notifyChanged();
    }
}
