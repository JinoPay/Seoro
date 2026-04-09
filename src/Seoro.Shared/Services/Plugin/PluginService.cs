using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seoro.Shared.Services.Plugin;

public class PluginManifest
{
    public List<string> Permissions { get; set; } = [];
    public string Name { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? EntryPoint { get; set; }
    public string? Version { get; set; }
}

public class PluginInfo
{
    public bool IsEnabled { get; set; }
    public List<string> Permissions { get; set; } = [];
    public PluginStatus Status { get; set; } = PluginStatus.Discovered;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? EntryPoint { get; set; }
    public string? Error { get; set; }
    public string? Version { get; set; }
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
    Task EnsurePluginsDirectoryAsync();
    Task SetPluginEnabledAsync(string pluginId, bool enabled);
    Task UnloadPluginAsync(string pluginId);
    Task<bool> LoadPluginAsync(string pluginId);
    Task<bool> RemovePluginAsync(string pluginId);
    Task<bool> ValidatePluginAsync(string pluginId);
    Task<List<PluginInfo>> GetInstalledPluginsAsync();
    Task<PluginInfo?> GetPluginAsync(string pluginId);
}

public class PluginService(
    IOptionsMonitor<AppSettings> appSettings,
    ISettingsService settingsService,
    ILogger<PluginService> logger)
    : IPluginService
{
    private IPluginExecutionEngine? _executionEngine;

    public string PluginsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "plugins");

    public async Task EnsurePluginsDirectoryAsync()
    {
        if (!Directory.Exists(PluginsDirectory))
            try
            {
                Directory.CreateDirectory(PluginsDirectory);
                logger.LogInformation("플러그인 디렉터리가 {Path}에 생성됨", PluginsDirectory);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "플러그인 디렉터리 생성 실패 {Path}", PluginsDirectory);
            }
    }

    public async Task SetPluginEnabledAsync(string pluginId, bool enabled)
    {
        Guard.NotNullOrWhiteSpace(pluginId, nameof(pluginId));

        var settings = appSettings.CurrentValue;
        settings.DisabledPlugins ??= [];

        if (enabled)
            settings.DisabledPlugins.Remove(pluginId);
        else if (!settings.DisabledPlugins.Contains(pluginId))
            settings.DisabledPlugins.Add(pluginId);

        await settingsService.SaveAsync(settings);
        logger.LogInformation("플러그인 '{PluginId}'이 {State}됨", pluginId, enabled ? "활성화" : "비활성화");
    }

    public async Task UnloadPluginAsync(string pluginId)
    {
        if (_executionEngine != null)
            await _executionEngine.UnloadPluginAsync(pluginId);
    }

    public async Task<bool> LoadPluginAsync(string pluginId)
    {
        if (_executionEngine == null)
        {
            logger.LogWarning("플러그인 실행 엔진을 사용할 수 없습니다");
            return false;
        }

        var plugin = await GetPluginAsync(pluginId);
        if (plugin == null) return false;

        return await _executionEngine.LoadPluginAsync(plugin);
    }

    public async Task<bool> RemovePluginAsync(string pluginId)
    {
        var pluginDir = Path.Combine(PluginsDirectory, pluginId);
        if (!Directory.Exists(pluginDir))
            return false;

        try
        {
            Directory.Delete(pluginDir, true);
            logger.LogInformation("플러그인 '{PluginId}' 제거됨", pluginId);

            // Clean up settings
            var settings = appSettings.CurrentValue;
            settings.DisabledPlugins?.Remove(pluginId);
            await settingsService.SaveAsync(settings);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "플러그인 '{PluginId}' 제거 실패", pluginId);
            return false;
        }
    }

    public async Task<bool> ValidatePluginAsync(string pluginId)
    {
        var plugin = await GetPluginAsync(pluginId);
        if (plugin == null) return false;

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(plugin.Name))
            errors.Add("manifest.json: 'name' 필드 누락");

        if (string.IsNullOrWhiteSpace(plugin.EntryPoint))
        {
            errors.Add("manifest.json: 'entryPoint' 필드 누락");
        }
        else
        {
            var entryPath = Path.Combine(plugin.Path, plugin.EntryPoint);
            if (!File.Exists(entryPath))
                errors.Add($"진입점 파일을 찾을 수 없음: {plugin.EntryPoint}");
        }

        if (errors.Count > 0)
        {
            logger.LogWarning("플러그인 '{PluginId}' 유효성 검사 실패: {Errors}", pluginId, string.Join("; ", errors));
            return false;
        }

        return true;
    }

    public async Task<List<PluginInfo>> GetInstalledPluginsAsync()
    {
        var plugins = new List<PluginInfo>();

        if (!Directory.Exists(PluginsDirectory))
        {
            logger.LogInformation(
                "플러그인 디렉토리가 없습니다. 플러그인을 사용하려면 {Path} 디렉토리를 생성하고 플러그인 폴더와 manifest.json을 추가하세요.",
                PluginsDirectory);
            return plugins;
        }

        var settings = appSettings.CurrentValue;

        try
        {
            foreach (var dir in Directory.GetDirectories(PluginsDirectory))
            {
                var pluginId = Path.GetFileName(dir);
                var plugin = await LoadPluginFromDirectoryAsync(dir, pluginId, settings);
                plugins.Add(plugin);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "플러그인 디렉터리 열거 실패");
        }

        return plugins;
    }

    public async Task<PluginInfo?> GetPluginAsync(string pluginId)
    {
        Guard.NotNullOrWhiteSpace(pluginId, nameof(pluginId));

        var pluginDir = Path.Combine(PluginsDirectory, pluginId);
        if (!Directory.Exists(pluginDir))
            return null;

        var settings = appSettings.CurrentValue;
        return await LoadPluginFromDirectoryAsync(pluginDir, pluginId, settings);
    }

    /// <summary>
    ///     Late-bind the execution engine to avoid circular DI.
    /// </summary>
    public void SetExecutionEngine(IPluginExecutionEngine engine)
    {
        _executionEngine = engine;
    }

    private async Task<PluginInfo> LoadPluginFromDirectoryAsync(string dir, string pluginId, AppSettings settings)
    {
        var plugin = new PluginInfo
        {
            Id = pluginId,
            Name = pluginId,
            Path = dir,
            IsEnabled = !(settings.DisabledPlugins?.Contains(pluginId) ?? false),
            Status = PluginStatus.Discovered
        };

        var manifestPath = Path.Combine(dir, "manifest.json");
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
                foreach (var perm in perms.EnumerateArray())
                {
                    var permStr = perm.GetString();
                    if (permStr != null) plugin.Permissions.Add(permStr);
                }

            // Validate entry point existence
            if (!string.IsNullOrEmpty(plugin.EntryPoint))
            {
                var entryPath = Path.Combine(dir, plugin.EntryPoint);
                if (File.Exists(entryPath))
                {
                    plugin.Status = PluginStatus.Valid;
                }
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
            logger.LogDebug(ex, "플러그인 매니페스트 읽기 실패 {Path}", manifestPath);
        }

        return plugin;
    }
}