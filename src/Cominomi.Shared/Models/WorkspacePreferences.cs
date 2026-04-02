namespace Cominomi.Shared.Models;

/// <summary>
///     Structured workspace preferences replacing free-form text fields.
///     Each preference category has typed fields with optional custom prompts.
/// </summary>
public class WorkspacePreferences
{
    /// <summary>Maximum number of files to include in a single review.</summary>
    public int? CodeReviewMaxFileCount { get; set; }

    /// <summary>Focus areas for review (e.g., "security", "performance", "readability").</summary>
    public List<string> CodeReviewFocusAreas { get; set; } = [];
    // --- Code Review ---

    /// <summary>Custom prompt to pass to agent during code review.</summary>
    public string? CodeReviewPrompt { get; set; }

    // --- General ---

    /// <summary>Custom prompt to pass to agent at the start of every new conversation.</summary>
    public string? GeneralPrompt { get; set; }
}