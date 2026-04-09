
namespace Seoro.Shared.Services.Settings;

public interface ISettingsService
{
    event Action<AppSettings>? OnSettingsChanged;
    Task SaveAsync(AppSettings settings);
    Task<AppSettings> LoadAsync();
}