using System.Text.Json;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class SettingsService : ISettingsService
{
    public event Action<AppSettings>? OnSettingsChanged;

    private readonly string _settingsPath = AppPaths.SettingsFile;
    private AppSettings? _cached;


    public async Task<AppSettings> LoadAsync()
    {
        if (_cached != null)
            return _cached;

        if (!File.Exists(_settingsPath))
        {
            _cached = new AppSettings();
            return _cached;
        }

        var json = await File.ReadAllTextAsync(_settingsPath);
        _cached = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options) ?? new AppSettings();
        _cached.DefaultModel = ModelDefinitions.NormalizeModelId(_cached.DefaultModel);
        return _cached;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        _cached = settings;
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        await File.WriteAllTextAsync(_settingsPath, json);
        OnSettingsChanged?.Invoke(settings);
    }
}
