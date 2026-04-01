using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ShellService : IShellService
{
    private readonly ILogger<ShellService> _logger;
    private readonly IProcessRunner _processRunner;
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ShellInfo? _cached;
    private DateTime _cachedAt;
    private List<ShellInfo>? _availableShellsCache;

    private string? _cachedLoginPath;
    private DateTime _loginPathCachedAt;
    private readonly SemaphoreSlim _pathLock = new(1, 1);

    public ShellService(ILogger<ShellService> logger, IProcessRunner processRunner, ISettingsService settingsService)
    {
        _logger = logger;
        _processRunner = processRunner;
        _settingsService = settingsService;
    }

    public async Task<ShellInfo> GetShellAsync()
    {
        if (_cached != null && DateTime.UtcNow - _cachedAt < CominomiConstants.ShellCacheTtl)
            return _cached;

        await _lock.WaitAsync();
        try
        {
            if (_cached != null && DateTime.UtcNow - _cachedAt < CominomiConstants.ShellCacheTtl)
                return _cached;

            _cached = await ResolveShellAsync();
            _cachedAt = DateTime.UtcNow;
            _logger.LogInformation("Resolved shell: {Type} at {Path}", _cached.Type, _cached.FileName);
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ShellInfo> GetTerminalShellAsync()
    {
        var settings = await _settingsService.LoadAsync();
        if (string.IsNullOrEmpty(settings.TerminalShell))
            return await GetShellAsync();

        var available = await GetAvailableShellsAsync();
        var match = available.FirstOrDefault(s => ShellTypeToKey(s.Type) == settings.TerminalShell);
        return match ?? await GetShellAsync();
    }

    public async Task<List<ShellInfo>> GetAvailableShellsAsync()
    {
        if (_availableShellsCache != null)
            return _availableShellsCache;

        var shells = new List<ShellInfo>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (File.Exists("/bin/zsh")) shells.Add(new("/bin/zsh", "-c ", ShellType.Zsh));
            if (File.Exists("/bin/bash")) shells.Add(new("/bin/bash", "-c ", ShellType.Bash));
            if (File.Exists("/bin/sh")) shells.Add(new("/bin/sh", "-c ", ShellType.Sh));
        }
        else
        {
            var bashPath = await FindGitBashAsync();
            if (bashPath != null) shells.Add(new(bashPath, "-c ", ShellType.Bash));
            shells.Add(new("cmd.exe", "/c ", ShellType.Cmd));

            // PowerShell 7+ (pwsh) or Windows PowerShell 5.1
            var pwshPath = await FindPowerShellAsync();
            if (pwshPath != null) shells.Add(new(pwshPath, "-Command ", ShellType.PowerShell));
        }

        _availableShellsCache = shells;
        return shells;
    }

    public void InvalidateCache()
    {
        _cached = null;
        _availableShellsCache = null;
        _cachedLoginPath = null;
    }

    public async Task<string?> GetLoginShellPathAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Environment.GetEnvironmentVariable("PATH");

        if (_cachedLoginPath != null && DateTime.UtcNow - _loginPathCachedAt < CominomiConstants.ShellCacheTtl)
            return _cachedLoginPath;

        await _pathLock.WaitAsync();
        try
        {
            if (_cachedLoginPath != null && DateTime.UtcNow - _loginPathCachedAt < CominomiConstants.ShellCacheTtl)
                return _cachedLoginPath;

            _cachedLoginPath = await CaptureLoginShellPathAsync();
            _loginPathCachedAt = DateTime.UtcNow;

            if (_cachedLoginPath != null)
                _logger.LogInformation("Captured login shell PATH ({Length} chars)", _cachedLoginPath.Length);
            else
                _logger.LogWarning("Failed to capture login shell PATH, falling back to process PATH");

            return _cachedLoginPath;
        }
        finally
        {
            _pathLock.Release();
        }
    }

    private async Task<string?> CaptureLoginShellPathAsync()
    {
        var shell = ResolveUnixShell();
        var sentinel = CominomiConstants.PathCaptureSentinel;

        // Source the user's rc file explicitly so PATH includes tool version managers
        // (mise, nvm, volta, fnm, etc.). The -l flag sources .zprofile/.bash_profile,
        // and the explicit source covers .zshrc/.bashrc where most activations live.
        var rcFile = shell.Type == ShellType.Bash ? "~/.bashrc" : "~/.zshrc";
        var command = $"source {rcFile} 2>/dev/null; echo {sentinel}; echo $PATH";

        try
        {
            var result = await _processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = shell.FileName,
                Arguments = ["-l", "-c", command],
                Timeout = CominomiConstants.WhichTimeout
            });

            if (!result.Success)
                return null;

            // Parse PATH from output: find sentinel marker, take the next line
            var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length - 1; i++)
            {
                if (lines[i].Trim() == sentinel)
                {
                    var path = lines[i + 1].Trim();
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
            }

            _logger.LogDebug("PATH capture: sentinel not found in shell output");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Login shell PATH capture timed out");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Login shell PATH capture failed");
        }

        return null;
    }

    public async Task<string?> WhichAsync(string executableName)
    {
        var shell = await GetShellAsync();

        if (!IsValidExecutableName(executableName))
        {
            _logger.LogWarning("Invalid executable name rejected: {Name}", executableName);
            return null;
        }

        try
        {
            ProcessRunOptions options;

            switch (shell.Type)
            {
                case ShellType.Cmd:
                    options = new ProcessRunOptions
                    {
                        FileName = "where.exe",
                        Arguments = [executableName],
                        Timeout = CominomiConstants.WhichTimeout
                    };
                    break;

                case ShellType.Bash when RuntimeInformation.IsOSPlatform(OSPlatform.Windows):
                    // Windows Git Bash: convert Unix path to Windows path via cygpath
                    options = new ProcessRunOptions
                    {
                        FileName = shell.FileName,
                        Arguments = ["-c", $"cygpath -w \"$(which {executableName})\""],
                        Timeout = CominomiConstants.WhichTimeout
                    };
                    break;

                default: // ShellType.Sh, Zsh, Bash — macOS/Linux
                    // Inject login shell PATH so tool version managers (mise, nvm, volta)
                    // are visible even when the GUI app inherits a minimal PATH.
                    var loginPath = await GetLoginShellPathAsync();
                    options = new ProcessRunOptions
                    {
                        FileName = shell.FileName,
                        Arguments = ["-c", $"which {executableName}"],
                        Timeout = CominomiConstants.WhichTimeout,
                        EnvironmentVariables = loginPath != null
                            ? new Dictionary<string, string> { ["PATH"] = loginPath }
                            : null
                    };
                    break;
            }

            var result = await _processRunner.RunAsync(options);
            var firstLine = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

            if (result.Success && !string.IsNullOrWhiteSpace(firstLine))
                return firstLine;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("WhichAsync timed out for: {Name} (timeout={Timeout}s)", executableName, CominomiConstants.WhichTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WhichAsync failed for: {Name}", executableName);
        }

        // Fallback for macOS: GUI apps launched from Launchpad/Finder inherit a minimal
        // PATH that excludes Homebrew and tool version manager directories.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] wellKnownPaths =
            [
                Path.Combine(home, ".local", "bin", executableName),                        // Anthropic installer, pip
                Path.Combine(home, ".local", "share", "mise", "shims", executableName),     // mise
                Path.Combine(home, ".volta", "bin", executableName),                        // volta
                $"/opt/homebrew/bin/{executableName}",                                      // Apple Silicon Homebrew
                $"/usr/local/bin/{executableName}",                                         // Intel Homebrew
                Path.Combine(home, ".npm", "bin", executableName)                           // npm global
            ];

            foreach (var candidate in wellKnownPaths)
            {
                if (File.Exists(candidate))
                {
                    _logger.LogDebug("WhichAsync: found {Name} via well-known path fallback: {Path}",
                        executableName, candidate);
                    return candidate;
                }
            }
        }

        return null;
    }

    public static string ShellTypeToKey(ShellType type) => type switch
    {
        ShellType.Zsh => "zsh",
        ShellType.Bash when !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => "bash",
        ShellType.Bash => "gitbash",
        ShellType.Sh => "sh",
        ShellType.Cmd => "cmd",
        ShellType.PowerShell => "powershell",
        _ => "sh"
    };

    public static string GetShellDisplayName(ShellInfo shell) => shell.Type switch
    {
        ShellType.Zsh => "zsh",
        ShellType.Bash when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => "Git Bash",
        ShellType.Bash => "bash",
        ShellType.Sh => "sh",
        ShellType.Cmd => "cmd",
        ShellType.PowerShell => "PowerShell",
        _ => shell.FileName
    };

    private static bool IsValidExecutableName(string name)
        => !string.IsNullOrEmpty(name) && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');

    private Task<ShellInfo> ResolveShellAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult(ResolveUnixShell());

        return ResolveWindowsShellAsync();
    }

    private ShellInfo ResolveUnixShell()
    {
        // Detect user's default shell from $SHELL
        var envShell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(envShell) && File.Exists(envShell))
        {
            var type = Path.GetFileName(envShell) switch
            {
                "zsh" => ShellType.Zsh,
                "bash" => ShellType.Bash,
                _ => ShellType.Sh
            };
            return new ShellInfo(envShell, "-c ", type);
        }

        // Fallback chain: /bin/zsh → /bin/bash → /bin/sh
        if (File.Exists("/bin/zsh")) return new ShellInfo("/bin/zsh", "-c ", ShellType.Zsh);
        if (File.Exists("/bin/bash")) return new ShellInfo("/bin/bash", "-c ", ShellType.Bash);
        return new ShellInfo("/bin/sh", "-c ", ShellType.Sh);
    }

    private async Task<ShellInfo> ResolveWindowsShellAsync()
    {
        // Try to find Git Bash via git installation
        var bashPath = await FindGitBashAsync();
        if (bashPath != null)
            return new ShellInfo(bashPath, "-c ", ShellType.Bash);

        // Fallback to cmd.exe
        return new ShellInfo("cmd.exe", "/c ", ShellType.Cmd);
    }

    private async Task<string?> FindGitBashAsync()
    {
        // Strategy 1: Find git via where.exe, then resolve bash.exe relative to it
        try
        {
            var result = await _processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = "where.exe",
                Arguments = ["git"],
                Timeout = CominomiConstants.WhichTimeout
            });

            var gitPath = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

            if (result.Success && !string.IsNullOrWhiteSpace(gitPath))
            {
                // git.exe is typically at <GitRoot>/cmd/git.exe or <GitRoot>/bin/git.exe
                var gitDir = Path.GetDirectoryName(gitPath);
                if (gitDir != null)
                {
                    var gitRoot = Path.GetDirectoryName(gitDir);
                    if (gitRoot != null)
                    {
                        var bashCandidate = Path.Combine(gitRoot, "bin", "bash.exe");
                        if (File.Exists(bashCandidate))
                        {
                            _logger.LogDebug("Found Git Bash via git path: {Path}", bashCandidate);
                            return bashCandidate;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to find git via where.exe");
        }

        // Strategy 2: Check well-known installation paths
        string[] wellKnownPaths =
        [
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Git", "bin", "bash.exe")
        ];

        foreach (var path in wellKnownPaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("Found Git Bash at well-known path: {Path}", path);
                return path;
            }
        }

        _logger.LogDebug("Git Bash not found, will fall back to cmd.exe");
        return null;
    }

    private async Task<string?> FindPowerShellAsync()
    {
        // PowerShell 7+ (pwsh)
        string[] pwshPaths =
        [
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files (x86)\PowerShell\7\pwsh.exe"
        ];

        foreach (var path in pwshPaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Try via where.exe
        try
        {
            var result = await _processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = "where.exe",
                Arguments = ["pwsh"],
                Timeout = CominomiConstants.WhichTimeout
            });

            var firstLine = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (result.Success && !string.IsNullOrWhiteSpace(firstLine))
                return firstLine;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "PowerShell detection via where.exe failed"); }

        // Fallback to Windows PowerShell 5.1
        var winPwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(winPwsh))
            return winPwsh;

        return null;
    }
}
