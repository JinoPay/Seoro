using System.Runtime.InteropServices;
using Cominomi.Shared;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ClaudeCliResolver
{
    private readonly IShellService _shellService;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger _logger;

    private (string fileName, string argPrefix)? _resolvedCommand;
    private string? _resolvedCommandPath;
    private readonly SemaphoreSlim _resolveLock = new(1, 1);

    public ClaudeCliResolver(IShellService shellService, IProcessRunner processRunner, ILogger logger)
    {
        _shellService = shellService;
        _processRunner = processRunner;
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
            // arguments may contain a baseArgs prefix (e.g., '/c "claude.exe" ') followed by the flag.
            // Split on whitespace while preserving quoted segments.
            var args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var result = await _processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = fileName,
                Arguments = args,
                EnvironmentVariables = CominomiConstants.Env.NoColorEnv,
                Timeout = TimeSpan.FromSeconds(10)
            });
            return result.Stdout;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run simple command: {FileName} {Args}", fileName, arguments);
            return null;
        }
    }
}
