using System.Diagnostics;
using System.Text;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class GitService : IGitService
{
    private readonly ILogger<GitService> _logger;

    public GitService(ILogger<GitService> logger)
    {
        _logger = logger;
    }

    public async Task<GitResult> CloneAsync(string url, string targetDir, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var parentDir = Path.GetDirectoryName(targetDir);
        if (parentDir != null)
            Directory.CreateDirectory(parentDir);

        _logger.LogDebug("git clone --progress {Url} -> {TargetDir}", url, targetDir);
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
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to kill git clone process"); }
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
            try { Directory.Delete(worktreePath, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up worktree directory: {Path}", worktreePath); }
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

    private async Task<GitResult> RunGitAsync(string arguments, string workingDir, CancellationToken ct = default)
    {
        _logger.LogDebug("git {Arguments}", arguments);
        var process = CreateGitProcess(arguments, workingDir);
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        _logger.LogDebug("git exited with code {ExitCode}", process.ExitCode);
        var result = new GitResult(process.ExitCode == 0, stdout.Trim(), stderr.Trim());
        process.Dispose();
        return result;
    }

    public async Task<GitResult> RenameBranchAsync(string workingDir, string oldName, string newName, CancellationToken ct = default)
    {
        return await RunGitAsync($"branch -m \"{oldName}\" \"{newName}\"", workingDir, ct);
    }

    public async Task<GitResult> DeleteBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
    {
        return await RunGitAsync($"branch -D \"{branchName}\"", repoDir, ct);
    }

    public async Task<bool> IsBranchMergedAsync(string repoDir, string branchName, string baseBranch, CancellationToken ct = default)
    {
        var result = await RunGitAsync($"merge-base --is-ancestor \"{branchName}\" \"{baseBranch}\"", repoDir, ct);
        return result.Success;
    }

    public async Task<GitResult> PushBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
    {
        return await RunGitAsync($"push -u origin \"{branchName}\"", repoDir, ct);
    }

    public async Task<GitResult> PushForceBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
    {
        return await RunGitAsync($"push --force-with-lease origin \"{branchName}\"", repoDir, ct);
    }

    public async Task<GitResult> FetchAsync(string repoDir, CancellationToken ct = default)
    {
        return await RunGitAsync("fetch origin", repoDir, ct);
    }

    public async Task<string> GetNameStatusAsync(string workingDir, string baseBranch, CancellationToken ct = default)
    {
        // Use baseBranch (not baseBranch...HEAD) to include uncommitted working tree changes
        var result = await RunGitAsync($"diff --name-status {baseBranch}", workingDir, ct);
        return result.Success ? result.Output : "";
    }

    public async Task<string> GetUnifiedDiffAsync(string workingDir, string baseBranch, CancellationToken ct = default)
    {
        // Use baseBranch (not baseBranch...HEAD) to include uncommitted working tree changes
        var result = await RunGitAsync($"diff {baseBranch}", workingDir, ct);
        return result.Success ? result.Output : "";
    }

    public async Task<GitResult> GetCommitLogAsync(string repoDir, string baseBranch, CancellationToken ct = default)
    {
        return await RunGitAsync($"log {baseBranch}..HEAD --oneline", repoDir, ct);
    }

    public async Task<List<string>> ListTrackedFilesAsync(string workingDir, CancellationToken ct = default)
    {
        var result = await RunGitAsync("ls-files", workingDir, ct);
        if (!result.Success) return new List<string>();
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task<string> ReadFileAsync(string workingDir, string relativePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(workingDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return await File.ReadAllTextAsync(fullPath, ct);
    }

    public static DiffSummary ParseDiff(string nameStatus, string rawDiff)
    {
        var summary = new DiffSummary();
        if (string.IsNullOrWhiteSpace(nameStatus))
            return summary;

        // Parse name-status lines: "M\tfile.cs", "A\tnewfile.cs", etc.
        var fileMap = new Dictionary<string, FileDiff>();
        foreach (var line in nameStatus.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length < 2) continue;

            var statusChar = parts[0].Trim();
            var filePath = parts[1].Trim();
            var changeType = statusChar switch
            {
                "A" => FileChangeType.Added,
                "D" => FileChangeType.Deleted,
                _ when statusChar.StartsWith("R") => FileChangeType.Renamed,
                _ => FileChangeType.Modified
            };

            // For renames, the path may contain "old\tnew"
            if (changeType == FileChangeType.Renamed)
            {
                var renameParts = filePath.Split('\t', 2);
                filePath = renameParts.Length > 1 ? renameParts[1] : filePath;
            }

            var fileDiff = new FileDiff { FilePath = filePath, ChangeType = changeType };
            fileMap[filePath] = fileDiff;
            summary.Files.Add(fileDiff);
        }

        // Parse unified diff and assign to files
        if (!string.IsNullOrWhiteSpace(rawDiff))
        {
            // Split by "diff --git" marker
            var chunks = rawDiff.Split("diff --git ", StringSplitOptions.RemoveEmptyEntries);
            foreach (var chunk in chunks)
            {
                // First line: "a/path b/path"
                var firstNewline = chunk.IndexOf('\n');
                if (firstNewline < 0) continue;

                var header = chunk[..firstNewline];
                var bIndex = header.LastIndexOf(" b/");
                if (bIndex < 0) continue;

                var filePath = header[(bIndex + 3)..].Trim();
                var diffContent = chunk[(firstNewline + 1)..];

                // Count additions and deletions
                int additions = 0, deletions = 0;
                foreach (var line in diffContent.Split('\n'))
                {
                    if (line.StartsWith('+') && !line.StartsWith("+++"))
                        additions++;
                    else if (line.StartsWith('-') && !line.StartsWith("---"))
                        deletions++;
                }

                if (fileMap.TryGetValue(filePath, out var fileDiff))
                {
                    fileDiff.UnifiedDiff = diffContent;
                    fileDiff.Additions = additions;
                    fileDiff.Deletions = deletions;
                }
            }
        }

        return summary;
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
