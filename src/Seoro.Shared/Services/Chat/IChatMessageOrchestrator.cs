
namespace Seoro.Shared.Services.Chat;

/// <summary>
///     Result of a streaming message exchange (send or continue).
///     ChatView reads these to update local UI state.
/// </summary>
public class StreamResult
{
    public bool PlanReviewVisible { get; init; }
    public bool QuickResponseVisible { get; init; }

    /// <summary>True when the stream was cancelled via CancellationToken.</summary>
    public bool WasCancelled { get; init; }

    public List<string> QuickResponseOptions { get; init; } = [];

    /// <summary>Raw JSON input from the AskUserQuestion tool call for the bottom bar.</summary>
    public string? AskUserQuestionInput { get; init; }

    /// <summary>Non-null when the streaming loop caught an exception that ChatView should surface via Snackbar.</summary>
    public string? ErrorMessage { get; init; }

    public string? PlanContent { get; init; }
    public string? PlanFilePath { get; init; }
}

/// <summary>
///     Orchestrates message send / continue flows that were previously inlined in ChatView.
///     Owns: worktree init, attachment handling, streaming loop, finalize, hooks.
///     Does NOT own: UI rendering, Snackbar, JS interop, plan review UI actions.
/// </summary>
public interface IChatMessageOrchestrator
{
    /// <summary>
    ///     Continue flow: system message → stream → finalize.
    /// </summary>
    Task<StreamResult> ContinueAsync(
        Session session,
        Workspace? workspace,
        CancellationToken ct = default);

    /// <summary>
    ///     Full send flow: worktree init → attachments → user message → first-message rename → stream → finalize → hooks.
    /// </summary>
    Task<StreamResult> SendAsync(
        Session session,
        ChatInputMessage input,
        string selectedBranch,
        Workspace? workspace,
        CancellationToken ct = default);
}