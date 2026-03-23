using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public static class SessionStatusMachine
{
    private static readonly Dictionary<SessionStatus, HashSet<SessionStatus>> ValidTransitions = new()
    {
        [SessionStatus.Initializing] = [SessionStatus.Pending, SessionStatus.Ready, SessionStatus.Error],
        [SessionStatus.Pending] = [SessionStatus.Initializing, SessionStatus.Ready, SessionStatus.Error],
        [SessionStatus.Ready] = [SessionStatus.Error, SessionStatus.Archived],
        [SessionStatus.Error] = [SessionStatus.Ready, SessionStatus.Initializing, SessionStatus.Archived],
        [SessionStatus.Archived] = [],
    };

    public static bool IsValidTransition(SessionStatus from, SessionStatus to)
    {
        if (from == to)
            return true; // Idempotent — no-op transitions are always safe

        return ValidTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }
}
