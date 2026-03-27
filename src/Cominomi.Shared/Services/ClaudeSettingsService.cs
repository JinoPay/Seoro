using System.Text.Json;
using System.Text.Json.Serialization;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ClaudeSettingsService : IClaudeSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string ClaudeHomeDir = GetClaudeHomeDir();

    private readonly ILogger<ClaudeSettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ClaudeSettingsService(ILogger<ClaudeSettingsService> logger)
    {
        _logger = logger;
    }

    public async Task<ClaudeSettings> ReadAsync(ClaudeSettingsScope scope, string? projectPath = null)
    {
        var filePath = GetFilePath(scope, projectPath);
        if (!File.Exists(filePath))
            return new ClaudeSettings();

        await _lock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new ClaudeSettings();

            return JsonSerializer.Deserialize<ClaudeSettings>(json, JsonOptions) ?? new ClaudeSettings();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Claude settings from {Path}", filePath);
            return new ClaudeSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task WriteAsync(ClaudeSettingsScope scope, ClaudeSettings settings, string? projectPath = null)
    {
        var filePath = GetFilePath(scope, projectPath);

        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await AtomicFileWriter.WriteAsync(filePath, json);
            _logger.LogDebug("Wrote Claude settings to {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write Claude settings to {Path}", filePath);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public string GetFilePath(ClaudeSettingsScope scope, string? projectPath = null)
    {
        return scope switch
        {
            ClaudeSettingsScope.Global => Path.Combine(ClaudeHomeDir, "settings.json"),
            ClaudeSettingsScope.Project => Path.Combine(
                ValidateProjectPath(projectPath), ".claude", "settings.json"),
            ClaudeSettingsScope.Local => Path.Combine(
                ValidateProjectPath(projectPath), ".claude", "settings.local.json"),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
    }

    public bool Exists(ClaudeSettingsScope scope, string? projectPath = null)
    {
        return File.Exists(GetFilePath(scope, projectPath));
    }

    private static string ValidateProjectPath(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("projectPath is required for Project and Local scopes.");
        return projectPath;
    }

    private static string GetClaudeHomeDir()
    {
        // Claude CLI stores config in ~/.claude/ on all platforms
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude");
    }
}
