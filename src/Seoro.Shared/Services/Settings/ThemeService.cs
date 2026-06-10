namespace Seoro.Shared.Services.Settings;

public class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _settingsService.OnSettingsChanged += HandleSettingsChanged;
    }

    public void Dispose()
    {
        _settingsService.OnSettingsChanged -= HandleSettingsChanged;
    }

    public event Action? OnThemeChanged;

    public bool IsDarkMode { get; private set; } = true;

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
}
