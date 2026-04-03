namespace Cominomi.Shared.Services;

public record CredentialBackup(string? ConfigJson, string? CredentialsJson);

public interface IClaudeCredentialService
{
    string ClaudeHomeDir { get; }

    /// <summary>Reads ~/.claude/.claude.json (both platforms).</summary>
    Task<string?> ReadCurrentConfigAsync();

    /// <summary>Reads live credentials — file on Windows, Keychain on macOS.</summary>
    Task<string?> ReadCurrentCredentialsAsync();

    /// <summary>
    ///     Writes ~/.claude/.claude.json, merging only the oauthAccount section
    ///     from <paramref name="configJson"/> into the existing file.
    /// </summary>
    Task WriteConfigAsync(string configJson);

    /// <summary>Writes live credentials — file on Windows, Keychain on macOS.</summary>
    Task WriteCredentialsAsync(string credentialsJson);

    /// <summary>Backs up config + credentials to account-backups/{accountId}/.</summary>
    Task BackupAsync(string accountId, string? configJson, string? credentialsJson);

    /// <summary>Loads the backed-up config + credentials for an account. Returns null if not found or corrupt.</summary>
    Task<CredentialBackup?> LoadBackupAsync(string accountId);

    /// <summary>Deletes the backup directory for an account.</summary>
    Task DeleteBackupAsync(string accountId);

    /// <summary>
    ///     Extracts the emailAddress from a .claude.json string.
    ///     Falls back to parsing the JWT access_token payload if config is absent.
    /// </summary>
    string? ExtractEmail(string? configJson, string? credentialsJson);
}
