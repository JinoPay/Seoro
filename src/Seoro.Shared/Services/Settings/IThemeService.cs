namespace Seoro.Shared.Services.Settings;

public interface IThemeService : IDisposable
{
    bool IsDarkMode { get; }
    event Action? OnThemeChanged;
    Task InitializeAsync();
    Task ToggleDarkModeAsync();
}
