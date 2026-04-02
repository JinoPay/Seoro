using MudBlazor;

namespace Cominomi.Shared.Services;

public interface IThemeService : IDisposable
{
    bool IsDarkMode { get; }
    MudTheme Theme { get; }
    event Action? OnThemeChanged;
    Task InitializeAsync();
    Task ToggleDarkModeAsync();
}