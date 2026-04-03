using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ClaudeCredentialService(IProcessRunner processRunner, ILogger<ClaudeCredentialService> logger)
    : IClaudeCredentialService
{
    private static readonly string HomeDir =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string ClaudeHomeDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    // ~/.claude/.claude.json (primary), fallback: ~/.claude.json (matches cc-account-switcher behavior)
    private string ConfigFilePath => ResolveConfigFilePath();
    private string CredentialsFilePath => Path.Combine(ClaudeHomeDir, ".credentials.json");

    private string ResolveConfigFilePath()
    {
        var primary = Path.Combine(ClaudeHomeDir, ".claude.json");
        if (File.Exists(primary))
        {
            try
            {
                var json = File.ReadAllText(primary);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("oauthAccount", out _))
                    return primary;
            }
            catch { /* fall through to fallback */ }
        }

        var fallback = Path.Combine(HomeDir, ".claude.json");
        if (File.Exists(fallback))
            return fallback;

        return primary; // default even if not yet created
    }

    // ── Config (shared across platforms) ──────────────────────────────────

    public async Task<string?> ReadCurrentConfigAsync()
    {
        var path = ResolveConfigFilePath();
        if (!File.Exists(path)) return null;
        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read Claude config from {Path}", path);
            return null;
        }
    }

    public async Task WriteConfigAsync(string configJson)
    {
        var targetPath = ResolveConfigFilePath();
        try
        {
            // Merge only the oauthAccount key — preserves all other settings
            string merged;
            if (File.Exists(targetPath))
            {
                var existing = JsonNode.Parse(await File.ReadAllTextAsync(targetPath))?.AsObject()
                               ?? new JsonObject();
                var incoming = JsonNode.Parse(configJson)?.AsObject();
                if (incoming?.TryGetPropertyValue("oauthAccount", out var oauthNode) == true && oauthNode != null)
                    existing["oauthAccount"] = oauthNode.DeepClone();
                merged = existing.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                merged = configJson;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await AtomicFileWriter.WriteAsync(targetPath, merged);
            logger.LogDebug("Wrote Claude config to {Path}", targetPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write Claude config to {Path}", targetPath);
            throw;
        }
    }

    // ── Credentials (platform-specific) ───────────────────────────────────

    public async Task<string?> ReadCurrentCredentialsAsync()
    {
        if (OperatingSystem.IsMacOS())
            return await ReadKeychainAsync();
        return await ReadCredentialsFileAsync();
    }

    public async Task WriteCredentialsAsync(string credentialsJson)
    {
        if (OperatingSystem.IsMacOS())
            await WriteKeychainAsync(credentialsJson);
        else
            await WriteCredentialsFileAsync(credentialsJson);
    }

    // ── macOS Keychain ─────────────────────────────────────────────────────

    private async Task<string?> ReadKeychainAsync()
    {
        var user = Environment.UserName;
        var result = await processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "security",
            Arguments = ["find-generic-password", "-s", "Claude Code-credentials", "-a", user, "-w"]
        });

        if (!result.Success || string.IsNullOrWhiteSpace(result.Stdout))
        {
            if (result.ExitCode != 44) // 44 = item not found — expected when not logged in
                logger.LogWarning("Keychain read failed (exit {Code}): {Err}", result.ExitCode, result.Stderr);
            return null;
        }

        return result.Stdout.Trim();
    }

    private async Task WriteKeychainAsync(string credentialsJson)
    {
        var user = Environment.UserName;
        var result = await processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "security",
            Arguments = ["add-generic-password", "-U", "-s", "Claude Code-credentials", "-a", user, "-w", credentialsJson]
        });

        if (!result.Success)
        {
            var msg = $"Keychain write failed (exit {result.ExitCode}): {result.Stderr}";
            logger.LogError("Keychain write failed (exit {Code}): {Err}", result.ExitCode, result.Stderr);
            throw new InvalidOperationException(msg);
        }

        logger.LogDebug("Wrote credentials to macOS Keychain");
    }

    // ── Windows credentials file ───────────────────────────────────────────

    private async Task<string?> ReadCredentialsFileAsync()
    {
        if (!File.Exists(CredentialsFilePath)) return null;
        try
        {
            return await File.ReadAllTextAsync(CredentialsFilePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read credentials from {Path}", CredentialsFilePath);
            return null;
        }
    }

    private async Task WriteCredentialsFileAsync(string credentialsJson)
    {
        try
        {
            Directory.CreateDirectory(ClaudeHomeDir);
            await AtomicFileWriter.WriteAsync(CredentialsFilePath, credentialsJson);
            logger.LogDebug("Wrote credentials to {Path}", CredentialsFilePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write credentials to {Path}", CredentialsFilePath);
            throw;
        }
    }

    // ── Backup ─────────────────────────────────────────────────────────────

    public async Task BackupAsync(string accountId, string? configJson, string? credentialsJson)
    {
        var dir = Path.Combine(AppPaths.AccountBackups, accountId);
        Directory.CreateDirectory(dir);

        if (configJson != null)
            await AtomicFileWriter.WriteAsync(Path.Combine(dir, "config.json"), configJson);

        if (credentialsJson != null)
            await AtomicFileWriter.WriteAsync(Path.Combine(dir, "credentials.json"), credentialsJson);

        logger.LogDebug("Backed up credentials for account {Id}", accountId);
    }

    public async Task<CredentialBackup?> LoadBackupAsync(string accountId)
    {
        var dir = Path.Combine(AppPaths.AccountBackups, accountId);
        if (!Directory.Exists(dir)) return null;

        try
        {
            var configPath = Path.Combine(dir, "config.json");
            var credPath = Path.Combine(dir, "credentials.json");

            var config = File.Exists(configPath) ? await File.ReadAllTextAsync(configPath) : null;
            var creds = File.Exists(credPath) ? await File.ReadAllTextAsync(credPath) : null;

            // Validate that credentials JSON at least parses
            if (creds != null)
                JsonDocument.Parse(creds).Dispose();

            return new CredentialBackup(config, creds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Backup for account {Id} is corrupt or unreadable", accountId);
            return null;
        }
    }

    public Task DeleteBackupAsync(string accountId)
    {
        var dir = Path.Combine(AppPaths.AccountBackups, accountId);
        if (Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                logger.LogDebug("Deleted backup for account {Id}", accountId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete backup for account {Id}", accountId);
            }
        }

        return Task.CompletedTask;
    }

    // ── Email extraction ───────────────────────────────────────────────────

    public string? ExtractEmail(string? configJson, string? credentialsJson)
    {
        // Try .claude.json first
        if (configJson != null)
        {
            try
            {
                var doc = JsonDocument.Parse(configJson);
                if (doc.RootElement.TryGetProperty("oauthAccount", out var oauth) &&
                    oauth.TryGetProperty("emailAddress", out var email))
                    return email.GetString();
            }
            catch { /* fall through */ }
        }

        // Fall back to JWT payload in access_token
        // Windows stores tokens nested: { "claudeAiOauth": { "accessToken": "..." } }
        if (credentialsJson != null)
        {
            try
            {
                var doc = JsonDocument.Parse(credentialsJson);
                var token = FindTokenInElement(doc.RootElement, "accessToken", "access_token");
                if (token != null)
                    return ExtractEmailFromJwt(token);
            }
            catch { /* fall through */ }
        }

        return null;
    }

    /// <summary>
    ///     Finds a string value for any of the given keys at the root level or one level deep.
    ///     Handles { "claudeAiOauth": { "accessToken": "..." } } format on Windows.
    /// </summary>
    private static string? FindTokenInElement(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
            if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();

        foreach (var child in root.EnumerateObject())
        {
            if (child.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var key in keys)
                if (child.Value.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
        }

        return null;
    }

    private static string? ExtractEmailFromJwt(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var payload = parts[1];
            // Base64url padding
            var padded = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/')));
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("email", out var email))
                return email.GetString();
        }
        catch { /* ignore */ }

        return null;
    }
}
