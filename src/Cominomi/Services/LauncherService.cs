using System.Diagnostics;
using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Cominomi.Services;

public class LauncherService : ILauncherService
{
    private readonly IShellService _shell;
    private readonly ILogger<LauncherService> _logger;
    private List<IdeInfo>? _cachedIdes;

    private static readonly (string Name, string Command, string Icon)[] KnownIdes =
    [
        ("VS Code", "code", "vscode"),
        ("Cursor", "cursor", "cursor"),
        ("Visual Studio", "devenv", "vs"),
        ("Rider", "rider64", "rider"),
        ("WebStorm", "webstorm64", "webstorm"),
        ("IntelliJ IDEA", "idea64", "idea"),
        ("GoLand", "goland64", "goland"),
        ("Fleet", "fleet", "fleet"),
        ("Zed", "zed", "zed"),
        ("Sublime Text", "subl", "sublime"),
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
#if WINDOWS
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
#elif MACCATALYST
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = false
            });
#endif
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

        foreach (var (name, command, icon) in KnownIdes)
        {
            try
            {
                var path = await _shell.WhichAsync(command);
                if (path != null)
                    result.Add(new IdeInfo(name, command, icon));
            }
            catch
            {
                // skip
            }
        }

        _cachedIdes = result;
        return result;
    }
}
