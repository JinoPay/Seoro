using System.Collections.Concurrent;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class ActiveSessionRegistry : IActiveSessionRegistry
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public void Register(Session session)
        => _sessions[session.Id] = session;

    public void Unregister(string sessionId)
        => _sessions.TryRemove(sessionId, out _);

    public Session? Get(string sessionId)
        => _sessions.TryGetValue(sessionId, out var session) ? session : null;
}
