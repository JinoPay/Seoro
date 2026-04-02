using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISettingsService
{
    event Action<AppSettings>? OnSettingsChanged;
    Task SaveAsync(AppSettings settings);
    Task<AppSettings> LoadAsync();
}