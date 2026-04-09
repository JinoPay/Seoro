
namespace Seoro.Shared.Services.Claude;

/// <summary>
///     Reads and writes Claude CLI's native settings.json files
///     at three scopes: Global, Project, and Local.
/// </summary>
public interface IClaudeSettingsService
{
    /// <summary>
    ///     Checks whether the settings file exists for the given scope.
    /// </summary>
    bool Exists(ClaudeSettingsScope scope, string? projectPath = null);

    /// <summary>
    ///     Resolves the file path for the given scope.
    /// </summary>
    string GetFilePath(ClaudeSettingsScope scope, string? projectPath = null);

    /// <summary>
    ///     Writes Claude CLI settings for the given scope.
    ///     Creates directories and the file if they don't exist.
    /// </summary>
    Task WriteAsync(ClaudeSettingsScope scope, ClaudeSettings settings, string? projectPath = null);

    /// <summary>
    ///     Reads Claude CLI settings for the given scope.
    ///     Returns an empty <see cref="ClaudeSettings" /> if the file doesn't exist.
    /// </summary>
    /// <param name="scope">Global, Project, or Local scope.</param>
    /// <param name="projectPath">
    ///     Absolute path to the project root. Required for Project and Local scopes.
    /// </param>
    Task<ClaudeSettings> ReadAsync(ClaudeSettingsScope scope, string? projectPath = null);
}