using MudBlazor;

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

    public MudTheme Theme { get; } = new()
    {
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Pretendard", "-apple-system", "BlinkMacSystemFont", "Segoe UI", "sans-serif" },
                FontSize = "0.875rem",
                LineHeight = "1.5"
            },
            Body1 = new Body1Typography { FontSize = "0.875rem", LineHeight = "1.5" },
            Body2 = new Body2Typography { FontSize = "0.8125rem", LineHeight = "1.5" },
            Caption = new CaptionTypography { FontSize = "0.75rem" },
            Button = new ButtonTypography
            {
                FontSize = "0.8125rem",
                FontWeight = "500",
                TextTransform = "none",
                LetterSpacing = "0.01em"
            }
        },
        PaletteLight = new PaletteLight
        {
            Primary = "#7c3aed",
            Secondary = "#2563eb",
            Tertiary = "#16a34a",
            Background = "#ffffff",
            Surface = "#f1f5f9",
            AppbarBackground = "#f8fafc",
            AppbarText = "rgba(0,0,0,0.87)",
            DrawerBackground = "#f8fafc",
            DrawerText = "rgba(0,0,0,0.87)",
            TextPrimary = "rgba(0,0,0,0.87)",
            TextSecondary = "rgba(0,0,0,0.60)",
            TextDisabled = "rgba(0,0,0,0.38)",
            ActionDefault = "rgba(0,0,0,0.54)",
            ActionDisabled = "rgba(0,0,0,0.26)",
            LinesDefault = "rgba(0,0,0,0.10)",
            LinesInputs = "rgba(0,0,0,0.10)",
            Divider = "rgba(0,0,0,0.10)",
            Success = "#16a34a",
            Warning = "#d97706",
            Error = "#dc2626",
            Info = "#2563eb"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#a78bfa",
            Secondary = "#93c5fd",
            Tertiary = "#86efac",
            Background = "#0f172a",
            Surface = "#1e293b",
            AppbarBackground = "#0f172a",
            AppbarText = "rgba(255,255,255,0.87)",
            DrawerBackground = "#0c1322",
            DrawerText = "rgba(255,255,255,0.87)",
            TextPrimary = "rgba(255,255,255,0.87)",
            TextSecondary = "rgba(255,255,255,0.60)",
            TextDisabled = "rgba(255,255,255,0.38)",
            ActionDefault = "rgba(255,255,255,0.60)",
            ActionDisabled = "rgba(255,255,255,0.26)",
            LinesDefault = "rgba(255,255,255,0.10)",
            LinesInputs = "rgba(255,255,255,0.10)",
            Divider = "rgba(255,255,255,0.10)",
            Success = "#86efac",
            Warning = "#fcd34d",
            Error = "#fca5a5",
            Info = "#93c5fd"
        }
    };

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