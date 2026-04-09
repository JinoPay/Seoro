
namespace Seoro.Shared.Services.Infrastructure;

/// <summary>
///     Unified stderr/error text classifier for external processes (git, gh, claude).
///     Replaces ad-hoc Contains() checks scattered across GitService, GhService, ClaudeService.
/// </summary>
public static class ProcessErrorClassifier
{
    private static readonly ErrorPattern[] ClaudePatterns =
    [
        new(["requires --verbose"], ErrorCode.ClaudeProcessFailed, ErrorCategory.Transient),
        new(["API error", "overloaded"], ErrorCode.StreamingFailed, ErrorCategory.Transient),
        new(["authentication", "unauthorized", "invalid api key"], ErrorCode.ClaudeProcessFailed,
            ErrorCategory.Permanent)
    ];
    // ─── Pattern definitions ────────────────────────────────────────

    private static readonly ErrorPattern[] GitPatterns =
    [
        new(["rejected"], ErrorCode.BranchPushRejected, ErrorCategory.Transient),
        new(["not a git repository"], ErrorCode.NotAGitRepo, ErrorCategory.Permanent),
        new(["fatal: unable to create", "worktree"], ErrorCode.WorktreeCreationFailed, ErrorCategory.Permanent)
    ];

    /// <summary>
    ///     Classify a Claude CLI error.
    /// </summary>
    public static AppError ClassifyClaudeError(string stderr, string? stdout = null)
    {
        var combined = CombineText(stderr, stdout);
        return MatchPatterns(combined, stderr, ClaudePatterns)
               ?? new AppError(ErrorCode.ClaudeProcessFailed, ErrorCategory.Unknown, stderr);
    }

    // ─── Public API ─────────────────────────────────────────────────

    /// <summary>
    ///     Classify a git process error.
    /// </summary>
    public static AppError ClassifyGitError(string stderr, string? stdout = null)
    {
        var combined = CombineText(stderr, stdout);
        return MatchPatterns(combined, stderr, GitPatterns)
               ?? new AppError(ErrorCode.BranchPushFailed, ErrorCategory.Unknown, stderr);
    }

    /// <summary>
    ///     Classify a push error specifically (backward compatible with AppError.ClassifyPushError).
    /// </summary>
    public static AppError ClassifyPushError(string errorText)
    {
        if (errorText.Contains("rejected", StringComparison.OrdinalIgnoreCase))
            return AppError.PushRejected(errorText);
        return AppError.PushFailed(errorText);
    }

    private static AppError? MatchPatterns(string combined, string originalError, ErrorPattern[] patterns)
    {
        foreach (var pattern in patterns)
            if (pattern.Keywords.Any(k => combined.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return new AppError(pattern.Code, pattern.Category, originalError);
        return null;
    }

    // ─── Internals ──────────────────────────────────────────────────

    private static string CombineText(string stderr, string? stdout)
    {
        return string.IsNullOrEmpty(stdout) ? stderr : $"{stderr} {stdout}";
    }

    private sealed record ErrorPattern(
        string[] Keywords,
        ErrorCode Code,
        ErrorCategory Category,
        bool IsRateLimit = false);
}