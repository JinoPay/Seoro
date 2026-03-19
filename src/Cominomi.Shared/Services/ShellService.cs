using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ShellService : IShellService
{
    private readonly ILogger<ShellService> _logger;
    private readonly IProcessRunner _processRunner;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ShellInfo? _cached;
    private DateTime _cachedAt;

    public ShellService(ILogger<ShellService> logger, IProcessRunner processRunner)
    {
        _logger = logger;
        _processRunner = processRunner;
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

    public void InvalidateCache()
    {
        _cached = null;
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

                case ShellType.Bash:
                    // Windows Git Bash: convert Unix path to Windows path via cygpath
                    options = new ProcessRunOptions
                    {
                        FileName = shell.FileName,
                        Arguments = ["-c", $"cygpath -w \"$(which {executableName})\""],
                        Timeout = CominomiConstants.WhichTimeout
                    };
                    break;

                default: // ShellType.Sh — macOS/Linux
                    options = new ProcessRunOptions
                    {
                        FileName = shell.FileName,
                        Arguments = ["-c", $"which {executableName}"],
                        Timeout = CominomiConstants.WhichTimeout
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

        return null;
    }

    private static bool IsValidExecutableName(string name)
        => !string.IsNullOrEmpty(name) && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');

    private async Task<ShellInfo> ResolveShellAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new ShellInfo("/bin/sh", "-c ", ShellType.Sh);

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
}
