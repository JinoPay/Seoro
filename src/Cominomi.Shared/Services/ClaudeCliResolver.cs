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

    /// <summary>
    /// Returns a command to execute Claude CLI, falling back to a bare name if not found.
    /// Use this when you intend to *run* Claude (best-effort).
    /// </summary>
    public async Task<(string fileName, string argPrefix)> ResolveAsync(string? configuredPath)
    {
        await _resolveLock.WaitAsync();
        try
        {
            if (_resolvedCommand.HasValue && _resolvedCommandPath == configuredPath)
                return _resolvedCommand.Value;

            var result = await FindClaudeCommandAsync(configuredPath)
                         ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                             ? ("claude.exe", "")
                             : ("claude", ""));
            _resolvedCommand = result;
            _resolvedCommandPath = configuredPath;
            return result;
        }
        finally
        {
            _resolveLock.Release();
        }
    }

    /// <summary>
    /// Returns the resolved Claude CLI command only if actually found on disk.
    /// Returns null when not found — no fallback guess.
    /// </summary>
    public async Task<(string fileName, string argPrefix)?> DetectAsync(string? configuredPath)
    {
        return await FindClaudeCommandAsync(configuredPath);
    }

    private async Task<(string fileName, string argPrefix)?> FindClaudeCommandAsync(string? configuredPath)
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
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates =
            [
                Path.Combine(home, ".local", "bin", "claude"),                        // Anthropic installer, pip
                Path.Combine(home, ".local", "share", "mise", "shims", "claude"),     // mise
                Path.Combine(home, ".volta", "bin", "claude"),                        // volta
                "/opt/homebrew/bin/claude",                                           // Apple Silicon Homebrew
                "/usr/local/bin/claude",                                              // Intel Homebrew
                Path.Combine(home, ".npm", "bin", "claude")                           // npm global
            ];

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return (candidate, "");
            }
        }

        return null;
    }

    public async Task<string?> RunSimpleCommandAsync(string fileName, string arguments)
    {
        _logger.LogDebug("Executing: {FileName} {Arguments}", fileName, arguments);
        try
        {
            // arguments may contain a baseArgs prefix (e.g., '/c "claude.exe" ') followed by the flag.
            // Split on whitespace while preserving quoted segments.
            var args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var loginPath = await _shellService.GetLoginShellPathAsync();
            var envVars = new Dictionary<string, string>(CominomiConstants.Env.NoColorEnv);
            if (loginPath != null)
                envVars["PATH"] = loginPath;

            var result = await _processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = fileName,
                Arguments = args,
                EnvironmentVariables = envVars,
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
