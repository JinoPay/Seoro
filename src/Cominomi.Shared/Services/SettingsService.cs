using System.Text.Json;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services.Migration;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SettingsService : ISettingsService
{
    public event Action<AppSettings>? OnSettingsChanged;

    private readonly string _settingsPath = AppPaths.SettingsFile;
    private readonly AppSettingsChangeNotifier _changeNotifier;
    private readonly ILogger<SettingsService> _logger;
    private AppSettings? _cached;

    public SettingsService(AppSettingsChangeNotifier changeNotifier, ILogger<SettingsService> logger)
    {
        _changeNotifier = changeNotifier;
        _logger = logger;
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (_cached != null)
            return _cached;

        if (!File.Exists(_settingsPath))
        {
            _logger.LogDebug("Settings file not found, using defaults");
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
            _logger.LogInformation("Settings migrated from disk");
        }
        _logger.LogDebug("Settings loaded from {Path}", _settingsPath);
        return _cached;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        SettingsValidator.Sanitize(settings);
        _cached = settings;
        var json = MigratingJsonWriter.Write(settings, JsonDefaults.Options);
        await AtomicFileWriter.WriteAsync(_settingsPath, json);
        OnSettingsChanged?.Invoke(settings);
        _changeNotifier.NotifyChanged();
        _logger.LogDebug("Settings saved to {Path}", _settingsPath);
    }
}
