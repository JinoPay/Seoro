using System.Diagnostics;
using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Cominomi.Desktop.Services;

public class LauncherService : ILauncherService
{
    private readonly IShellService _shell;
    private readonly ILogger<LauncherService> _logger;
    private List<IdeInfo>? _cachedIdes;

    private record IdeDefinition(
        string Name, string Icon,
        string? WinCommand, string? MacCommand, string? MacAppName);

    private static readonly IdeDefinition[] IdeDefinitions =
    [
        new("VS Code",       "vscode",   "code",       "code",      "Visual Studio Code"),
        new("Cursor",        "cursor",   "cursor",     "cursor",    "Cursor"),
        new("Windsurf",      "windsurf", "windsurf",   "windsurf",  "Windsurf"),
        new("Visual Studio", "vs",       "devenv",     null,        null),
        new("Rider",         "rider",    "rider64",    "rider",     "Rider"),
        new("WebStorm",      "webstorm", "webstorm64", "webstorm",  "WebStorm"),
        new("IntelliJ IDEA", "idea",     "idea64",     "idea",      "IntelliJ IDEA"),
        new("GoLand",        "goland",   "goland64",   "goland",    "GoLand"),
        new("Fleet",         "fleet",    "fleet",      "fleet",     "Fleet"),
        new("Zed",           "zed",      null,         "zed",       "Zed"),
        new("Sublime Text",  "sublime",  "subl",       "subl",      "Sublime Text"),
    ];

    public LauncherService(IShellService shell, ILogger<LauncherService> logger)
    {
        _shell = shell;
        _logger = logger;
    }

    public async Task OpenFolderAsync(string folderPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = false
                });
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open folder: {Path}", folderPath);
        }
    }

    public async Task OpenInIdeAsync(string folderPath, string ideCommand)
    {
        try
        {
            var ide = _cachedIdes?.FirstOrDefault(i => i.Command == ideCommand);

            if ((ide?.LaunchMode ?? IdeLaunchMode.Cli) == IdeLaunchMode.MacApp && OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-a \"{ideCommand}\" \"{folderPath}\"",
                    UseShellExecute = false
                });
                return;
            }

            var resolved = await _shell.WhichAsync(ideCommand);
            var command = resolved ?? ideCommand;

            Process.Start(new ProcessStartInfo
            {
                FileName = command,
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = OperatingSystem.IsWindows()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open IDE {Ide} for path: {Path}", ideCommand, folderPath);
        }
    }

    public async Task<List<IdeInfo>> GetAvailableIdesAsync()
    {
        if (_cachedIdes != null)
            return _cachedIdes;

        var result = new List<IdeInfo>();

        foreach (var def in IdeDefinitions)
        {
            try
            {
                var detected = await TryDetectIdeAsync(def);
                if (detected != null)
                    result.Add(detected);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "IDE detection skipped: {Name}", def.Name);
            }
        }

        _cachedIdes = result;
        return result;
    }

    private async Task<IdeInfo?> TryDetectIdeAsync(IdeDefinition def)
    {
        var command = OperatingSystem.IsMacOS() ? def.MacCommand : def.WinCommand;
        if (command == null) return null;

        // Tier 1: CLI command via WhichAsync
        var path = await _shell.WhichAsync(command);
        if (path != null)
            return new IdeInfo(def.Name, command, def.Icon);

        if (OperatingSystem.IsMacOS())
        {
            // Tier 2: JetBrains Toolbox scripts
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var toolboxScript = Path.Combine(
                home, "Library", "Application Support",
                "JetBrains", "Toolbox", "scripts", command);

            if (File.Exists(toolboxScript))
                return new IdeInfo(def.Name, toolboxScript, def.Icon);

            // Tier 3: macOS .app bundle
            if (def.MacAppName != null)
            {
                var systemApp = $"/Applications/{def.MacAppName}.app";
                var userApp = Path.Combine(home, "Applications", $"{def.MacAppName}.app");

                if (Directory.Exists(systemApp) || Directory.Exists(userApp))
                    return new IdeInfo(def.Name, def.MacAppName, def.Icon, IdeLaunchMode.MacApp);
            }
        }

        return null;
    }
}
