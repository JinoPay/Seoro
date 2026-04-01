using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Cominomi.Shared;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Services;

public class GitService : IGitService
{
    private readonly ILogger<GitService> _logger;
    private readonly IProcessRunner _processRunner;
    private readonly IOptionsMonitor<AppSettings> _appSettings;
    private readonly IShellService _shellService;

    // Resolved git path cache
    private string? _resolvedGitPath;
    private DateTime _gitPathResolvedAt;
    private readonly SemaphoreSlim _gitPathLock = new(1, 1);
    private static readonly TimeSpan GitPathCacheTtl = TimeSpan.FromMinutes(10);

    // Cache: DetectDefaultBranch rarely changes → 5 min TTL
    private readonly ConcurrentDictionary<string, (string? Branch, DateTime LoadedAt)> _defaultBranchCache = new();
    private static readonly TimeSpan DefaultBranchCacheTtl = TimeSpan.FromMinutes(5);

    // Cache: ListBranches changes more often → 30 sec TTL
    private readonly ConcurrentDictionary<string, (List<string> Branches, DateTime LoadedAt)> _branchListCache = new();
    private readonly ConcurrentDictionary<string, (List<BranchGroup> Groups, DateTime LoadedAt)> _branchGroupCache = new();
    private static readonly TimeSpan BranchListCacheTtl = TimeSpan.FromSeconds(30);

    public GitService(ILogger<GitService> logger, IProcessRunner processRunner, IOptionsMonitor<AppSettings> appSettings, IShellService shellService)
    {
        _logger = logger;
        _processRunner = processRunner;
        _appSettings = appSettings;
        _shellService = shellService;
    }

    private async Task<string> ResolveGitPathAsync()
    {
        // Use configured path if set
        var configuredPath = _appSettings.CurrentValue.GitPath;
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        // Check cache
        if (_resolvedGitPath != null && DateTime.UtcNow - _gitPathResolvedAt < GitPathCacheTtl)
            return _resolvedGitPath;

        await _gitPathLock.WaitAsync();
        try
        {
            if (_resolvedGitPath != null && DateTime.UtcNow - _gitPathResolvedAt < GitPathCacheTtl)
                return _resolvedGitPath;

            var resolved = await _shellService.WhichAsync("git");
            _resolvedGitPath = resolved ?? "git";
            _gitPathResolvedAt = DateTime.UtcNow;

            if (resolved != null)
                _logger.LogDebug("Resolved git path: {Path}", resolved);

            return _resolvedGitPath;
        }
        finally
        {
            _gitPathLock.Release();
        }
    }

    public async Task<GitResult> CloneAsync(string url, string targetDir, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var parentDir = Path.GetDirectoryName(targetDir);
        if (parentDir != null)
            Directory.CreateDirectory(parentDir);

        var gitPath = await ResolveGitPathAsync();
        _logger.LogDebug("git clone --progress {Url} -> {TargetDir}", url, targetDir);
        var process = CreateStreamingGitProcess(gitPath, ["clone", "--progress", url, targetDir], parentDir ?? ".");
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
        GitResult result;
        if (await BranchExistsAsync(repoDir, branchName))
        {
            result = await RunGitAsync(repoDir, ct, "worktree", "add", worktreePath, branchName);
        }
        else
        {
            result = await RunGitAsync(repoDir, ct, "worktree", "add", "-b", branchName, worktreePath, baseBranch);
        }

        if (result.Success)
            _logger.LogInformation("Worktree added at {WorktreePath} on branch {BranchName}", worktreePath, branchName);
        else
            _logger.LogWarning("Failed to add worktree at {WorktreePath}: {Error}", worktreePath, result.Error);

        return result;
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

        if (result.Success)
            _logger.LogInformation("Worktree removed: {WorktreePath}", worktreePath);

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
        _logger.LogDebug("Default branch for {RepoDir}: {Branch}", repoDir, current);
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
        var gitPath = await ResolveGitPathAsync();
        _logger.LogDebug("git {Arguments}", string.Join(" ", args));
        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = gitPath,
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
        var result = await RunGitAsync(workingDir, ct, "branch", "-m", oldName, newName);
        if (result.Success)
            _logger.LogInformation("Branch renamed: {OldName} -> {NewName}", oldName, newName);
        else
            _logger.LogWarning("Branch rename failed: {OldName} -> {NewName}: {Error}", oldName, newName, result.Error);
        return result;
    }

    public async Task<GitResult> DeleteBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "branch", "-D", branchName);
        if (result.Success)
            _logger.LogInformation("Branch deleted: {BranchName}", branchName);
        else
            _logger.LogWarning("Branch delete failed: {BranchName}: {Error}", branchName, result.Error);
        return result;
    }

    public async Task<GitResult> FetchAsync(string repoDir, CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "fetch", "origin");
        if (result.Success)
        {
            InvalidateBranchCaches(repoDir);
            _logger.LogDebug("Fetch completed for {RepoDir}", repoDir);
        }
        return result;
    }

    public async Task<GitResult> FetchAllAsync(string repoDir, CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "fetch", "--all", "--prune");
        if (result.Success)
        {
            InvalidateBranchCaches(repoDir);
            _logger.LogDebug("Fetch all completed for {RepoDir}", repoDir);
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

    public async Task<string[]> ReadFileLinesAsync(string workingDir, string relativePath, int startLine, int endLine, CancellationToken ct = default)
    {
        var content = await ReadFileAsync(workingDir, relativePath, ct);
        var allLines = content.Split('\n');
        var from = Math.Max(0, startLine - 1); // 1-based to 0-based
        var to = Math.Min(allLines.Length, endLine - 1);
        if (from >= to) return [];
        return allLines[from..to];
    }

    public async Task<string[]> ReadBaseFileLinesAsync(string workingDir, string baseBranch, string relativePath, int startLine, int endLine, CancellationToken ct = default)
    {
        var gitPath = relativePath.Replace('\\', '/');
        var result = await RunGitAsync(workingDir, ct, "show", $"{baseBranch}:{gitPath}");
        if (!result.Success) return [];
        var allLines = result.Output.Split('\n');
        var from = Math.Max(0, startLine - 1);
        var to = Math.Min(allLines.Length, endLine - 1);
        if (from >= to) return [];
        return allLines[from..to];
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

    public async Task<DiffSummary> GetDiffSummaryAsync(string workingDir, string baseBranch, CancellationToken ct = default)
    {
        // If baseBranch (e.g. "HEAD") is not a valid ref (no commits yet), fall back to empty tree
        var verifyResult = await RunGitAsync(workingDir, ct, "rev-parse", "--verify", "--quiet", baseBranch);
        if (!verifyResult.Success)
        {
            // 4b825dc... is git's well-known empty tree hash
            baseBranch = "4b825dc642cb6eb9a060e54bf899d69f82e20891";
        }

        // Fetch name-status, untracked files, and stream diff in parallel
        var nameStatusTask = GetNameStatusAsync(workingDir, baseBranch, ct);
        var untrackedTask = GetUntrackedFilesAsync(workingDir, ct);

        var gitPath = await ResolveGitPathAsync();
        _logger.LogDebug("git diff {BaseBranch} (streaming)", baseBranch);
        var streamingTask = _processRunner.RunStreamingAsync(new ProcessRunOptions
        {
            FileName = gitPath,
            Arguments = ["diff", baseBranch],
            WorkingDirectory = workingDir,
            EnvironmentVariables = CominomiConstants.Env.GitEnv,
        }, ct);

        var nameStatus = await nameStatusTask;
        var untrackedFiles = await untrackedTask;
        var streaming = await streamingTask;

        // Parse name-status into file map
        var summary = new DiffSummary();
        var fileMap = ParseNameStatusIntoFileMap(nameStatus, summary);

        // Stream unified diff and parse incrementally (never loads full diff into memory)
        await using (streaming)
        {
            string? currentFile = null;
            var currentDiff = new StringBuilder();
            int additions = 0, deletions = 0;
            bool inDiffBlock = false;

            while (await streaming.ReadLineAsync(ct) is { } line)
            {
                if (line.StartsWith("diff --git "))
                {
                    FlushFileDiff(fileMap, currentFile, currentDiff, additions, deletions);

                    currentFile = ExtractPathFromDiffHeader(line);
                    currentDiff.Clear();
                    additions = 0;
                    deletions = 0;
                    inDiffBlock = true;
                    continue;
                }

                if (!inDiffBlock) continue;

                // Fall back to +++ line for renames or ambiguous headers
                if (currentFile == null && line.StartsWith("+++ b/"))
                    currentFile = line[6..];

                currentDiff.AppendLine(line);

                if (line.StartsWith('+') && !line.StartsWith("+++"))
                    additions++;
                else if (line.StartsWith('-') && !line.StartsWith("---"))
                    deletions++;
            }

            // Flush last file
            FlushFileDiff(fileMap, currentFile, currentDiff, additions, deletions);

            var (exitCode, stderr) = await streaming.WaitForExitAsync(ct);
            if (exitCode != 0)
                _logger.LogWarning("git diff exited with {ExitCode}: {Stderr}", exitCode, stderr);
        }

        // Append untracked files as Added
        foreach (var relPath in untrackedFiles)
        {
            try
            {
                var fullPath = Path.Combine(workingDir, relPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath)) continue;

                var content = await File.ReadAllTextAsync(fullPath, ct);
                var lines = content.Split('\n');
                var addCount = lines.Length;

                // Build synthetic unified diff
                var diffBuilder = new StringBuilder();
                diffBuilder.AppendLine("--- /dev/null");
                diffBuilder.AppendLine($"+++ b/{relPath}");
                diffBuilder.AppendLine($"@@ -0,0 +1,{addCount} @@");
                foreach (var line in lines)
                    diffBuilder.AppendLine("+" + line);

                summary.Files.Add(new FileDiff
                {
                    FilePath = relPath,
                    ChangeType = FileChangeType.Untracked,
                    UnifiedDiff = diffBuilder.ToString(),
                    Additions = addCount,
                    Deletions = 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read untracked file: {Path}", relPath);
            }
        }

        return summary;
    }

    private async Task<List<string>> GetUntrackedFilesAsync(string workingDir, CancellationToken ct = default)
    {
        var result = await RunGitBoundedAsync(workingDir, ct, "ls-files", "--others", "--exclude-standard");
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return [];

        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();
    }

    private static Dictionary<string, FileDiff> ParseNameStatusIntoFileMap(string nameStatus, DiffSummary summary)
    {
        var fileMap = new Dictionary<string, FileDiff>();
        if (string.IsNullOrWhiteSpace(nameStatus))
            return fileMap;

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

            if (changeType == FileChangeType.Renamed)
            {
                var renameParts = filePath.Split('\t', 2);
                filePath = renameParts.Length > 1 ? renameParts[1] : filePath;
            }

            var fileDiff = new FileDiff { FilePath = filePath, ChangeType = changeType };
            fileMap[filePath] = fileDiff;
            summary.Files.Add(fileDiff);
        }

        return fileMap;
    }

    /// <summary>
    /// Extracts the file path from a diff header using symmetric path structure.
    /// Handles paths containing " b/" correctly, unlike LastIndexOf(" b/").
    /// Accepts "diff --git a/&lt;path&gt; b/&lt;path&gt;" or "a/&lt;path&gt; b/&lt;path&gt;" formats.
    /// Returns null for renames (asymmetric paths) — caller should fall back to +++ line.
    /// </summary>
    internal static string? ExtractPathFromDiffHeader(string header)
    {
        const string fullPrefix = "diff --git a/";
        const string shortPrefix = "a/";

        string rest;
        if (header.StartsWith(fullPrefix))
            rest = header[fullPrefix.Length..];
        else if (header.StartsWith(shortPrefix))
            rest = header[shortPrefix.Length..];
        else
            return null;

        // For non-renames: rest = "<path> b/<path>", length = 2 * pathLen + 3
        if (rest.Length < 3 || (rest.Length - 3) % 2 != 0)
            return null;

        var pathLen = (rest.Length - 3) / 2;
        var candidate = rest[..pathLen];

        return rest.EndsWith(" b/" + candidate) ? candidate : null;
    }

    private static void FlushFileDiff(Dictionary<string, FileDiff> fileMap, string? filePath, StringBuilder diffContent, int additions, int deletions)
    {
        if (filePath == null) return;
        if (!fileMap.TryGetValue(filePath, out var fileDiff)) return;

        fileDiff.UnifiedDiff = diffContent.ToString();
        fileDiff.Additions = additions;
        fileDiff.Deletions = deletions;
    }

    private void InvalidateBranchCaches(string repoDir)
    {
        var key = Path.GetFullPath(repoDir);
        _defaultBranchCache.TryRemove(key, out _);
        _branchListCache.TryRemove(key, out _);
        _branchGroupCache.TryRemove(key, out _);
    }

    public async Task<GitResult> StageAllAsync(string workingDir, CancellationToken ct = default)
    {
        var result = await RunGitAsync(workingDir, ct, "add", "-A");
        if (result.Success)
            _logger.LogDebug("Staged all changes in {WorkingDir}", workingDir);
        return result;
    }

    public async Task<GitResult> CommitAsync(string workingDir, string message, CancellationToken ct = default)
    {
        var result = await RunGitAsync(workingDir, ct, "commit", "-m", message);
        if (result.Success)
            _logger.LogInformation("Committed in {WorkingDir}: {Message}", workingDir, message.Length > 80 ? message[..80] + "..." : message);
        return result;
    }

    public async Task<(int Ahead, int Behind)> GetAheadBehindAsync(string workingDir, CancellationToken ct = default)
    {
        var result = await RunGitAsync(workingDir, ct, "rev-list", "--count", "--left-right", "@{upstream}...HEAD");
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return (0, 0);

        var parts = result.Output.Trim().Split('\t');
        if (parts.Length != 2) return (0, 0);

        int.TryParse(parts[0], out var behind);
        int.TryParse(parts[1], out var ahead);
        return (ahead, behind);
    }

    public async Task<List<string>> GetStatusPorcelainAsync(string workingDir, CancellationToken ct = default)
    {
        var result = await RunGitBoundedAsync(workingDir, ct, "status", "--porcelain");
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return [];

        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 3)
            .ToList();
    }

    public async Task<List<string>> GetChangedFilesAsync(string workingDir, string baseBranch, CancellationToken ct = default)
    {
        // tracked changes vs base branch (includes uncommitted)
        var diffResult = await RunGitBoundedAsync(workingDir, ct, "diff", "--name-only", baseBranch);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (diffResult.Success && !string.IsNullOrWhiteSpace(diffResult.Output))
        {
            foreach (var line in diffResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.TrimEnd('\r').Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    files.Add(trimmed);
            }
        }

        // untracked files
        var untrackedResult = await RunGitBoundedAsync(workingDir, ct, "ls-files", "--others", "--exclude-standard");
        if (untrackedResult.Success && !string.IsNullOrWhiteSpace(untrackedResult.Output))
        {
            foreach (var line in untrackedResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.TrimEnd('\r').Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    files.Add(trimmed);
            }
        }

        return files.ToList();
    }

    public async Task<GitResult> CheckoutFilesAsync(string workingDir, IEnumerable<string> relativePaths, CancellationToken ct = default)
    {
        var paths = relativePaths.ToList();
        if (paths.Count == 0)
            return new GitResult(true, "", "");

        var args = new List<string> { "checkout", "--" };
        args.AddRange(paths);
        return await RunGitAsync(workingDir, ct, args.ToArray());
    }

    /// <summary>
    /// Creates a git process for CloneAsync which needs character-by-character stderr streaming.
    /// All other git commands use IProcessRunner via RunGitAsync.
    /// </summary>
    private static Process CreateStreamingGitProcess(string gitPath, string[] args, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = gitPath,
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
