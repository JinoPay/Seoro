using Cominomi.Shared.Models;
using MudBlazor;

namespace Cominomi.Shared.Services;

public class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;

    public bool IsDarkMode { get; private set; } = true;

    public MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#7c3aed",
            AppbarBackground = "#f5f5f5",
            AppbarText = "#333333",
            Background = "#ffffff",
            Surface = "#f5f5f5",
            DrawerBackground = "#fafafa",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#a78bfa",
            AppbarBackground = "#1e1e2e",
            AppbarText = "#cdd6f4",
            Background = "#1e1e2e",
            Surface = "#313244",
            DrawerBackground = "#11111b",
            TextPrimary = "#cdd6f4",
            TextSecondary = "#a6adc8",
            LinesDefault = "rgba(166,173,200,0.12)",
        }
    };

    public event Action? OnThemeChanged;

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _settingsService.OnSettingsChanged += HandleSettingsChanged;
    }

    public async Task InitializeAsync()
    {
        var settings = await _settingsService.LoadAsync();
        IsDarkMode = settings.Theme != "light";
    }

    public async Task ToggleDarkModeAsync()
    {
        IsDarkMode = !IsDarkMode;
        var settings = await _settingsService.LoadAsync();
        settings.Theme = IsDarkMode ? "dark" : "light";
        await _settingsService.SaveAsync(settings);
        OnThemeChanged?.Invoke();
    }

    private void HandleSettingsChanged(AppSettings settings)
    {
        IsDarkMode = settings.Theme != "light";
        OnThemeChanged?.Invoke();
    }

    public void Dispose()
    {
        _settingsService.OnSettingsChanged -= HandleSettingsChanged;
    }
}
