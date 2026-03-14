using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISettingsService
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
}
