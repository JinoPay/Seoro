using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seoro.Shared.Services.Git;

public class GitService(
    ILogger<GitService> logger,
    IProcessRunner processRunner,
    IOptionsMonitor<AppSettings> appSettings,
    IShellService shellService)
    : IGitService
{
    /// <summary>
    ///     큰 출력을 생성할 수 있는 git 명령어(diff, ls-files, log)의 최대 stdout 바이트 수.
    ///     1 MB — 실용적인 사용에 충분하고, 무제한 메모리 증가를 방지합니다.
    /// </summary>
    private const int LargeOutputMaxBytes = 1 * 1024 * 1024;

    private static readonly TimeSpan BranchListCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultBranchCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GitPathCacheTtl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (List<BranchGroup> Groups, DateTime LoadedAt)> _branchGroupCache =
        new();

    // 캐시: ListBranches는 자주 변경됨 → 30초 TTL
    private readonly ConcurrentDictionary<string, (List<string> Branches, DateTime LoadedAt)> _branchListCache = new();

    // 캐시: DetectDefaultBranch는 거의 변경되지 않음 → 5분 TTL
    private readonly ConcurrentDictionary<string, (string? Branch, DateTime LoadedAt)> _defaultBranchCache = new();
    private readonly SemaphoreSlim _gitPathLock = new(1, 1);
    private DateTime _gitPathResolvedAt;

    // 해결된 git 경로 캐시
    private string? _resolvedGitPath;

    public async Task<(int Additions, int Deletions)> GetDiffStatAsync(string workingDir, string baseBranch,
        CancellationToken ct = default)
    {
        var result = await RunGitAsync(workingDir, ct, "diff", "--shortstat", baseBranch);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return (0, 0);

        // 출력을 파싱: " 3 files changed, 36 insertions(+), 16 deletions(-)"
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

    public async Task<bool> IsGitRepoAsync(string path)
    {
        if (!Directory.Exists(path))
            return false;

        var result = await RunGitAsync(path, default, "rev-parse", "--is-inside-work-tree");
        return result.Success && result.Output.Trim() == "true";
    }

    public async Task<DiffSummary> GetDiffSummaryAsync(string workingDir, string baseBranch,
        CancellationToken ct = default)
    {
        // baseBranch(예: "HEAD")가 유효한 ref가 아닌 경우(아직 커밋 없음), 빈 트리로 폴백
        var verifyResult = await RunGitAsync(workingDir, ct, "rev-parse", "--verify", "--quiet", baseBranch);
        if (!verifyResult.Success)
        {
            // 4b825dc... git의 잘 알려진 빈 트리 해시
            baseBranch = "4b825dc642cb6eb9a060e54bf899d69f82e20891";

            // 빈 트리 해시가 사용 가능한지 확인 (일부 환경에서는 실패할 수 있음)
            var emptyTreeCheck = await RunGitAsync(workingDir, ct, "cat-file", "-e", baseBranch);
            if (!emptyTreeCheck.Success)
            {
                logger.LogDebug("빈 트리 해시를 사용할 수 없음, 추적되지 않은 파일만 요약을 반환함");
                return await BuildUntrackedOnlySummaryAsync(workingDir, ct);
            }
        }

        // name-status, 추적되지 않은 파일, 및 diff 스트림을 병렬로 가져오기
        var nameStatusTask = GetNameStatusAsync(workingDir, baseBranch, ct);
        var untrackedTask = GetUntrackedFilesAsync(workingDir, ct);

        var gitPath = await ResolveGitPathAsync();
        logger.LogDebug("git diff {BaseBranch} (스트리밍)", baseBranch);
        var streamingTask = processRunner.RunStreamingAsync(new ProcessRunOptions
        {
            FileName = gitPath,
            Arguments = ["diff", baseBranch],
            WorkingDirectory = workingDir,
            EnvironmentVariables = SeoroConstants.Env.GitEnv
        }, ct);

        var nameStatus = await nameStatusTask;
        var untrackedFiles = await untrackedTask;
        var streaming = await streamingTask;

        // name-status를 파일 맵으로 파싱
        var summary = new DiffSummary();
        var fileMap = ParseNameStatusIntoFileMap(nameStatus, summary);

        // 통합 diff를 스트림으로 받아 증분으로 파싱 (전체 diff를 메모리에 로드하지 않음)
        await using (streaming)
        {
            string? currentFile = null;
            var currentDiff = new StringBuilder();
            int additions = 0, deletions = 0;
            var inDiffBlock = false;

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

                // 이름 변경 또는 모호한 헤더에 대해 +++ 라인으로 폴백
                if (currentFile == null && line.StartsWith("+++ b/"))
                    currentFile = line[6..];

                currentDiff.AppendLine(line);

                if (line.StartsWith('+') && !line.StartsWith("+++"))
                    additions++;
                else if (line.StartsWith('-') && !line.StartsWith("---"))
                    deletions++;
            }

            // 마지막 파일 플러시
            FlushFileDiff(fileMap, currentFile, currentDiff, additions, deletions);

            var (exitCode, stderr) = await streaming.WaitForExitAsync(ct);
            if (exitCode != 0)
                logger.LogWarning("git diff가 {ExitCode} 코드로 종료됨: {Stderr}", exitCode, stderr);
        }

        // 추적되지 않은 파일을 Added로 추가
        foreach (var relPath in untrackedFiles)
            try
            {
                var fullPath = Path.Combine(workingDir, relPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath)) continue;

                if (IsLikelyBinary(relPath))
                {
                    summary.Files.Add(new FileDiff
                    {
                        FilePath = relPath,
                        ChangeType = FileChangeType.Untracked,
                        IsBinary = true,
                        Additions = 0,
                        Deletions = 0
                    });
                    continue;
                }

                var content = await File.ReadAllTextAsync(fullPath, ct);
                var lines = content.Split('\n');
                var addCount = lines.Length;

                // 합성 통합 diff 작성
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
                logger.LogDebug(ex, "추적되지 않은 파일 읽기 실패: {Path}", relPath);
            }

        return summary;
    }

    public async Task<GitResult> AddWorktreeAsync(string repoDir, string worktreePath, string branchName,
        string baseBranch, CancellationToken ct = default)
    {
        var parentDir = Path.GetDirectoryName(worktreePath);
        if (parentDir != null)
            Directory.CreateDirectory(parentDir);

        // 브랜치가 이미 존재하는지 확인
        GitResult result;
        if (await BranchExistsAsync(repoDir, branchName))
            result = await RunGitAsync(repoDir, ct, "worktree", "add", worktreePath, branchName);
        else
            result = await RunGitAsync(repoDir, ct, "worktree", "add", "-b", branchName, worktreePath, baseBranch);

        if (result.Success)
            logger.LogInformation("워크트리가 {WorktreePath}에 추가됨, 브랜치: {BranchName}", worktreePath, branchName);
        else
            logger.LogWarning("워크트리 추가 실패 {WorktreePath}: {Error}", worktreePath, result.Error);

        return result;
    }

    public async Task<GitResult> CheckoutFilesAsync(string workingDir, IEnumerable<string> relativePaths,
        CancellationToken ct = default)
    {
        var paths = relativePaths.ToList();
        if (paths.Count == 0)
            return new GitResult(true, "", "");

        var args = new List<string> { "checkout", "--" };
        args.AddRange(paths);
        return await RunGitAsync(workingDir, ct, args.ToArray());
    }

    public async Task<GitResult> CloneAsync(string url, string targetDir, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var parentDir = Path.GetDirectoryName(targetDir);
        if (parentDir != null)
            Directory.CreateDirectory(parentDir);

        var gitPath = await ResolveGitPathAsync();
        logger.LogDebug("git clone --progress {Url} -> {TargetDir} 실행 중", url, targetDir);
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

        // Git clone은 진행 상황을 stderr에 씀
        var stderrBuilder = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            var buffer = new char[256];
            while (!process.StandardError.EndOfStream)
            {
                int read;
                try
                {
                    read = await process.StandardError.ReadAsync(buffer, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (read > 0)
                {
                    var text = new string(buffer, 0, read);
                    stderrBuilder.Append(text);

                    // 진행 상황 라인 추출 (\r 또는 \n으로 끝남)
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
                try
                {
                    process.Kill(true);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "git clone 프로세스 종료 실패");
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

    public async Task<GitResult> CommitAsync(string workingDir, string message, CancellationToken ct = default)
    {
        var result = await RunGitAsync(workingDir, ct, "commit", "-m", message);
        if (result.Success)
            logger.LogInformation("{WorkingDir}에 커밋됨: {Message}", workingDir,
                message.Length > 80 ? message[..80] + "..." : message);
        return result;
    }

    public async Task<GitResult> DeleteBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "branch", "-D", branchName);
        if (result.Success)
            logger.LogInformation("브랜치 삭제됨: {BranchName}", branchName);
        else
            logger.LogWarning("브랜치 삭제 실패: {BranchName}: {Error}", branchName, result.Error);
        return result;
    }

    public async Task<GitResult> FetchAllAsync(string repoDir, CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "fetch", "--all", "--prune");
        if (result.Success)
        {
            InvalidateBranchCaches(repoDir);
            logger.LogDebug("모든 fetch 완료 {RepoDir}", repoDir);
        }

        return result;
    }

    public async Task<GitResult> FetchAsync(string repoDir, CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "fetch", "origin");
        if (result.Success)
        {
            InvalidateBranchCaches(repoDir);
            logger.LogDebug("fetch 완료 {RepoDir}", repoDir);
        }

        return result;
    }

    public async Task<GitResult> InitAsync(string path, CancellationToken ct = default)
    {
        var result = await RunGitAsync(path, ct, "init");
        if (result.Success)
            logger.LogInformation("Git 저장소 초기화됨 {Path}", path);
        else
            logger.LogWarning("Git 저장소 초기화 실패 {Path}: {Error}", path, result.Error);
        return result;
    }

    public async Task<GitResult> RemoveWorktreeAsync(string repoDir, string worktreePath,
        CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "worktree", "remove", worktreePath, "--force");

        // 디렉토리가 여전히 존재하면 정리
        if (Directory.Exists(worktreePath))
            try
            {
                Directory.Delete(worktreePath, true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "워크트리 디렉토리 정리 실패: {Path}", worktreePath);
            }

        // 오래된 워크트리 항목 정리
        await RunGitAsync(repoDir, ct, "worktree", "prune");

        if (result.Success)
            logger.LogInformation("워크트리 제거됨: {WorktreePath}", worktreePath);

        return result;
    }

    public async Task<GitResult> RenameBranchAsync(string workingDir, string oldName, string newName,
        CancellationToken ct = default)
    {
        var result = await RunGitAsync(workingDir, ct, "branch", "-m", oldName, newName);
        if (result.Success)
            logger.LogInformation("브랜치 이름 변경됨: {OldName} -> {NewName}", oldName, newName);
        else
            logger.LogWarning("브랜치 이름 변경 실패: {OldName} -> {NewName}: {Error}", oldName, newName, result.Error);
        return result;
    }

    public async Task<GitResult> StageAllAsync(string workingDir, CancellationToken ct = default)
    {
        var result = await RunGitAsync(workingDir, ct, "add", "-A");
        if (result.Success)
            logger.LogDebug("{WorkingDir}의 모든 변경사항이 준비됨", workingDir);
        return result;
    }

    public async Task<List<BranchGroup>> ListAllBranchesGroupedAsync(string repoDir)
    {
        var key = Path.GetFullPath(repoDir);
        if (_branchGroupCache.TryGetValue(key, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < BranchListCacheTtl)
            return cached.Groups;

        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // 원격 브랜치 가져오기
        var remoteResult = await RunGitAsync(repoDir, default, "branch", "-r", "--format=%(refname:short)");
        if (remoteResult.Success)
            foreach (var line in remoteResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var branch = line.Trim();
                if (string.IsNullOrEmpty(branch) || branch.Contains("/HEAD")) continue;

                var slashIdx = branch.IndexOf('/');
                if (slashIdx <= 0) continue;

                // seoro/ 워크트리 브랜치 건너뛰기 (예: origin/seoro/20260409-132932)
                var branchName = branch[(slashIdx + 1)..];
                if (branchName.StartsWith(SeoroConstants.BranchPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var remoteName = branch[..slashIdx];
                if (!groups.ContainsKey(remoteName))
                    groups[remoteName] = [];
                groups[remoteName].Add(branch);
            }

        // Get local branches
        var localResult = await RunGitAsync(repoDir, default, "branch", "--format=%(refname:short)");
        var localBranches = new List<string>();
        if (localResult.Success)
            localBranches = localResult.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrEmpty(b) &&
                            !b.StartsWith(SeoroConstants.BranchPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

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

    public async Task<List<string>> GetChangedFilesAsync(string workingDir, string baseBranch,
        CancellationToken ct = default)
    {
        // tracked changes vs base branch (includes uncommitted)
        var diffResult = await RunGitBoundedAsync(workingDir, ct, "diff", "--name-only", baseBranch);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (diffResult.Success && !string.IsNullOrWhiteSpace(diffResult.Output))
            foreach (var line in diffResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.TrimEnd('\r').Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    files.Add(trimmed);
            }

        // untracked files
        var untrackedResult = await RunGitBoundedAsync(workingDir, ct, "ls-files", "--others", "--exclude-standard");
        if (untrackedResult.Success && !string.IsNullOrWhiteSpace(untrackedResult.Output))
            foreach (var line in untrackedResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.TrimEnd('\r').Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    files.Add(trimmed);
            }

        return files.ToList();
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

    public async Task<List<string>> ListTrackedFilesAsync(string workingDir, CancellationToken ct = default)
    {
        var result = await RunGitBoundedAsync(workingDir, ct, "ls-files");
        if (!result.Success) return new List<string>();
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task<string?> DetectDefaultBranchAsync(string repoDir)
    {
        var key = Path.GetFullPath(repoDir);
        if (_defaultBranchCache.TryGetValue(key, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < DefaultBranchCacheTtl)
            return cached.Branch;

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

        // Fallback: check remote branches first, then local
        var remoteMain =
            await RunGitAsync(repoDir, default, "show-ref", "--verify", "--quiet", "refs/remotes/origin/main");
        if (remoteMain.Success)
        {
            _defaultBranchCache[key] = ("origin/main", DateTime.UtcNow);
            return "origin/main";
        }

        var remoteMaster = await RunGitAsync(repoDir, default, "show-ref", "--verify", "--quiet",
            "refs/remotes/origin/master");
        if (remoteMaster.Success)
        {
            _defaultBranchCache[key] = ("origin/master", DateTime.UtcNow);
            return "origin/master";
        }

        // No remote — fall back to local branches
        if (await BranchExistsAsync(repoDir, "main"))
        {
            _defaultBranchCache[key] = ("main", DateTime.UtcNow);
            return "main";
        }

        if (await BranchExistsAsync(repoDir, "master"))
        {
            _defaultBranchCache[key] = ("master", DateTime.UtcNow);
            return "master";
        }

        // Last resort: get current branch
        var current = await GetCurrentBranchAsync(repoDir);
        _defaultBranchCache[key] = (current, DateTime.UtcNow);
        logger.LogDebug("Default branch for {RepoDir}: {Branch}", repoDir, current);
        return current;
    }

    public async Task<string?> GetCurrentBranchAsync(string repoDir)
    {
        var result = await RunGitAsync(repoDir, default, "rev-parse", "--abbrev-ref", "HEAD");
        return result.Success ? result.Output.Trim() : null;
    }

    public async Task<string?> ResolveCommitHashAsync(string repoDir, string refName, CancellationToken ct = default)
    {
        var result = await RunGitAsync(repoDir, ct, "rev-parse", "--verify", refName);
        return result.Success ? result.Output.Trim() : null;
    }

    public async Task<string[]> ReadBaseFileLinesAsync(string workingDir, string baseBranch, string relativePath,
        int startLine, int endLine, CancellationToken ct = default)
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

    public async Task<string[]> ReadFileLinesAsync(string workingDir, string relativePath, int startLine, int endLine,
        CancellationToken ct = default)
    {
        var content = await ReadFileAsync(workingDir, relativePath, ct);
        var allLines = content.Split('\n');
        var from = Math.Max(0, startLine - 1); // 1-based to 0-based
        var to = Math.Min(allLines.Length, endLine - 1);
        if (from >= to) return [];
        return allLines[from..to];
    }

    public async Task<string> GetNameStatusAsync(string workingDir, string baseBranch, CancellationToken ct = default)
    {
        // Use baseBranch (not baseBranch...HEAD) to include uncommitted working tree changes
        var result = await RunGitBoundedAsync(workingDir, ct, "diff", "--name-status", baseBranch);
        return result.Success ? result.Output : "";
    }

    public async Task<string> ReadFileAsync(string workingDir, string relativePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(workingDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return await File.ReadAllTextAsync(fullPath, ct);
    }

    public async Task<bool> BranchExistsAsync(string repoDir, string branchName)
    {
        // Check local branches
        var result = await RunGitAsync(repoDir, default, "show-ref", "--verify", "--quiet", $"refs/heads/{branchName}");
        if (result.Success) return true;

        // Check remote branches
        result = await RunGitAsync(repoDir, default, "show-ref", "--verify", "--quiet",
            $"refs/remotes/origin/{branchName}");
        return result.Success;
    }

    public async Task<List<string>> ListBranchesAsync(string repoDir)
    {
        var key = Path.GetFullPath(repoDir);
        if (_branchListCache.TryGetValue(key, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < BranchListCacheTtl)
            return cached.Branches;

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
    ///     Creates a git process for CloneAsync which needs character-by-character stderr streaming.
    ///     All other git commands use IProcessRunner via RunGitAsync.
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
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        foreach (var (key, value) in SeoroConstants.Env.GitEnv)
            psi.Environment[key] = value;
        return new Process { StartInfo = psi };
    }

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff", ".tif",
        ".svg", ".pdf", ".zip", ".tar", ".gz", ".7z", ".rar",
        ".exe", ".dll", ".so", ".dylib", ".wasm",
        ".mp3", ".mp4", ".wav", ".ogg", ".flac", ".mov", ".avi", ".mkv",
        ".ttf", ".otf", ".woff", ".woff2",
        ".db", ".sqlite", ".bin", ".dat"
    };

    private static bool IsLikelyBinary(string filePath) =>
        BinaryExtensions.Contains(Path.GetExtension(filePath));

    private static void FlushFileDiff(Dictionary<string, FileDiff> fileMap, string? filePath, StringBuilder diffContent,
        int additions, int deletions)
    {
        if (filePath == null) return;
        if (!fileMap.TryGetValue(filePath, out var fileDiff)) return;

        fileDiff.UnifiedDiff = diffContent.ToString();
        if (IsLikelyBinary(filePath))
        {
            fileDiff.IsBinary = true;
            fileDiff.Additions = 0;
            fileDiff.Deletions = 0;
        }
        else
        {
            fileDiff.Additions = additions;
            fileDiff.Deletions = deletions;
        }
    }

    private async Task<DiffSummary> BuildUntrackedOnlySummaryAsync(string workingDir, CancellationToken ct)
    {
        var summary = new DiffSummary();
        var untrackedFiles = await GetUntrackedFilesAsync(workingDir, ct);

        foreach (var relPath in untrackedFiles)
            try
            {
                var fullPath = Path.Combine(workingDir, relPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath)) continue;

                if (IsLikelyBinary(relPath))
                {
                    summary.Files.Add(new FileDiff
                    {
                        FilePath = relPath,
                        ChangeType = FileChangeType.Untracked,
                        IsBinary = true,
                        Additions = 0,
                        Deletions = 0
                    });
                    continue;
                }

                var content = await File.ReadAllTextAsync(fullPath, ct);
                var lines = content.Split('\n');
                var addCount = lines.Length;

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
                logger.LogDebug(ex, "추적되지 않은 파일 읽기 실패: {Path}", relPath);
            }

        return summary;
    }

    // ────────────────────────────────────────────────
    //  Phase 1: 원격 URL·푸시·충돌·시뮬레이션·스쿼시 머지
    // ────────────────────────────────────────────────

    public async Task<string?> GetRemoteUrlAsync(string repoDir, string remoteName = "origin",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoDir) || !Directory.Exists(repoDir))
            return null;

        // git remote get-url <name> — 실패 시 exit code 2 + stderr.
        // 원격이 없거나 저장소가 아니면 null 을 돌려 호출자가 None 으로 폴백할 수 있게 한다.
        var result = await RunGitAsync(repoDir, ct, "remote", "get-url", remoteName);
        if (!result.Success)
        {
            logger.LogDebug("원격 URL 조회 실패: repo={Repo} remote={Remote} err={Err}",
                repoDir, remoteName, result.Error);
            return null;
        }

        var url = result.Output.Trim();
        if (string.IsNullOrEmpty(url))
            return null;

        logger.LogDebug("원격 URL 감지: repo={Repo} remote={Remote} url={Url}",
            repoDir, remoteName, GitHubUrlHelper.MaskCredentials(url));
        return url;
    }

    public async Task<GitResult> PushAsync(string workingDir, string remote, string branch,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workingDir) || string.IsNullOrWhiteSpace(remote)
                                                  || string.IsNullOrWhiteSpace(branch))
            return new GitResult(false, string.Empty, "push 파라미터가 비어 있습니다.");

        logger.LogInformation("git push 시작: workdir={Dir} remote={Remote} branch={Branch}",
            workingDir, remote, branch);

        var result = await RunGitAsync(workingDir, ct, "push", remote, branch);
        if (result.Success)
            logger.LogInformation("git push 완료: {Branch} → {Remote}", branch, remote);
        else
            logger.LogWarning("git push 실패: {Branch} → {Remote}: {Error}", branch, remote, result.Error);

        return result;
    }

    public async Task<bool> HasUnresolvedConflictsAsync(string workingDir, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workingDir) || !Directory.Exists(workingDir))
            return false;

        // 1) .git/MERGE_HEAD 존재 확인. 워크트리의 경우 .git 은 파일(gitdir: ...) 이라
        //    rev-parse --git-dir 로 실제 경로를 물어본다.
        var gitDirResult = await RunGitAsync(workingDir, ct, "rev-parse", "--git-dir");
        if (!gitDirResult.Success)
            return false;

        var relativeGitDir = gitDirResult.Output.Trim();
        var gitDir = Path.IsPathRooted(relativeGitDir)
            ? relativeGitDir
            : Path.GetFullPath(Path.Combine(workingDir, relativeGitDir));

        if (!File.Exists(Path.Combine(gitDir, "MERGE_HEAD")))
            return false;

        // 2) git status --porcelain 의 UU/AA/DD/AU/UA/DU/UD 마커 확인.
        var status = await GetStatusPorcelainAsync(workingDir, ct);
        return status.Any(line => line.Length >= 2 && IsConflictMarker(line.AsSpan(0, 2)));
    }

    private static bool IsConflictMarker(ReadOnlySpan<char> code)
    {
        // git status --porcelain 의 2자리 충돌 표기. 자세한 정의는 `git status --help` 참조.
        return code is "UU" or "AA" or "DD" or "AU" or "UA" or "DU" or "UD";
    }

    public async Task<(int Ahead, int Behind)?> FetchAndCompareAsync(string repoDir,
        string sourceRef, string targetRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoDir) || !Directory.Exists(repoDir))
            return null;

        logger.LogDebug("fetch + ahead/behind 계산 시작: repo={Repo} source={Src} target={Tgt}",
            repoDir, sourceRef, targetRef);

        // 10초 타임아웃 — 네트워크 지연으로 UI 가 굳지 않도록.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            // 타겟 ref 에서 리모트 접두사를 벗겨 fetch 대상 브랜치 이름을 추출한다.
            var normalizedTarget = BranchRefNormalizer.Normalize(targetRef);
            var fetchResult = await RunGitAsync(repoDir, timeout.Token, "fetch", "origin", normalizedTarget);
            if (!fetchResult.Success)
            {
                logger.LogWarning("fetch 실패 (오프라인 가능성): repo={Repo} err={Err}", repoDir, fetchResult.Error);
                return null;
            }

            // git rev-list --count --left-right <source>...<target>
            //  → "<source-only> <target-only>" 출력 (source ahead, target ahead)
            var revList = await RunGitAsync(repoDir, timeout.Token, "rev-list", "--count", "--left-right",
                $"{sourceRef}...{targetRef}");
            if (!revList.Success || string.IsNullOrWhiteSpace(revList.Output))
            {
                logger.LogWarning("rev-list 실패: repo={Repo} err={Err}", repoDir, revList.Error);
                return null;
            }

            var parts = revList.Output.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                // 공백 기반 구분일 수도 있음
                parts = revList.Output.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            }
            if (parts.Length != 2 || !int.TryParse(parts[0], out var ahead) || !int.TryParse(parts[1], out var behind))
                return null;

            logger.LogDebug("ahead/behind 계산 완료: ahead={Ahead} behind={Behind}", ahead, behind);
            return (ahead, behind);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("FetchAndCompareAsync 타임아웃: repo={Repo}", repoDir);
            return null;
        }
    }

    public async Task<MergeSimulationResult> SimulateMergeAsync(string repoDir,
        string sourceRef, string targetRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoDir) || !Directory.Exists(repoDir))
            return MergeSimulationResult.Failed("저장소 경로가 유효하지 않습니다.");

        logger.LogDebug("머지 시뮬레이션 시작: repo={Repo} source={Src} target={Tgt}",
            repoDir, sourceRef, targetRef);

        // 1) ahead/behind 는 네트워크 없이 계산 가능하지만, 정확도를 위해 fetch 결과에 의존한다.
        //    호출자가 FetchAndCompareAsync 를 먼저 부르는 것이 권장되나 이 메서드 자체는 fetch 를 하지 않아
        //    캐시된 리모트 상태로 동작할 수 있다.
        var revList = await RunGitAsync(repoDir, ct, "rev-list", "--count", "--left-right",
            $"{sourceRef}...{targetRef}");
        int ahead = 0, behind = 0;
        if (revList.Success && !string.IsNullOrWhiteSpace(revList.Output))
        {
            var parts = revList.Output.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out ahead);
                int.TryParse(parts[1], out behind);
            }
        }

        // 2) git merge-tree --write-tree <target> <source>
        //    종료 코드: 0 = 충돌 없음, 1 = 충돌 있음, 그 외 = 에러 (git < 2.38 에서는 인자 해석 실패)
        var mergeTree = await RunGitAsync(repoDir, ct, "merge-tree", "--write-tree",
            "--name-only", "-z", targetRef, sourceRef);

        // --write-tree 미지원 버전 폴백: 에러 텍스트로 감지.
        if (!mergeTree.Success && mergeTree.Error.Contains("write-tree", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("git merge-tree --write-tree 미지원 (git 2.38+ 필요). repo={Repo}", repoDir);
            return new MergeSimulationResult(false, false, [], ahead, behind,
                "git 2.38 이상이 필요합니다 (merge-tree --write-tree).");
        }

        // 종료 코드가 0 이면 충돌 없음, 1 이면 충돌. 그 외는 실패.
        var conflicts = new List<string>();
        var wouldConflict = false;

        if (mergeTree.Success)
        {
            wouldConflict = false;
        }
        else
        {
            // merge-tree 는 충돌 시 exit 1 을 반환하고 stdout 에 트리 해시 + 충돌 파일 목록을 쓴다.
            // 우리 RunGitAsync 는 exit!=0 이면 Success=false 로 돌리므로 stdout 이 비었는지 확인.
            wouldConflict = !string.IsNullOrWhiteSpace(mergeTree.Output);
            if (wouldConflict)
            {
                // --name-only -z: NUL 구분된 파일 경로 목록. 첫 줄은 트리 해시라 건너뛴다.
                var lines = mergeTree.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 1; i < lines.Length; i++)
                {
                    foreach (var file in lines[i].Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!string.IsNullOrWhiteSpace(file))
                            conflicts.Add(file.Trim());
                    }
                }
            }
            else
            {
                logger.LogWarning("merge-tree 실패: repo={Repo} err={Err}", repoDir, mergeTree.Error);
                return new MergeSimulationResult(false, false, [], ahead, behind, mergeTree.Error);
            }
        }

        logger.LogDebug("머지 시뮬레이션 완료: conflict={Conflict} files={Count} ahead={Ahead} behind={Behind}",
            wouldConflict, conflicts.Count, ahead, behind);
        return new MergeSimulationResult(true, wouldConflict, conflicts, ahead, behind, null);
    }

    public async Task<List<string>> GetUncommittedChangesAsync(string workingDir,
        CancellationToken ct = default)
    {
        // staged + unstaged + untracked 전부 — 사용자에게 "미커밋 변경 N개" 라는 단일 지표로 보여주기 위함.
        var porcelain = await GetStatusPorcelainAsync(workingDir, ct);
        var files = new List<string>();
        foreach (var line in porcelain)
        {
            if (line.Length < 3) continue;
            // porcelain 형식: XY <path> (또는 renames 는 arrow 포함). 첫 2자가 상태 코드, 이후 공백, 이후 경로.
            var path = line[3..].Trim();
            // rename 은 "old -> new" 형태라 오른쪽만 취한다.
            var arrowIdx = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIdx > 0)
                path = path[(arrowIdx + 4)..];
            if (!string.IsNullOrWhiteSpace(path))
                files.Add(path);
        }
        return files;
    }

    public async Task<SquashMergeResult> SquashMergeViaTempCloneAsync(
        string mainRepoDir,
        string sourceWorktreePath,
        string sourceBranchName,
        string targetBranchName,
        string commitMessage,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mainRepoDir) || !Directory.Exists(mainRepoDir))
            return SquashMergeResult.Failed("메인 레포 경로가 유효하지 않습니다.");
        if (string.IsNullOrWhiteSpace(sourceWorktreePath) || !Directory.Exists(sourceWorktreePath))
            return SquashMergeResult.Failed("소스 워크트리 경로가 유효하지 않습니다.");
        if (string.IsNullOrWhiteSpace(sourceBranchName) || string.IsNullOrWhiteSpace(targetBranchName))
            return SquashMergeResult.Failed("브랜치 이름이 비어 있습니다.");
        if (string.IsNullOrWhiteSpace(commitMessage))
            return SquashMergeResult.Failed("커밋 메시지가 비어 있습니다.");

        var tempDir = Path.Combine(AppPaths.MergeStaging, Guid.NewGuid().ToString("N"));
        logger.LogInformation("스쿼시 머지 시작: source={Src} target={Tgt} temp={Temp}",
            sourceBranchName, targetBranchName, tempDir);

        try
        {
            // 1) 임시 클론 디렉터리 생성 후 git clone --no-hardlinks.
            //    --no-hardlinks 는 하드링크 기반 .git 객체 공유를 끄고 실제 복사를 강제한다.
            //    이유: 임시 클론에서 write 가 일어나면 하드링크 때문에 메인 레포의 객체에 영향을 줄 수 있다.
            progress?.Report("임시 클론 생성 중...");
            Directory.CreateDirectory(AppPaths.MergeStaging);
            var cloneResult = await RunGitAsync(AppPaths.MergeStaging, ct,
                "clone", "--no-hardlinks", mainRepoDir, tempDir);
            if (!cloneResult.Success)
            {
                logger.LogError("임시 클론 실패: {Err}", cloneResult.Error);
                return SquashMergeResult.Failed($"임시 클론 실패: {cloneResult.Error}");
            }

            // 2) 임시 클론에서 타겟 브랜치를 fetch 후 체크아웃.
            //    임시 클론의 origin = mainRepoDir 이므로 타겟 브랜치는 origin/<target> 으로 가져온다.
            progress?.Report($"타겟 브랜치 `{targetBranchName}` 체크아웃 중...");
            var normalizedTarget = BranchRefNormalizer.Normalize(targetBranchName);
            var fetchTarget = await RunGitAsync(tempDir, ct, "fetch", "origin", normalizedTarget);
            if (!fetchTarget.Success)
            {
                logger.LogError("타겟 브랜치 fetch 실패: {Err}", fetchTarget.Error);
                return SquashMergeResult.Failed($"타겟 브랜치 fetch 실패: {fetchTarget.Error}");
            }

            var checkoutResult = await RunGitAsync(tempDir, ct, "checkout", "-B", normalizedTarget,
                $"origin/{normalizedTarget}");
            if (!checkoutResult.Success)
            {
                logger.LogError("타겟 브랜치 체크아웃 실패: {Err}", checkoutResult.Error);
                return SquashMergeResult.Failed($"타겟 브랜치 체크아웃 실패: {checkoutResult.Error}");
            }

            // 3) 소스 브랜치를 원본 워크트리에서 직접 fetch 해 로컬 ref refs/seoro/source 로 저장.
            //    이 방식은 메인 레포를 통하지 않고 워크트리가 쓰던 최신 커밋을 그대로 가져온다.
            progress?.Report($"소스 브랜치 `{sourceBranchName}` 가져오는 중...");
            var fetchSource = await RunGitAsync(tempDir, ct, "fetch", sourceWorktreePath,
                $"{sourceBranchName}:refs/seoro/source");
            if (!fetchSource.Success)
            {
                logger.LogError("소스 브랜치 fetch 실패: {Err}", fetchSource.Error);
                return SquashMergeResult.Failed($"소스 브랜치 fetch 실패: {fetchSource.Error}");
            }

            // 4) squash merge 수행.
            progress?.Report("스쿼시 머지 실행 중...");
            var mergeResult = await RunGitAsync(tempDir, ct, "merge", "--squash", "refs/seoro/source");
            if (!mergeResult.Success)
            {
                // 충돌 여부 판정: .git/MERGE_HEAD 또는 porcelain UU 마커.
                var hasConflict = await HasUnresolvedConflictsAsync(tempDir, ct);
                if (hasConflict)
                {
                    logger.LogWarning("머지 충돌 감지. merge --abort 후 임시 클론 삭제 (Alt A)");
                    var conflictFiles = await GetConflictingFilesAsync(tempDir, ct);
                    await RunGitAsync(tempDir, ct, "merge", "--abort");
                    return SquashMergeResult.ConflictDetected(conflictFiles);
                }
                // squash 머지는 MERGE_HEAD 를 만들지 않고 index 에만 변경을 반영하므로
                // HasUnresolvedConflictsAsync 가 false 여도 충돌이 있을 수 있다. porcelain 으로 재확인.
                var porcelain = await GetStatusPorcelainAsync(tempDir, ct);
                var conflicts = porcelain
                    .Where(l => l.Length >= 2 && IsConflictMarker(l.AsSpan(0, 2)))
                    .Select(l => l.Length >= 3 ? l[3..].Trim() : l)
                    .ToList();
                if (conflicts.Count > 0)
                {
                    logger.LogWarning("squash 머지 충돌 감지: {Count}개 파일", conflicts.Count);
                    await RunGitAsync(tempDir, ct, "reset", "--hard", "HEAD");
                    return SquashMergeResult.ConflictDetected(conflicts);
                }

                logger.LogError("머지 실패 (충돌 아님): {Err}", mergeResult.Error);
                return SquashMergeResult.Failed($"머지 실패: {mergeResult.Error}");
            }

            // 5) squash 결과를 커밋 (squash 는 index 만 갱신하므로 별도 커밋 필요).
            progress?.Report("커밋 생성 중...");
            var commitResult = await RunGitAsync(tempDir, ct, "commit", "-m", commitMessage);
            if (!commitResult.Success)
            {
                // "nothing to commit" 은 squash 가 사실상 no-op 인 경우로, 에러로 취급.
                logger.LogError("커밋 실패: {Err}", commitResult.Error);
                return SquashMergeResult.Failed($"커밋 실패: {commitResult.Error}");
            }

            // 6) origin(=mainRepoDir) 에 push. temp clone 의 origin 은 로컬 메인 레포 경로이므로
            //    네트워크 없이 즉시 업데이트된다.
            progress?.Report("메인 레포에 반영 중...");
            var pushResult = await RunGitAsync(tempDir, ct, "push", "origin", normalizedTarget);
            if (!pushResult.Success)
            {
                logger.LogError("메인 레포 push 실패: {Err}", pushResult.Error);
                return SquashMergeResult.Failed($"메인 레포에 반영 실패: {pushResult.Error}");
            }

            // 메인 레포의 브랜치 캐시를 무효화해 UI 가 즉시 새 ref 를 반영하도록 한다.
            InvalidateBranchCaches(mainRepoDir);

            logger.LogInformation("스쿼시 머지 완료: {Source} → {Target}", sourceBranchName, targetBranchName);
            return SquashMergeResult.Succeeded(commitResult.Output);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("스쿼시 머지 취소됨: temp={Temp}", tempDir);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "스쿼시 머지 예외: temp={Temp}", tempDir);
            return SquashMergeResult.Failed(ex.Message);
        }
        finally
        {
            // 성공·실패·취소 무관하게 임시 클론 디렉터리를 정리한다.
            // (Alt A 전용 — Alt B 가 도입되면 이 finally 블록을 수정해야 한다.)
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                    logger.LogDebug("임시 클론 삭제: {Temp}", tempDir);
                }
            }
            catch (Exception cleanupEx)
            {
                logger.LogWarning(cleanupEx, "임시 클론 삭제 실패: {Temp}", tempDir);
            }
        }
    }

    public Task InvalidateBranchCacheAsync(string repoDir)
    {
        InvalidateBranchCaches(repoDir);
        logger.LogDebug("브랜치 캐시 수동 무효화: {Repo}", repoDir);
        return Task.CompletedTask;
    }

    private async Task<List<string>> GetConflictingFilesAsync(string workingDir, CancellationToken ct)
    {
        var porcelain = await GetStatusPorcelainAsync(workingDir, ct);
        return porcelain
            .Where(line => line.Length >= 2 && IsConflictMarker(line.AsSpan(0, 2)))
            .Select(line => line.Length >= 3 ? line[3..].Trim() : line.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
    }

    private async Task<GitResult> RunGitAsync(string workingDir, CancellationToken ct, params string[] args)
    {
        return await RunGitCoreAsync(workingDir, null, ct, args);
    }

    private async Task<GitResult> RunGitBoundedAsync(string workingDir, CancellationToken ct, params string[] args)
    {
        return await RunGitCoreAsync(workingDir, LargeOutputMaxBytes, ct, args);
    }

    private async Task<GitResult> RunGitCoreAsync(string workingDir, int? maxOutputBytes, CancellationToken ct,
        params string[] args)
    {
        var gitPath = await ResolveGitPathAsync();
        logger.LogDebug("git {Arguments}", string.Join(" ", args));
        var result = await processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = gitPath,
            Arguments = args,
            WorkingDirectory = workingDir,
            EnvironmentVariables = SeoroConstants.Env.GitEnv,
            MaxOutputBytes = maxOutputBytes
        }, ct);
        if (result.Truncated)
            logger.LogWarning("git {Command} output truncated at {MaxBytes} bytes", args.FirstOrDefault(),
                maxOutputBytes);
        return new GitResult(result.Success, result.Stdout, result.Stderr);
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

    private async Task<string> ResolveGitPathAsync()
    {
        // Use configured path if set
        var configuredPath = appSettings.CurrentValue.GitPath;
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

            var resolved = await shellService.WhichAsync("git");
            _resolvedGitPath = resolved ?? "git";
            _gitPathResolvedAt = DateTime.UtcNow;

            if (resolved != null)
                logger.LogDebug("Resolved git path: {Path}", resolved);

            return _resolvedGitPath;
        }
        finally
        {
            _gitPathLock.Release();
        }
    }

    private void InvalidateBranchCaches(string repoDir)
    {
        var key = Path.GetFullPath(repoDir);
        _defaultBranchCache.TryRemove(key, out _);
        _branchListCache.TryRemove(key, out _);
        _branchGroupCache.TryRemove(key, out _);
    }

    /// <summary>
    ///     Extracts the file path from a diff header using symmetric path structure.
    ///     Handles paths containing " b/" correctly, unlike LastIndexOf(" b/").
    ///     Accepts "diff --git a/&lt;path&gt; b/&lt;path&gt;" or "a/&lt;path&gt; b/&lt;path&gt;" formats.
    ///     Returns null for renames (asymmetric paths) — caller should fall back to +++ line.
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
}