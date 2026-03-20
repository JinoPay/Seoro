using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

/// <summary>
/// Unified stderr/error text classifier for external processes (git, gh, claude).
/// Replaces ad-hoc Contains() checks scattered across GitService, GhService, ClaudeService.
/// </summary>
public static class ProcessErrorClassifier
{
    // ─── Pattern definitions ────────────────────────────────────────

    private static readonly ErrorPattern[] GitPatterns =
    [
        new(["rejected"], ErrorCode.BranchPushRejected, ErrorCategory.Transient),
        new(["not a git repository"], ErrorCode.NotAGitRepo, ErrorCategory.Permanent),
        new(["merge conflict", "not mergeable", "conflicting files"], ErrorCode.PrMergeConflict, ErrorCategory.Transient),
        new(["fatal: unable to create", "worktree"], ErrorCode.WorktreeCreationFailed, ErrorCategory.Permanent),
        new(["required status check"], ErrorCode.CiChecksFailed, ErrorCategory.Transient),
    ];

    private static readonly ErrorPattern[] GhPatterns =
    [
        new(["rate limit", "secondary rate", "API rate limit exceeded", "abuse detection"], ErrorCode.ProcessFailed, ErrorCategory.Transient, true),
        new(["already exists"], ErrorCode.PrCreationFailed, ErrorCategory.Permanent),
        new(["not found", "Could not resolve"], ErrorCode.PrNotFound, ErrorCategory.Permanent),
        new(["merge conflict", "not mergeable", "conflicting files"], ErrorCode.PrMergeConflict, ErrorCategory.Transient),
        new(["required status check"], ErrorCode.CiChecksFailed, ErrorCategory.Transient),
    ];

    private static readonly ErrorPattern[] ClaudePatterns =
    [
        new(["requires --verbose"], ErrorCode.ClaudeProcessFailed, ErrorCategory.Transient),
        new(["API error", "overloaded"], ErrorCode.StreamingFailed, ErrorCategory.Transient),
        new(["authentication", "unauthorized", "invalid api key"], ErrorCode.ClaudeProcessFailed, ErrorCategory.Permanent),
    ];

    // ─── Public API ─────────────────────────────────────────────────

    /// <summary>
    /// Classify a git process error.
    /// </summary>
    public static AppError ClassifyGitError(string stderr, string? stdout = null)
    {
        var combined = CombineText(stderr, stdout);
        return MatchPatterns(combined, stderr, GitPatterns)
               ?? new AppError(ErrorCode.BranchPushFailed, ErrorCategory.Unknown, stderr);
    }

    /// <summary>
    /// Classify a GitHub CLI error.
    /// </summary>
    public static AppError ClassifyGhError(string stderr, string? stdout = null)
    {
        var combined = CombineText(stderr, stdout);
        return MatchPatterns(combined, stderr, GhPatterns)
               ?? new AppError(ErrorCode.ProcessFailed, ErrorCategory.Unknown, stderr);
    }

    /// <summary>
    /// Classify a Claude CLI error.
    /// </summary>
    public static AppError ClassifyClaudeError(string stderr, string? stdout = null)
    {
        var combined = CombineText(stderr, stdout);
        return MatchPatterns(combined, stderr, ClaudePatterns)
               ?? new AppError(ErrorCode.ClaudeProcessFailed, ErrorCategory.Unknown, stderr);
    }

    /// <summary>
    /// Check if stderr indicates a GitHub API rate limit error.
    /// </summary>
    public static bool IsGhRateLimitError(string stderr)
    {
        if (string.IsNullOrEmpty(stderr)) return false;
        var pattern = GhPatterns.FirstOrDefault(p => p.IsRateLimit);
        return pattern != null && pattern.Keywords.Any(
            k => stderr.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Classify a push error specifically (backward compatible with AppError.ClassifyPushError).
    /// </summary>
    public static AppError ClassifyPushError(string errorText)
    {
        if (errorText.Contains("rejected", StringComparison.OrdinalIgnoreCase))
            return AppError.PushRejected(errorText);
        return AppError.PushFailed(errorText);
    }

    /// <summary>
    /// Classify a merge error specifically (backward compatible with AppError.ClassifyMergeError).
    /// </summary>
    public static AppError ClassifyMergeError(string errorText, string? output = null)
    {
        var combined = (errorText + " " + output).ToLowerInvariant();
        if (combined.Contains("merge conflict") || combined.Contains("not mergeable")
            || combined.Contains("conflicting files") || combined.Contains("required status check"))
            return AppError.PrConflict(errorText);
        return AppError.PrMerge(errorText);
    }

    // ─── Internals ──────────────────────────────────────────────────

    private static string CombineText(string stderr, string? stdout)
        => string.IsNullOrEmpty(stdout) ? stderr : $"{stderr} {stdout}";

    private static AppError? MatchPatterns(string combined, string originalError, ErrorPattern[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.Keywords.Any(k => combined.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return new AppError(pattern.Code, pattern.Category, originalError);
        }
        return null;
    }

    private sealed record ErrorPattern(
        string[] Keywords,
        ErrorCode Code,
        ErrorCategory Category,
        bool IsRateLimit = false);
}
