using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Cominomi.Shared.Services;

public class DependencyCheckService : IDependencyCheckService
{
    public async Task<List<DependencyResult>> CheckAllAsync()
    {
        var tasks = new[]
        {
            CheckToolAsync("git", "Git version control",
                "https://git-scm.com/downloads",
                "winget install Git.Git",
                "brew install git"),
            CheckToolAsync("gh", "GitHub CLI",
                "https://cli.github.com/",
                "winget install GitHub.cli",
                "brew install gh"),
            CheckClaudeAsync()
        };

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private static async Task<DependencyResult> CheckToolAsync(
        string command, string description,
        string installUrl, string windowsHint, string macHint)
    {
        var path = FindExecutable(command);
        if (path == null)
            return new DependencyResult(command, description, false, null, installUrl, windowsHint, macHint);

        var version = await GetVersionAsync(path);
        return new DependencyResult(command, description, true, version, installUrl, windowsHint, macHint);
    }

    private static async Task<DependencyResult> CheckClaudeAsync()
    {
        const string description = "Claude CLI";
        const string installUrl = "https://docs.anthropic.com/en/docs/claude-code/overview";
        const string installHint = "npm install -g @anthropic-ai/claude-code";

        var path = FindExecutable("claude");

        if (path == null && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
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
                {
                    path = candidate;
                    break;
                }
            }
        }

        if (path == null)
            return new DependencyResult("claude", description, false, null, installUrl, installHint, installHint);

        var version = await GetVersionAsync(path);
        return new DependencyResult("claude", description, true, version, installUrl, installHint, installHint);
    }

    private static string? FindExecutable(string command)
    {
        try
        {
            var whichCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where.exe" : "/usr/bin/which";
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = whichCmd,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadLine()?.Trim();
            proc.WaitForExit(3000);
            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                return output;
        }
        catch { }

        return null;
    }

    private static async Task<string?> GetVersionAsync(string executablePath)
    {
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadLineAsync();
            await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            return output?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
