using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

/// <summary>
/// Result of a streaming message exchange (send or continue).
/// ChatView reads these to update local UI state.
/// </summary>
public class StreamResult
{
    public string? PlanFilePath { get; init; }
    public string? PlanContent { get; init; }
    public bool PlanReviewVisible { get; init; }
    public bool QuickResponseVisible { get; init; }
    public List<string> QuickResponseOptions { get; init; } = [];

    /// <summary>Non-null when the streaming loop caught an exception that ChatView should surface via Snackbar.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>True when the stream was cancelled via CancellationToken.</summary>
    public bool WasCancelled { get; init; }
}

/// <summary>
/// Orchestrates message send / continue flows that were previously inlined in ChatView.
/// Owns: worktree init, attachment handling, streaming loop, finalize, hooks, PR status check.
/// Does NOT own: UI rendering, Snackbar, JS interop, plan review UI actions.
/// </summary>
public interface IChatMessageOrchestrator
{
    /// <summary>
    /// Full send flow: worktree init → attachments → user message → first-message rename → stream → finalize → hooks → PR check.
    /// </summary>
    Task<StreamResult> SendAsync(
        Session session,
        ChatInputMessage input,
        string selectedBranch,
        Workspace? workspace,
        CancellationToken ct = default);

    /// <summary>
    /// Continue flow: system message → stream → finalize.
    /// </summary>
    Task<StreamResult> ContinueAsync(
        Session session,
        Workspace? workspace,
        CancellationToken ct = default);

    /// <summary>
    /// Check if a PR exists for the session's branch and update session state.
    /// </summary>
    Task CheckAndUpdatePrStatusAsync(Session session);
}
