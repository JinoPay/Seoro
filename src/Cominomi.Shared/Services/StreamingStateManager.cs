using System.Collections.Concurrent;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class StreamingStateManager
{
    private readonly ConcurrentDictionary<string, SessionStreamingState> _streamingStates = new();
    private readonly ConcurrentDictionary<string, Session> _activeSessions = new();
    private readonly Action _notifyChanged;

    public StreamingStateManager(Action notifyChanged)
    {
        _notifyChanged = notifyChanged;
    }

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
        => _activeSessions[session.Id] = session;

    public void UnregisterActiveSession(string sessionId)
        => _activeSessions.TryRemove(sessionId, out _);

    public Session? GetActiveSession(string sessionId)
        => _activeSessions.TryGetValue(sessionId, out var session) ? session : null;

    public void SetStreaming(bool streaming, string? sessionId)
    {
        if (sessionId == null) return;

        var state = _streamingStates.GetOrAdd(sessionId, _ => new SessionStreamingState());
        state.IsStreaming = streaming;
        if (!streaming)
        {
            state.Phase = StreamingPhase.None;
            state.ActiveToolName = null;
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
