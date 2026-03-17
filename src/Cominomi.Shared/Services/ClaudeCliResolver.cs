using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ClaudeCliResolver
{
    private readonly IShellService _shellService;
    private readonly ILogger _logger;

    private (string fileName, string argPrefix)? _resolvedCommand;
    private string? _resolvedCommandPath;
    private readonly SemaphoreSlim _resolveLock = new(1, 1);

    public ClaudeCliResolver(IShellService shellService, ILogger logger)
    {
        _shellService = shellService;
        _logger = logger;
    }

    public async Task<(string fileName, string argPrefix)> ResolveAsync(string? configuredPath)
    {
        await _resolveLock.WaitAsync();
        try
        {
            if (_resolvedCommand.HasValue && _resolvedCommandPath == configuredPath)
                return _resolvedCommand.Value;

            var result = await ResolveClaudeCommandAsync(configuredPath);
            _resolvedCommand = result;
            _resolvedCommandPath = configuredPath;
            return result;
        }
        finally
        {
            _resolveLock.Release();
        }
    }

    private async Task<(string fileName, string argPrefix)> ResolveClaudeCommandAsync(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && configuredPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                return ("cmd.exe", $"/c \"{configuredPath}\" ");
            }
            return (configuredPath, "");
        }

        var resolved = await _shellService.WhichAsync("claude");
        if (resolved != null)
        {
            // .cmd wrappers (npm-installed on Windows) need cmd.exe to execute reliably
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                return ("cmd.exe", $"/c \"{resolved}\" ");
            }
            return (resolved, "");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] candidates =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm", "bin", "claude"),
                "/usr/local/bin/claude",
                "/opt/homebrew/bin/claude"
            ];

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return (candidate, "");
            }
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("claude.exe", "")
            : ("claude", "");
    }

    public async Task<string?> RunSimpleCommandAsync(string fileName, string arguments)
    {
        _logger.LogDebug("Executing: {FileName} {Arguments}", fileName, arguments);
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Environment = { ["NO_COLOR"] = "1" }
                }
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            proc.Dispose();
            return output;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run simple command: {FileName} {Args}", fileName, arguments);
            return null;
        }
    }
}
