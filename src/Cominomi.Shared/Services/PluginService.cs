using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class PluginManifest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? EntryPoint { get; set; }
    public List<string> Permissions { get; set; } = [];
}

public class PluginInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? EntryPoint { get; set; }
    public string Path { get; set; } = "";
    public bool IsEnabled { get; set; }
    public PluginStatus Status { get; set; } = PluginStatus.Discovered;
    public string? Error { get; set; }
    public List<string> Permissions { get; set; } = [];
}

public enum PluginStatus
{
    Discovered,
    Valid,
    Invalid,
    Loaded,
    Error
}

public interface IPluginService
{
    string PluginsDirectory { get; }
    Task<List<PluginInfo>> GetInstalledPluginsAsync();
    Task<PluginInfo?> GetPluginAsync(string pluginId);
    Task SetPluginEnabledAsync(string pluginId, bool enabled);
    Task<bool> ValidatePluginAsync(string pluginId);
    Task<bool> RemovePluginAsync(string pluginId);
    Task EnsurePluginsDirectoryAsync();
}

public class PluginService : IPluginService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<PluginService> _logger;

    public string PluginsDirectory { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "plugins");

    public PluginService(ISettingsService settingsService, ILogger<PluginService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task EnsurePluginsDirectoryAsync()
    {
        if (!Directory.Exists(PluginsDirectory))
        {
            try
            {
                Directory.CreateDirectory(PluginsDirectory);
                _logger.LogInformation("Created plugins directory at {Path}", PluginsDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create plugins directory at {Path}", PluginsDirectory);
            }
        }
    }

    public async Task<List<PluginInfo>> GetInstalledPluginsAsync()
    {
        var plugins = new List<PluginInfo>();

        if (!Directory.Exists(PluginsDirectory))
        {
            _logger.LogDebug("Plugins directory does not exist: {Path}", PluginsDirectory);
            return plugins;
        }

        var settings = await _settingsService.LoadAsync();

        try
        {
            foreach (var dir in Directory.GetDirectories(PluginsDirectory))
            {
                var pluginId = System.IO.Path.GetFileName(dir);
                var plugin = await LoadPluginFromDirectoryAsync(dir, pluginId, settings);
                plugins.Add(plugin);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate plugins directory");
        }

        return plugins;
    }

    public async Task<PluginInfo?> GetPluginAsync(string pluginId)
    {
        var pluginDir = System.IO.Path.Combine(PluginsDirectory, pluginId);
        if (!Directory.Exists(pluginDir))
            return null;

        var settings = await _settingsService.LoadAsync();
        return await LoadPluginFromDirectoryAsync(pluginDir, pluginId, settings);
    }

    public async Task SetPluginEnabledAsync(string pluginId, bool enabled)
    {
        var settings = await _settingsService.LoadAsync();
        settings.DisabledPlugins ??= [];

        if (enabled)
            settings.DisabledPlugins.Remove(pluginId);
        else if (!settings.DisabledPlugins.Contains(pluginId))
            settings.DisabledPlugins.Add(pluginId);

        await _settingsService.SaveAsync(settings);
        _logger.LogInformation("Plugin '{PluginId}' {State}", pluginId, enabled ? "enabled" : "disabled");
    }

    public async Task<bool> ValidatePluginAsync(string pluginId)
    {
        var plugin = await GetPluginAsync(pluginId);
        if (plugin == null) return false;

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(plugin.Name))
            errors.Add("manifest.json: 'name' 필드 누락");

        if (string.IsNullOrWhiteSpace(plugin.EntryPoint))
            errors.Add("manifest.json: 'entryPoint' 필드 누락");
        else
        {
            var entryPath = System.IO.Path.Combine(plugin.Path, plugin.EntryPoint);
            if (!File.Exists(entryPath))
                errors.Add($"진입점 파일을 찾을 수 없음: {plugin.EntryPoint}");
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning("Plugin '{PluginId}' validation failed: {Errors}", pluginId, string.Join("; ", errors));
            return false;
        }

        return true;
    }

    public async Task<bool> RemovePluginAsync(string pluginId)
    {
        var pluginDir = System.IO.Path.Combine(PluginsDirectory, pluginId);
        if (!Directory.Exists(pluginDir))
            return false;

        try
        {
            Directory.Delete(pluginDir, recursive: true);
            _logger.LogInformation("Removed plugin '{PluginId}'", pluginId);

            // Clean up settings
            var settings = await _settingsService.LoadAsync();
            settings.DisabledPlugins?.Remove(pluginId);
            await _settingsService.SaveAsync(settings);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove plugin '{PluginId}'", pluginId);
            return false;
        }
    }

    private async Task<PluginInfo> LoadPluginFromDirectoryAsync(string dir, string pluginId, Models.AppSettings settings)
    {
        var plugin = new PluginInfo
        {
            Id = pluginId,
            Name = pluginId,
            Path = dir,
            IsEnabled = !(settings.DisabledPlugins?.Contains(pluginId) ?? false),
            Status = PluginStatus.Discovered
        };

        var manifestPath = System.IO.Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            plugin.Status = PluginStatus.Invalid;
            plugin.Error = "manifest.json 파일을 찾을 수 없습니다.";
            return plugin;
        }

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("name", out var name))
                plugin.Name = name.GetString() ?? plugin.Name;
            if (root.TryGetProperty("description", out var desc))
                plugin.Description = desc.GetString();
            if (root.TryGetProperty("version", out var version))
                plugin.Version = version.GetString();
            if (root.TryGetProperty("author", out var author))
                plugin.Author = author.GetString();
            if (root.TryGetProperty("entryPoint", out var entryPoint))
                plugin.EntryPoint = entryPoint.GetString();
            if (root.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Array)
            {
                foreach (var perm in perms.EnumerateArray())
                {
                    var permStr = perm.GetString();
                    if (permStr != null) plugin.Permissions.Add(permStr);
                }
            }

            // Validate entry point existence
            if (!string.IsNullOrEmpty(plugin.EntryPoint))
            {
                var entryPath = System.IO.Path.Combine(dir, plugin.EntryPoint);
                if (File.Exists(entryPath))
                    plugin.Status = PluginStatus.Valid;
                else
                {
                    plugin.Status = PluginStatus.Invalid;
                    plugin.Error = $"진입점 파일을 찾을 수 없습니다: {plugin.EntryPoint}";
                }
            }
            else
            {
                plugin.Status = PluginStatus.Valid;
            }
        }
        catch (Exception ex)
        {
            plugin.Status = PluginStatus.Error;
            plugin.Error = $"매니페스트 파싱 실패: {ex.Message}";
            _logger.LogDebug(ex, "Failed to read plugin manifest at {Path}", manifestPath);
        }

        return plugin;
    }
}
