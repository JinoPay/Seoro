using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Cominomi.Shared;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class GitService : IGitService
{
    private readonly ILogger<GitService> _logger;
    private readonly IProcessRunner _processRunner;

    // Cache: DetectDefaultBranch rarely changes → 5 min TTL
    private readonly ConcurrentDictionary<string, (string? Branch, DateTime LoadedAt)> _defaultBranchCache = new();
    private static readonly TimeSpan DefaultBranchCacheTtl = TimeSpan.FromMinutes(5);

    // Cache: ListBranches changes more often → 30 sec TTL
    private readonly ConcurrentDictionary<string, (List<string> Branches, DateTime LoadedAt)> _branchListCache = new();
    private readonly ConcurrentDictionary<string, (List<BranchGroup> Groups, DateTime LoadedAt)> _branchGroupCache = new();
    private static readonly TimeSpan BranchListCacheTtl = TimeSpan.FromSeconds(30);

    public GitService(ILogger<GitService> logger, IProcessRunner processRunner)
    {
        _logger = logger;
        _processRunner = processRunner;
    }

    public async Task<GitResult> CloneAsync(string url, string targetDir, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var parentDir = Path.GetDirectoryName(targetDir);
        if (parentDir != null)
            Directory.CreateDirectory(parentDir);

        _logger.LogDebug("git clone --progress {Url} -> {TargetDir}", url, targetDir);
        var process = CreateStreamingGitProcess(["clone", "--progress", url, targetDir], parentDir ?? ".");
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
            return await RunGitAsync(repoDir, ct, "worktree", "add", worktreePath, branchName);
        }

        return await RunGitAsync(repoDir, ct, "worktree", "add", "-b", branchName, worktreePath, baseBranch);
    }

    public async Task<GitResult> RemoveWorktreeAsync(string repoDir, string worktreePath, CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "worktree", "remove", worktreePath, "--force");

        // Clean up directory if it still exists
        if (Directory.Exists(worktreePath))
        {
            try { Directory.Delete(worktreePath, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up worktree directory: {Path}", worktreePath); }
        }

        // Prune stale worktree entries
        await RunGitAsync(repoDir, ct, "worktree", "prune");

        return result;
    }

    public async Task<string?> DetectDefaultBranchAsync(string repoDir)
    {
        var key = Path.GetFullPath(repoDir);
        if (_defaultBranchCache.TryGetValue(key, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < DefaultBranchCacheTtl)
        {
            return cached.Branch;
        }

        // Try symbolic-ref first
        var result = await RunGitAsync(repoDir, default, "symbolic-ref", "refs/remotes/origin/HEAD");
        if (result.Success)
        {
            var refPath = result.Output.Trim();
            var branch = refPath.Replace("refs/remotes/", "");
            if (!string.IsNullOrEmpty(branch))
            {
                _defaultBranchCache[key] = (branch, DateTime.UtcNow);
                return branch;
            }
        }

        // Fallback: check if main or master exists
        if (await BranchExistsAsync(repoDir, "main"))
        {
            _defaultBranchCache[key] = ("origin/main", DateTime.UtcNow);
            return "origin/main";
        }
        if (await BranchExistsAsync(repoDir, "master"))
        {
            _defaultBranchCache[key] = ("origin/master", DateTime.UtcNow);
            return "origin/master";
        }

        // Last resort: get current branch
        var current = await GetCurrentBranchAsync(repoDir);
        _defaultBranchCache[key] = (current, DateTime.UtcNow);
        return current;
    }

    public async Task<bool> IsGitRepoAsync(string path)
    {
        if (!Directory.Exists(path))
            return false;

        var result = await RunGitAsync(path, default, "rev-parse", "--is-inside-work-tree");
        return result.Success && result.Output.Trim() == "true";
    }

    public async Task<string?> GetCurrentBranchAsync(string repoDir)
    {
        var result = await RunGitAsync(repoDir, default, "rev-parse", "--abbrev-ref", "HEAD");
        return result.Success ? result.Output.Trim() : null;
    }

    public async Task<List<string>> ListBranchesAsync(string repoDir)
    {
        var key = Path.GetFullPath(repoDir);
        if (_branchListCache.TryGetValue(key, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < BranchListCacheTtl)
        {
            return cached.Branches;
        }

        var result = await RunGitAsync(repoDir, default, "branch", "--format=%(refname:short)");
        if (!result.Success)
            return [];

        var branches = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim())
            .Where(b => !string.IsNullOrEmpty(b))
            .ToList();

        _branchListCache[key] = (branches, DateTime.UtcNow);
        return branches;
    }

    public async Task<bool> BranchExistsAsync(string repoDir, string branchName)
    {
        // Check local branches
        var result = await RunGitAsync(repoDir, default, "show-ref", "--verify", "--quiet", $"refs/heads/{branchName}");
        if (result.Success) return true;

        // Check remote branches
        result = await RunGitAsync(repoDir, default, "show-ref", "--verify", "--quiet", $"refs/remotes/origin/{branchName}");
        return result.Success;
    }

    public async Task<GitResult> RunAsync(string arguments, string workingDir, CancellationToken ct = default)
    {
        var args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return await RunGitAsync(workingDir, ct, args);
    }

    /// <summary>
    /// Maximum stdout bytes for git commands that may produce large output (diff, ls-files, log).
    /// 1 MB — large enough for practical use, prevents unbounded memory growth.
    /// </summary>
    private const int LargeOutputMaxBytes = 1 * 1024 * 1024;

    private async Task<GitResult> RunGitAsync(string workingDir, CancellationToken ct, params string[] args)
    {
        return await RunGitCoreAsync(workingDir, maxOutputBytes: null, ct, args);
    }

    private async Task<GitResult> RunGitBoundedAsync(string workingDir, CancellationToken ct, params string[] args)
    {
        return await RunGitCoreAsync(workingDir, LargeOutputMaxBytes, ct, args);
    }

    private async Task<GitResult> RunGitCoreAsync(string workingDir, int? maxOutputBytes, CancellationToken ct, params string[] args)
    {
        _logger.LogDebug("git {Arguments}", string.Join(" ", args));
        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workingDir,
            EnvironmentVariables = CominomiConstants.Env.GitEnv,
            MaxOutputBytes = maxOutputBytes,
        }, ct);
        if (result.Truncated)
            _logger.LogWarning("git {Command} output truncated at {MaxBytes} bytes", args.FirstOrDefault(), maxOutputBytes);
        return new GitResult(result.Success, result.Stdout, result.Stderr);
    }

    public async Task<GitResult> RenameBranchAsync(string workingDir, string oldName, string newName, CancellationToken ct = default)
    {
        return await RunGitAsync(workingDir, ct, "branch", "-m", oldName, newName);
    }

    public async Task<GitResult> DeleteBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
    {
        return await RunGitAsync(repoDir, ct, "branch", "-D", branchName);
    }

    public async Task<bool> IsBranchMergedAsync(string repoDir, string branchName, string baseBranch, CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "merge-base", "--is-ancestor", branchName, baseBranch);
        return result.Success;
    }

    public async Task<GitResult> PushBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
    {
        return await RunGitAsync(repoDir, ct, "push", "-u", "origin", branchName);
    }

    public async Task<GitResult> PushForceBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
    {
        return await RunGitAsync(repoDir, ct, "push", "--force-with-lease", "origin", branchName);
    }

    public async Task<GitResult> FetchAsync(string repoDir, CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "fetch", "origin");
        if (result.Success) InvalidateBranchCaches(repoDir);
        return result;
    }

    public async Task<GitResult> FetchAllAsync(string repoDir, CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "fetch", "--all", "--prune");
        if (result.Success) InvalidateBranchCaches(repoDir);
        return result;
    }

    public async Task<GitResult> RebaseAsync(string workingDir, string baseBranch, CancellationToken ct = default)
    {
        var result = await RunGitAsync(workingDir, ct, "rebase", $"origin/{baseBranch}");
        if (!result.Success)
        {
            // Abort failed rebase to leave worktree clean
            await RunGitAsync(workingDir, ct, "rebase", "--abort");
        }
        return result;
    }

    public async Task<List<BranchGroup>> ListAllBranchesGroupedAsync(string repoDir)
    {
        var key = Path.GetFullPath(repoDir);
        if (_branchGroupCache.TryGetValue(key, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < BranchListCacheTtl)
        {
            return cached.Groups;
        }

        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Get remote branches
        var remoteResult = await RunGitAsync(repoDir, default, "branch", "-r", "--format=%(refname:short)");
        if (remoteResult.Success)
        {
            foreach (var line in remoteResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var branch = line.Trim();
                if (string.IsNullOrEmpty(branch) || branch.Contains("/HEAD")) continue;

                var slashIdx = branch.IndexOf('/');
                if (slashIdx <= 0) continue;

                var remoteName = branch[..slashIdx];
                if (!groups.ContainsKey(remoteName))
                    groups[remoteName] = [];
                groups[remoteName].Add(branch);
            }
        }

        // Get local branches
        var localResult = await RunGitAsync(repoDir, default, "branch", "--format=%(refname:short)");
        var localBranches = new List<string>();
        if (localResult.Success)
        {
            localBranches = localResult.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrEmpty(b))
                .ToList();
        }

        // Build ordered result: origin first, then other remotes alphabetically, then local
        var result = new List<BranchGroup>();

        if (groups.Remove("origin", out var originBranches))
            result.Add(new BranchGroup("origin", originBranches));

        foreach (var kv in groups.OrderBy(k => k.Key))
            result.Add(new BranchGroup(kv.Key, kv.Value));

        if (localBranches.Count > 0)
            result.Add(new BranchGroup("로컬", localBranches));

        _branchGroupCache[key] = (result, DateTime.UtcNow);
        return result;
    }

    public async Task<string> GetNameStatusAsync(string workingDir, string baseBranch, CancellationToken ct = default)
    {
        // Use baseBranch (not baseBranch...HEAD) to include uncommitted working tree changes
        var result = await RunGitBoundedAsync(workingDir, ct, "diff", "--name-status", baseBranch);
        return result.Success ? result.Output : "";
    }

    public async Task<string> GetUnifiedDiffAsync(string workingDir, string baseBranch, CancellationToken ct = default)
    {
        // Use baseBranch (not baseBranch...HEAD) to include uncommitted working tree changes
        var result = await RunGitBoundedAsync(workingDir, ct, "diff", baseBranch);
        return result.Success ? result.Output : "";
    }

    public async Task<GitResult> GetCommitLogAsync(string repoDir, string baseBranch, CancellationToken ct = default)
    {
        return await RunGitBoundedAsync(repoDir, ct, "log", $"{baseBranch}..HEAD", "--oneline");
    }

    public async Task<GitResult> GetFormattedCommitLogAsync(string repoDir, string baseBranch, int maxCount = 50, CancellationToken ct = default)
    {
        return await RunGitBoundedAsync(repoDir, ct, "log", $"{baseBranch}..HEAD", "--format=%H%x00%h%x00%an%x00%aI%x00%s", "-n", maxCount.ToString());
    }

    public async Task<List<string>> ListTrackedFilesAsync(string workingDir, CancellationToken ct = default)
    {
        var result = await RunGitBoundedAsync(workingDir, ct, "ls-files");
        if (!result.Success) return new List<string>();
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task<string> ReadFileAsync(string workingDir, string relativePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(workingDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return await File.ReadAllTextAsync(fullPath, ct);
    }

    public async Task<(int Additions, int Deletions)> GetDiffStatAsync(string workingDir, string baseBranch, CancellationToken ct = default)
    {
        var result = await RunGitAsync(workingDir, ct, "diff", "--shortstat", baseBranch);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return (0, 0);

        // Parse output like: " 3 files changed, 36 insertions(+), 16 deletions(-)"
        int additions = 0, deletions = 0;
        var parts = result.Output.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Contains("insertion"))
            {
                var numStr = trimmed.Split(' ')[0];
                int.TryParse(numStr, out additions);
            }
            else if (trimmed.Contains("deletion"))
            {
                var numStr = trimmed.Split(' ')[0];
                int.TryParse(numStr, out deletions);
            }
        }
        return (additions, deletions);
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

    private void InvalidateBranchCaches(string repoDir)
    {
        var key = Path.GetFullPath(repoDir);
        _defaultBranchCache.TryRemove(key, out _);
        _branchListCache.TryRemove(key, out _);
        _branchGroupCache.TryRemove(key, out _);
    }

    /// <summary>
    /// Creates a git process for CloneAsync which needs character-by-character stderr streaming.
    /// All other git commands use IProcessRunner via RunGitAsync.
    /// </summary>
    private static Process CreateStreamingGitProcess(string[] args, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        foreach (var (key, value) in CominomiConstants.Env.GitEnv)
            psi.Environment[key] = value;
        return new Process { StartInfo = psi };
    }
}
