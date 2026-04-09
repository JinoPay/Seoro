using MudBlazor;

namespace Seoro.Shared.Services.Settings;

public interface IThemeService : IDisposable
{
    bool IsDarkMode { get; }
    MudTheme Theme { get; }
    event Action? OnThemeChanged;
    Task InitializeAsync();
    Task ToggleDarkModeAsync();
}