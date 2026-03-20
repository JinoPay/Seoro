namespace Cominomi.Shared.Models;

/// <summary>
/// Structured workspace preferences replacing free-form text fields.
/// Each preference category has typed fields with optional custom prompts.
/// </summary>
public class WorkspacePreferences
{
    // --- Code Review ---

    /// <summary>Custom prompt to pass to agent during code review.</summary>
    public string? CodeReviewPrompt { get; set; }

    /// <summary>Maximum number of files to include in a single review.</summary>
    public int? CodeReviewMaxFileCount { get; set; }

    /// <summary>Focus areas for review (e.g., "security", "performance", "readability").</summary>
    public List<string> CodeReviewFocusAreas { get; set; } = [];

    // --- PR Creation ---

    /// <summary>Custom prompt to pass to agent during PR creation.</summary>
    public string? CreatePrPrompt { get; set; }

    /// <summary>Maximum length for PR titles.</summary>
    public int? PrTitleMaxLength { get; set; }

    /// <summary>Whether to auto-generate PR body from commits.</summary>
    public bool AutoGeneratePrBody { get; set; } = true;

    /// <summary>Labels to apply automatically to new PRs.</summary>
    public List<string> DefaultPrLabels { get; set; } = [];

    // --- Branch Rename ---

    /// <summary>Custom prompt to pass to agent during branch rename.</summary>
    public string? BranchRenamePrompt { get; set; }

    /// <summary>Branch naming convention pattern (e.g., "feature/{slug}", "cominomi/{slug}").</summary>
    public string? BranchNamingPattern { get; set; }

    // --- General ---

    /// <summary>Custom prompt to pass to agent at the start of every new conversation.</summary>
    public string? GeneralPrompt { get; set; }
}
