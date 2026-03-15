using System.Diagnostics;
using System.Text;

namespace Cominomi.Shared.Services;

public class GitService : IGitService
{
    public async Task<GitResult> CloneAsync(string url, string targetDir, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var parentDir = Path.GetDirectoryName(targetDir);
        if (parentDir != null)
            Directory.CreateDirectory(parentDir);

        var process = CreateGitProcess($"clone --progress \"{url}\" \"{targetDir}\"", parentDir ?? ".");
        process.Start();

        var stdoutBuilder = new StringBuilder();
        var stdoutTask = Task.Run(async () =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync(ct);
                if (line != null) stdoutBuilder.AppendLine(line);
            }
        }, ct);

        // Git clone writes progress to stderr
        var stderrBuilder = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            var buffer = new char[256];
            while (!process.StandardError.EndOfStream)
            {
                int read;
                try { read = await process.StandardError.ReadAsync(buffer, ct); }
                catch (OperationCanceledException) { break; }

                if (read > 0)
                {
                    var text = new string(buffer, 0, read);
                    stderrBuilder.Append(text);

                    // Extract progress lines (end with \r or \n)
                    var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            progress?.Report(trimmed);
                    }
                }
            }
        }, ct);

        try
        {
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            throw;
        }

        var result = new GitResult(
            process.ExitCode == 0,
            stdoutBuilder.ToString().Trim(),
            stderrBuilder.ToString().Trim());
        process.Dispose();
        return result;
    }

    public async Task<GitResult> AddWorktreeAsync(string repoDir, string worktreePath, string branchName, string baseBranch, CancellationToken ct = default)
    {
        var parentDir = Path.GetDirectoryName(worktreePath);
        if (parentDir != null)
            Directory.CreateDirectory(parentDir);

        // Check if branch already exists
        if (await BranchExistsAsync(repoDir, branchName))
        {
            return await RunGitAsync($"worktree add \"{worktreePath}\" \"{branchName}\"", repoDir, ct);
        }

        return await RunGitAsync($"worktree add -b \"{branchName}\" \"{worktreePath}\" \"{baseBranch}\"", repoDir, ct);
    }

    public async Task<GitResult> RemoveWorktreeAsync(string repoDir, string worktreePath, CancellationToken ct = default)
    {
        var result = await RunGitAsync($"worktree remove \"{worktreePath}\" --force", repoDir, ct);

        // Clean up directory if it still exists
        if (Directory.Exists(worktreePath))
        {
            try { Directory.Delete(worktreePath, recursive: true); } catch { }
        }

        // Prune stale worktree entries
        await RunGitAsync("worktree prune", repoDir, ct);

        return result;
    }

    public async Task<string?> DetectDefaultBranchAsync(string repoDir)
    {
        // Try symbolic-ref first
        var result = await RunGitAsync("symbolic-ref refs/remotes/origin/HEAD", repoDir);
        if (result.Success)
        {
            var refPath = result.Output.Trim();
            var branch = refPath.Replace("refs/remotes/origin/", "");
            if (!string.IsNullOrEmpty(branch))
                return branch;
        }

        // Fallback: check if main or master exists
        if (await BranchExistsAsync(repoDir, "main"))
            return "main";
        if (await BranchExistsAsync(repoDir, "master"))
            return "master";

        // Last resort: get current branch
        return await GetCurrentBranchAsync(repoDir);
    }

    public async Task<bool> IsGitRepoAsync(string path)
    {
        if (!Directory.Exists(path))
            return false;

        var result = await RunGitAsync("rev-parse --is-inside-work-tree", path);
        return result.Success && result.Output.Trim() == "true";
    }

    public async Task<string?> GetCurrentBranchAsync(string repoDir)
    {
        var result = await RunGitAsync("rev-parse --abbrev-ref HEAD", repoDir);
        return result.Success ? result.Output.Trim() : null;
    }

    public async Task<List<string>> ListBranchesAsync(string repoDir)
    {
        var result = await RunGitAsync("branch --format=%(refname:short)", repoDir);
        if (!result.Success)
            return [];

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim())
            .Where(b => !string.IsNullOrEmpty(b))
            .ToList();
    }

    public async Task<bool> BranchExistsAsync(string repoDir, string branchName)
    {
        // Check local branches
        var result = await RunGitAsync($"show-ref --verify --quiet refs/heads/{branchName}", repoDir);
        if (result.Success) return true;

        // Check remote branches
        result = await RunGitAsync($"show-ref --verify --quiet refs/remotes/origin/{branchName}", repoDir);
        return result.Success;
    }

    private static async Task<GitResult> RunGitAsync(string arguments, string workingDir, CancellationToken ct = default)
    {
        var process = CreateGitProcess(arguments, workingDir);
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var result = new GitResult(process.ExitCode == 0, stdout.Trim(), stderr.Trim());
        process.Dispose();
        return result;
    }

    private static Process CreateGitProcess(string arguments, string workingDir)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Environment =
                {
                    ["GIT_TERMINAL_PROMPT"] = "0",
                    ["NO_COLOR"] = "1"
                }
            }
        };
    }
}
