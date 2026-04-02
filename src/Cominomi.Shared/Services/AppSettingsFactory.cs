using Cominomi.Shared.Models;
using Cominomi.Shared.Services.Migration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Services;

/// <summary>
///     Loads AppSettings from the JSON settings file each time IOptionsMonitor needs a fresh instance.
/// </summary>
public class AppSettingsFactory(ILogger<AppSettingsFactory> logger) : IOptionsFactory<AppSettings>
{
    public AppSettings Create(string name)
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            var (settings, migrated, migratedJson) = MigratingJsonReader.Read<AppSettings>(json, JsonDefaults.Options);
            var result = settings ?? new AppSettings();
            result.DefaultModel = ModelDefinitions.NormalizeModelId(result.DefaultModel);
            if (migrated && migratedJson != null)
                AtomicFileWriter.WriteAsync(path, migratedJson).GetAwaiter().GetResult();
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load settings from {Path}, using defaults", path);
            return new AppSettings();
        }
    }
}