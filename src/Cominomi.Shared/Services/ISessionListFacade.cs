using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISessionListFacade
{
    /// <summary>
    ///     Archives a session and transitions its status.
    /// </summary>
    Task CleanupSessionAsync(Session session);

    /// <summary>
    ///     Loads a full session, sets ChatState (workspace + session), and persists selection.
    /// </summary>
    Task SelectSessionAsync(Session session, Workspace? ws, List<Workspace> workspaces);

    /// <summary>
    ///     Restores the last workspace/session from persisted settings.
    ///     Returns the workspace, matching session, and project name for UI expansion.
    /// </summary>
    Task<(Workspace? Workspace, Session? Session, string? ProjectName)> RestoreLastSelectionAsync(
        List<Workspace> workspaces,
        Dictionary<string, List<Session>> sessionCache);

    /// <summary>
    ///     Shows confirmation dialog, deletes session, cleans up caches, notifies user.
    ///     Returns true if the session was actually deleted.
    /// </summary>
    Task<bool> DeleteSessionAsync(Session session);

    /// <summary>
    ///     Shows confirmation dialog, deletes all sessions and the workspace, cleans up caches, notifies user.
    ///     Returns true if the workspace was actually deleted.
    /// </summary>
    Task<bool> DeleteWorkspaceAsync(Workspace workspace);

    /// <summary>
    ///     Creates a new session (standard or local-dir), sets ChatState, updates cache, and persists selection.
    /// </summary>
    Task<Session> CreateSessionAsync(Workspace ws, bool localDir = false);
}