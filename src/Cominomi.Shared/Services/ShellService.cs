using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ShellService : IShellService
{
    private readonly ILogger<ShellService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ShellInfo? _cached;

    public ShellService(ILogger<ShellService> logger)
    {
        _logger = logger;
    }

    public async Task<ShellInfo> GetShellAsync()
    {
        if (_cached != null) return _cached;

        await _lock.WaitAsync();
        try
        {
            if (_cached != null) return _cached;
            _cached = await ResolveShellAsync();
            _logger.LogInformation("Resolved shell: {Type} at {Path}", _cached.Type, _cached.FileName);
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> WhichAsync(string executableName)
    {
        var shell = await GetShellAsync();

        try
        {
            Process proc;

            if (shell.Type == ShellType.Cmd)
            {
                proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where.exe",
                        Arguments = executableName,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
            }
            else
            {
                proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = shell.FileName,
                        Arguments = $"-c \"which {executableName}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
            }

            proc.Start();
            var output = (await proc.StandardOutput.ReadLineAsync())?.Trim();

            using var cts = new CancellationTokenSource(3000);
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { proc.Kill(); } catch { } }

            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                return output;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WhichAsync failed for: {Name}", executableName);
        }

        return null;
    }

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
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = "git",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var gitPath = (await proc.StandardOutput.ReadLineAsync())?.Trim();

            using var cts = new CancellationTokenSource(3000);
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { proc.Kill(); } catch { } }

            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(gitPath))
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
