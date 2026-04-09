using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Claude;

public class ClaudeCliResolver(IShellService shellService, IProcessRunner processRunner, ILogger logger)
{
    private readonly SemaphoreSlim _resolveLock = new(1, 1);

    private (string fileName, string argPrefix)? _resolvedCommand;
    private string? _resolvedCommandPath;

    /// <summary>
    ///     Returns the resolved Claude CLI command only if actually found on disk.
    ///     Returns null when not found — no fallback guess.
    /// </summary>
    public async Task<(string fileName, string argPrefix)?> DetectAsync(string? configuredPath)
    {
        return await FindClaudeCommandAsync(configuredPath);
    }

    /// <summary>
    ///     Returns a command to execute Claude CLI, falling back to a bare name if not found.
    ///     Use this when you intend to *run* Claude (best-effort).
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
                             ? ("cmd.exe", "/c claude ")
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

    public async Task<string?> RunSimpleCommandAsync(string fileName, string arguments)
    {
        logger.LogDebug("Executing: {FileName} {Arguments}", fileName, arguments);
        try
        {
            // arguments may contain a baseArgs prefix (e.g., '/c "claude.exe" ') followed by the flag.
            // Split on whitespace while preserving quoted segments.
            var args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var loginPath = await shellService.GetLoginShellPathAsync();
            var envVars = new Dictionary<string, string>(SeoroConstants.Env.NoColorEnv);
            if (loginPath != null)
                envVars["PATH"] = loginPath;

            var result = await processRunner.RunAsync(new ProcessRunOptions
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
            logger.LogWarning(ex, "간단한 명령 실행 실패: {FileName} {Args}", fileName, arguments);
            return null;
        }
    }

    /// <summary>
    ///     Given a resolved path on Windows, return the correct (fileName, argPrefix) tuple.
    ///     Handles .exe (direct), .cmd/.bat (via cmd.exe /c), and bare scripts
    ///     (probe for .cmd sibling, else wrap with cmd.exe /c).
    /// </summary>
    private static (string fileName, string argPrefix) ResolveWindowsCommand(string resolvedPath)
    {
        var ext = Path.GetExtension(resolvedPath);

        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return (resolvedPath, "");

        if (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            return ("cmd.exe", $"/c \"{resolvedPath}\" ");

        // Bare script (e.g., npm's extensionless 'claude' wrapper) — probe for .cmd sibling
        var cmdSibling = resolvedPath + ".cmd";
        if (File.Exists(cmdSibling))
            return ("cmd.exe", $"/c \"{cmdSibling}\" ");

        return ("cmd.exe", $"/c \"{resolvedPath}\" ");
    }

    private async Task<(string fileName, string argPrefix)?> FindClaudeCommandAsync(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return ResolveWindowsCommand(configuredPath);
            return (configuredPath, "");
        }

        var resolved = await shellService.WhichAsync("claude");
        if (resolved != null)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return ResolveWindowsCommand(resolved);
            return (resolved, "");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] windowsCandidates =
            [
                Path.Combine(appData, "npm", "claude.cmd")
            ];

            foreach (var candidate in windowsCandidates)
                if (File.Exists(candidate))
                    return ResolveWindowsCommand(candidate);
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates =
            [
                Path.Combine(home, ".local", "bin", "claude"), // Anthropic installer, pip
                Path.Combine(home, ".local", "share", "mise", "shims", "claude"), // mise
                Path.Combine(home, ".volta", "bin", "claude"), // volta
                "/opt/homebrew/bin/claude", // Apple Silicon Homebrew
                "/usr/local/bin/claude", // Intel Homebrew
                Path.Combine(home, ".npm", "bin", "claude") // npm global
            ];

            foreach (var candidate in candidates)
                if (File.Exists(candidate))
                    return (candidate, "");
        }

        return null;
    }
}