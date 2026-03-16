using System.Text.Json;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class SettingsService : ISettingsService
{
    public event Action<AppSettings>? OnSettingsChanged;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;
    private AppSettings? _cached;

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cominomi");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
    }

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
        _cached = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        _cached.DefaultModel = ModelDefinitions.NormalizeModelId(_cached.DefaultModel);
        return _cached;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        _cached = settings;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json);
        OnSettingsChanged?.Invoke(settings);
    }
}
