using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Infrastructure;

public class ShellService(ILogger<ShellService> logger, IProcessRunner processRunner, ISettingsService settingsService)
    : IShellService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SemaphoreSlim _pathLock = new(1, 1);
    private DateTime _cachedAt;
    private DateTime _loginPathCachedAt;
    private List<ShellInfo>? _availableShellsCache;
    private ShellInfo? _cached;

    private string? _cachedLoginPath;

    public async Task<List<ShellInfo>> GetAvailableShellsAsync()
    {
        if (_availableShellsCache != null)
            return _availableShellsCache;

        var shells = new List<ShellInfo>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (File.Exists("/bin/zsh")) shells.Add(new ShellInfo("/bin/zsh", "-c ", ShellType.Zsh));
            if (File.Exists("/bin/bash")) shells.Add(new ShellInfo("/bin/bash", "-c ", ShellType.Bash));
            if (File.Exists("/bin/sh")) shells.Add(new ShellInfo("/bin/sh", "-c ", ShellType.Sh));
        }
        else
        {
            var bashPath = await FindGitBashAsync();
            if (bashPath != null) shells.Add(new ShellInfo(bashPath, "-c ", ShellType.Bash));
            shells.Add(new ShellInfo("cmd.exe", "/c ", ShellType.Cmd));

            // PowerShell 7+ (pwsh) or Windows PowerShell 5.1
            var pwshPath = await FindPowerShellAsync();
            if (pwshPath != null) shells.Add(new ShellInfo(pwshPath, "-Command ", ShellType.PowerShell));
        }

        _availableShellsCache = shells;
        return shells;
    }

    public async Task<ShellInfo> GetShellAsync()
    {
        if (_cached != null && DateTime.UtcNow - _cachedAt < SeoroConstants.ShellCacheTtl)
            return _cached;

        await _lock.WaitAsync();
        try
        {
            if (_cached != null && DateTime.UtcNow - _cachedAt < SeoroConstants.ShellCacheTtl)
                return _cached;

            _cached = await ResolveShellAsync();
            _cachedAt = DateTime.UtcNow;
            logger.LogInformation("셸 확인됨: {Type} at {Path}", _cached.Type, _cached.FileName);
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ShellInfo> GetTerminalShellAsync()
    {
        var settings = await settingsService.LoadAsync();
        if (string.IsNullOrEmpty(settings.TerminalShell))
            return await GetShellAsync();

        var available = await GetAvailableShellsAsync();
        var match = available.FirstOrDefault(s => ShellTypeToKey(s.Type) == settings.TerminalShell);
        return match ?? await GetShellAsync();
    }

    public async Task<string?> GetLoginShellPathAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Environment.GetEnvironmentVariable("PATH");

        if (_cachedLoginPath != null && DateTime.UtcNow - _loginPathCachedAt < SeoroConstants.ShellCacheTtl)
            return _cachedLoginPath;

        await _pathLock.WaitAsync();
        try
        {
            if (_cachedLoginPath != null && DateTime.UtcNow - _loginPathCachedAt < SeoroConstants.ShellCacheTtl)
                return _cachedLoginPath;

            _cachedLoginPath = await CaptureLoginShellPathAsync();
            _loginPathCachedAt = DateTime.UtcNow;

            if (_cachedLoginPath != null)
                logger.LogInformation("로그인 셸 PATH 캡처됨 ({Length} 글자)", _cachedLoginPath.Length);
            else
                logger.LogWarning("로그인 셸 PATH 캡처 실패, 프로세스 PATH로 대체");

            return _cachedLoginPath;
        }
        finally
        {
            _pathLock.Release();
        }
    }

    public async Task<string?> WhichAsync(string executableName)
    {
        var shell = await GetShellAsync();

        if (!IsValidExecutableName(executableName))
        {
            logger.LogWarning("유효하지 않은 실행파일 이름 거부됨: {Name}", executableName);
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
                        Timeout = SeoroConstants.WhichTimeout
                    };
                    break;

                case ShellType.Bash when RuntimeInformation.IsOSPlatform(OSPlatform.Windows):
                    // Windows Git Bash: convert Unix path to Windows path via cygpath
                    options = new ProcessRunOptions
                    {
                        FileName = shell.FileName,
                        Arguments = ["-c", $"cygpath -w \"$(which {executableName})\""],
                        Timeout = SeoroConstants.WhichTimeout
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
                        Timeout = SeoroConstants.WhichTimeout,
                        EnvironmentVariables = loginPath != null
                            ? new Dictionary<string, string> { ["PATH"] = loginPath }
                            : null
                    };
                    break;
            }

            var result = await processRunner.RunAsync(options);
            var firstLine = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

            if (result.Success && !string.IsNullOrWhiteSpace(firstLine))
                return firstLine;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("WhichAsync 타임아웃: {Name} (타임아웃={Timeout}초)", executableName,
                SeoroConstants.WhichTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "WhichAsync 실패: {Name}", executableName);
        }

        // Fallback for macOS: GUI apps launched from Launchpad/Finder inherit a minimal
        // PATH that excludes Homebrew and tool version manager directories.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] wellKnownPaths =
            [
                Path.Combine(home, ".local", "bin", executableName), // Anthropic installer, pip
                Path.Combine(home, ".local", "share", "mise", "shims", executableName), // mise
                Path.Combine(home, ".volta", "bin", executableName), // volta
                $"/opt/homebrew/bin/{executableName}", // Apple Silicon Homebrew
                $"/usr/local/bin/{executableName}", // Intel Homebrew
                Path.Combine(home, ".npm", "bin", executableName), // npm global
                Path.Combine(home, "Library", "Application Support", "JetBrains",
                    "Toolbox", "scripts", executableName) // JetBrains Toolbox
            ];

            foreach (var candidate in wellKnownPaths)
                if (File.Exists(candidate))
                {
                    logger.LogDebug("WhichAsync: 잘알려진 경로 대체를 통해 {Name} 발견: {Path}",
                        executableName, candidate);
                    return candidate;
                }
        }

        return null;
    }

    public void InvalidateCache()
    {
        _cached = null;
        _availableShellsCache = null;
        _cachedLoginPath = null;
    }

    public static string GetShellDisplayName(ShellInfo shell)
    {
        return shell.Type switch
        {
            ShellType.Zsh => "zsh",
            ShellType.Bash when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => "Git Bash",
            ShellType.Bash => "bash",
            ShellType.Sh => "sh",
            ShellType.Cmd => "cmd",
            ShellType.PowerShell => "PowerShell",
            _ => shell.FileName
        };
    }

    public static string ShellTypeToKey(ShellType type)
    {
        return type switch
        {
            ShellType.Zsh => "zsh",
            ShellType.Bash when !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => "bash",
            ShellType.Bash => "gitbash",
            ShellType.Sh => "sh",
            ShellType.Cmd => "cmd",
            ShellType.PowerShell => "powershell",
            _ => "sh"
        };
    }

    private static bool IsValidExecutableName(string name)
    {
        return !string.IsNullOrEmpty(name) && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
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

    private Task<ShellInfo> ResolveShellAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult(ResolveUnixShell());

        return ResolveWindowsShellAsync();
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

    private async Task<string?> CaptureLoginShellPathAsync()
    {
        var shell = ResolveUnixShell();
        var sentinel = SeoroConstants.PathCaptureSentinel;

        // Source the user's rc file explicitly so PATH includes tool version managers
        // (mise, nvm, volta, fnm, etc.). The -l flag sources .zprofile/.bash_profile,
        // and the explicit source covers .zshrc/.bashrc where most activations live.
        var rcFile = shell.Type == ShellType.Bash ? "~/.bashrc" : "~/.zshrc";
        var command = $"source {rcFile} 2>/dev/null; echo {sentinel}; echo $PATH";

        try
        {
            var result = await processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = shell.FileName,
                Arguments = ["-l", "-c", command],
                Timeout = SeoroConstants.WhichTimeout
            });

            if (!result.Success)
                return null;

            // Parse PATH from output: find sentinel marker, take the next line
            var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length - 1; i++)
                if (lines[i].Trim() == sentinel)
                {
                    var path = lines[i + 1].Trim();
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }

            logger.LogDebug("PATH 캡처: 셸 출력에서 센티널을 찾을 수 없음");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("로그인 셸 PATH 캡처 타임아웃");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "로그인 셸 PATH 캡처 실패");
        }

        return null;
    }

    private async Task<string?> FindGitBashAsync()
    {
        // Strategy 1: Find git via where.exe, then resolve bash.exe relative to it
        try
        {
            var result = await processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = "where.exe",
                Arguments = ["git"],
                Timeout = SeoroConstants.WhichTimeout
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
                            logger.LogDebug("git 경로를 통해 Git Bash 발견: {Path}", bashCandidate);
                            return bashCandidate;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "where.exe를 통해 git 찾기 실패");
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
            if (File.Exists(path))
            {
                logger.LogDebug("잘 알려진 경로에서 Git Bash 발견: {Path}", path);
                return path;
            }

        logger.LogDebug("Git Bash를 찾을 수 없음, cmd.exe로 대체할 예정");
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
            if (File.Exists(path))
                return path;

        // Try via where.exe
        try
        {
            var result = await processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = "where.exe",
                Arguments = ["pwsh"],
                Timeout = SeoroConstants.WhichTimeout
            });

            var firstLine = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (result.Success && !string.IsNullOrWhiteSpace(firstLine))
                return firstLine;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "where.exe를 통한 PowerShell 감지 실패");
        }

        // Fallback to Windows PowerShell 5.1
        var winPwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(winPwsh))
            return winPwsh;

        return null;
    }
}