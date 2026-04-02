using Cominomi.Shared.Models;
using Cominomi.Shared.Services.Migration;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SettingsService(AppSettingsChangeNotifier changeNotifier, ILogger<SettingsService> logger)
    : ISettingsService
{
    private readonly string _settingsPath = AppPaths.SettingsFile;
    private AppSettings? _cached;

    public event Action<AppSettings>? OnSettingsChanged;

    public async Task SaveAsync(AppSettings settings)
    {
        SettingsValidator.Sanitize(settings);
        _cached = settings;
        var json = MigratingJsonWriter.Write(settings, JsonDefaults.Options);
        await AtomicFileWriter.WriteAsync(_settingsPath, json);
        OnSettingsChanged?.Invoke(settings);
        changeNotifier.NotifyChanged();
        logger.LogDebug("Settings saved to {Path}", _settingsPath);
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (_cached != null)
            return _cached;

        if (!File.Exists(_settingsPath))
        {
            logger.LogDebug("Settings file not found, using defaults");
            _cached = new AppSettings();
            return _cached;
        }

        var json = await File.ReadAllTextAsync(_settingsPath);
        var (settings, migrated, migratedJson) = MigratingJsonReader.Read<AppSettings>(json, JsonDefaults.Options);
        _cached = settings ?? new AppSettings();
        _cached.DefaultModel = ModelDefinitions.NormalizeModelId(_cached.DefaultModel);
        SettingsValidator.Sanitize(_cached);
        if (migrated && migratedJson != null)
        {
            await AtomicFileWriter.WriteAsync(_settingsPath, migratedJson);
            logger.LogInformation("Settings migrated from disk");
        }

        logger.LogDebug("Settings loaded from {Path}", _settingsPath);
        return _cached;
    }
}