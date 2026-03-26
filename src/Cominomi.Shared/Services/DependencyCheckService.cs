using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class DependencyCheckService(
    ILogger<DependencyCheckService> logger,
    IShellService shellService,
    IProcessRunner processRunner)
    : IDependencyCheckService
{
    public async Task<List<DependencyResult>> CheckAllAsync()
    {
        // Invalidate cached shell so re-check picks up newly installed tools
        shellService.InvalidateCache();

        var tasks = new[]
        {
            CheckToolAsync("git", "Git version control",
                "https://git-scm.com/downloads",
                "winget install Git.Git",
                "brew install git"),
            CheckClaudeAsync(),
            CheckToolAsync("gh", "GitHub CLI",
                "https://cli.github.com/",
                "winget install GitHub.cli",
                "brew install gh")
        };

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<DependencyResult> CheckClaudeAsync()
    {
        const string description = "Claude CLI";
        const string installUrl = "https://docs.anthropic.com/en/docs/claude-code/overview";
        const string winHint = "irm https://claude.ai/install.ps1 | iex";
        const string macHint = "curl -fsSL https://claude.ai/install.sh | bash";

        var path = await FindExecutableAsync("claude");

        if (path == null && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] candidates =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm", "bin", "claude"),
                "/usr/local/bin/claude",
                "/opt/homebrew/bin/claude"
            ];

            foreach (var candidate in candidates)
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
        }

        if (path == null)
            return new DependencyResult("claude", description, false, null, null, installUrl,
                winHint, macHint, ClaudeInstallMethods.Windows, ClaudeInstallMethods.Mac);

        var version = await GetVersionAsync(path);
        return new DependencyResult("claude", description, true, version, path, installUrl,
            winHint, macHint, ClaudeInstallMethods.Windows, ClaudeInstallMethods.Mac);
    }

    private async Task<DependencyResult> CheckToolAsync(
        string command, string description,
        string installUrl, string windowsHint, string macHint,
        bool isRequired = true)
    {
        var path = await FindExecutableAsync(command);
        if (path == null)
            return new DependencyResult(command, description, false, null, null, installUrl,
                windowsHint, macHint, WindowsMethods: [], MacMethods: [], isRequired);

        var version = await GetVersionAsync(path);
        return new DependencyResult(command, description, true, version, path, installUrl,
            windowsHint, macHint, WindowsMethods: [], MacMethods: [], isRequired);
    }

    private async Task<string?> FindExecutableAsync(string command)
    {
        try
        {
            return await shellService.WhichAsync(command);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to find executable: {Command}", command);
        }

        return null;
    }

    private async Task<string?> GetVersionAsync(string executablePath)
    {
        try
        {
            var result = await processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = executablePath,
                Arguments = ["--version"],
                Timeout = TimeSpan.FromSeconds(5)
            });
            var firstLine = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return firstLine?.Trim();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to get version for: {Path}", executablePath);
            return null;
        }
    }
}
