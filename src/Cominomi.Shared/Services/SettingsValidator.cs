using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

/// <summary>
/// Validates and sanitizes AppSettings / Workspace values on load and save.
/// Returns a list of validation issues; empty list means valid.
/// Also provides Sanitize methods that clamp/fix invalid values to safe defaults.
/// </summary>
public static class SettingsValidator
{
    private static readonly HashSet<string> ValidThemes = ["dark", "light", "system"];
    private static readonly HashSet<string> ValidPermissionModes =
        ["default", "plan", "acceptEdits", "dontAsk", "bypassPermissions", "bypassAll"];
    private static readonly HashSet<string> ValidEffortLevels = ["auto", "low", "medium", "high", "max"];
    private static readonly HashSet<string> ValidMergeStrategies = ["squash", "merge", "rebase"];

    public static List<string> Validate(AppSettings settings)
    {
        var issues = new List<string>();

        // Theme
        if (!ValidThemes.Contains(settings.Theme))
            issues.Add($"Invalid theme '{settings.Theme}'. Must be one of: {string.Join(", ", ValidThemes)}");

        // Permission mode
        if (!ValidPermissionModes.Contains(settings.DefaultPermissionMode))
            issues.Add($"Invalid permission mode '{settings.DefaultPermissionMode}'. Must be one of: {string.Join(", ", ValidPermissionModes)}");

        // Effort level
        if (!ValidEffortLevels.Contains(settings.DefaultEffortLevel))
            issues.Add($"Invalid effort level '{settings.DefaultEffortLevel}'. Must be one of: {string.Join(", ", ValidEffortLevels)}");

        // Merge strategy
        if (!ValidMergeStrategies.Contains(settings.DefaultMergeStrategy))
            issues.Add($"Invalid merge strategy '{settings.DefaultMergeStrategy}'. Must be one of: {string.Join(", ", ValidMergeStrategies)}");

        // Timeouts — must be positive
        if (settings.DefaultProcessTimeoutSeconds <= 0)
            issues.Add($"DefaultProcessTimeoutSeconds must be positive, got {settings.DefaultProcessTimeoutSeconds}");
        if (settings.HookTimeoutSeconds <= 0)
            issues.Add($"HookTimeoutSeconds must be positive, got {settings.HookTimeoutSeconds}");
        if (settings.SummarizationTimeoutSeconds <= 0)
            issues.Add($"SummarizationTimeoutSeconds must be positive, got {settings.SummarizationTimeoutSeconds}");
        if (settings.VersionCheckTimeoutSeconds <= 0)
            issues.Add($"VersionCheckTimeoutSeconds must be positive, got {settings.VersionCheckTimeoutSeconds}");
        if (settings.CiCheckTimeoutSeconds <= 0)
            issues.Add($"CiCheckTimeoutSeconds must be positive, got {settings.CiCheckTimeoutSeconds}");

        // Optional numeric constraints
        if (settings.DefaultMaxTurns is < 1)
            issues.Add($"DefaultMaxTurns must be >= 1, got {settings.DefaultMaxTurns}");
        if (settings.DefaultMaxBudgetUsd is < 0)
            issues.Add($"DefaultMaxBudgetUsd must be non-negative, got {settings.DefaultMaxBudgetUsd}");

        // Path validation (only if set)
        ValidatePath(settings.ClaudePath, "ClaudePath", issues);
        ValidatePath(settings.GitPath, "GitPath", issues);
        ValidatePath(settings.McpConfigPath, "McpConfigPath", issues);
        ValidateDirectoryPath(settings.DefaultCloneDirectory, "DefaultCloneDirectory", issues);

        return issues;
    }

    /// <summary>
    /// Sanitizes settings by clamping invalid values to safe defaults.
    /// Returns the sanitized settings (same instance, mutated in place).
    /// </summary>
    public static AppSettings Sanitize(AppSettings settings)
    {
        if (!ValidThemes.Contains(settings.Theme))
            settings.Theme = "dark";

        if (!ValidPermissionModes.Contains(settings.DefaultPermissionMode))
            settings.DefaultPermissionMode = CominomiConstants.DefaultPermissionMode;

        if (!ValidEffortLevels.Contains(settings.DefaultEffortLevel))
            settings.DefaultEffortLevel = CominomiConstants.DefaultEffortLevel;

        if (!ValidMergeStrategies.Contains(settings.DefaultMergeStrategy))
            settings.DefaultMergeStrategy = CominomiConstants.DefaultMergeStrategy;

        // Clamp timeouts to minimum 1 second
        settings.DefaultProcessTimeoutSeconds = Math.Max(1, settings.DefaultProcessTimeoutSeconds);
        settings.HookTimeoutSeconds = Math.Max(1, settings.HookTimeoutSeconds);
        settings.SummarizationTimeoutSeconds = Math.Max(1, settings.SummarizationTimeoutSeconds);
        settings.VersionCheckTimeoutSeconds = Math.Max(1, settings.VersionCheckTimeoutSeconds);
        settings.CiCheckTimeoutSeconds = Math.Max(1, settings.CiCheckTimeoutSeconds);

        // Clamp optional numeric values
        if (settings.DefaultMaxTurns is < 1)
            settings.DefaultMaxTurns = null;
        if (settings.DefaultMaxBudgetUsd is < 0)
            settings.DefaultMaxBudgetUsd = null;

        return settings;
    }

    public static List<string> ValidateWorkspace(Workspace workspace)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(workspace.Name))
            issues.Add("Workspace name cannot be empty");

        ValidateDirectoryPath(workspace.RepoLocalPath, "RepoLocalPath", issues);

        // Validate structured preferences
        if (workspace.Preferences != null)
        {
            var prefs = workspace.Preferences;
            if (prefs.CodeReviewMaxFileCount is < 1)
                issues.Add($"CodeReviewMaxFileCount must be >= 1, got {prefs.CodeReviewMaxFileCount}");
            if (prefs.PrTitleMaxLength is < 10 or > 200)
                issues.Add($"PrTitleMaxLength must be 10-200, got {prefs.PrTitleMaxLength}");
        }

        return issues;
    }

    public static Workspace SanitizeWorkspace(Workspace workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace.DefaultRemote))
            workspace.DefaultRemote = "origin";

        // Ensure preferences object exists
        workspace.Preferences ??= new WorkspacePreferences();

        // Clamp preference values
        var prefs = workspace.Preferences;
        if (prefs.CodeReviewMaxFileCount is < 1)
            prefs.CodeReviewMaxFileCount = null;
        if (prefs.PrTitleMaxLength is < 10 or > 200)
            prefs.PrTitleMaxLength = null;

        return workspace;
    }

    private static void ValidatePath(string? path, string fieldName, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            issues.Add($"{fieldName} contains invalid path characters: '{path}'");
    }

    private static void ValidateDirectoryPath(string? path, string fieldName, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            issues.Add($"{fieldName} contains invalid path characters: '{path}'");
    }
}
