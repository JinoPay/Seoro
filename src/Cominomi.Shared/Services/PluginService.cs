using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class PluginInfo
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Path { get; set; } = "";
}

public interface IPluginService
{
    Task<List<PluginInfo>> GetInstalledPluginsAsync();
}

public class PluginService : IPluginService
{
    private readonly ILogger<PluginService> _logger;

    public PluginService(ILogger<PluginService> logger)
    {
        _logger = logger;
    }

    public async Task<List<PluginInfo>> GetInstalledPluginsAsync()
    {
        var plugins = new List<PluginInfo>();
        var pluginsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "plugins");

        if (!Directory.Exists(pluginsDir))
            return plugins;

        try
        {
            foreach (var dir in Directory.GetDirectories(pluginsDir))
            {
                var manifestPath = System.IO.Path.Combine(dir, "manifest.json");
                var plugin = new PluginInfo
                {
                    Name = System.IO.Path.GetFileName(dir),
                    Path = dir
                };

                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(manifestPath);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("name", out var name))
                            plugin.Name = name.GetString() ?? plugin.Name;
                        if (doc.RootElement.TryGetProperty("description", out var desc))
                            plugin.Description = desc.GetString();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to read plugin manifest at {Path}", manifestPath);
                    }
                }

                plugins.Add(plugin);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate plugins directory");
        }

        return plugins;
    }
}
