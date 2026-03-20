using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

/// <summary>
/// Centralized registry for in-memory session instances that are currently active
/// (e.g., being streamed to). Prevents duplicate loading of the same session
/// from disk by providing a single authoritative in-memory reference.
/// </summary>
public interface IActiveSessionRegistry
{
    void Register(Session session);
    void Unregister(string sessionId);
    Session? Get(string sessionId);
}
