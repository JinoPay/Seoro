using System.Text.Json;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Services;

/// <summary>
/// Loads AppSettings from the JSON settings file each time IOptionsMonitor needs a fresh instance.
/// </summary>
public class AppSettingsFactory : IOptionsFactory<AppSettings>
{
    public AppSettings Create(string name)
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options) ?? new AppSettings();
            settings.DefaultModel = ModelDefinitions.NormalizeModelId(settings.DefaultModel);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }
}
