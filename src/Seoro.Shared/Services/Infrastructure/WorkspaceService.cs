using System.Text.RegularExpressions;
using Seoro.Shared.Services.Migration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seoro.Shared.Services.Infrastructure;

public partial class WorkspaceService(
    IGitService gitService,
    IOptionsMonitor<AppSettings> appSettings,
    ILogger<WorkspaceService> logger)
    : IWorkspaceService
{
    private readonly string _repoInfoDir = AppPaths.Repos;
    private readonly string _workspacesDir = AppPaths.Workspaces;

    public event Action<Workspace>? OnWorkspaceSaved;

    public async Task DeleteWorkspaceAsync(string workspaceId)
    {
        var path = Path.Combine(_workspacesDir, $"{workspaceId}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
            logger.LogInformation("워크스페이스 {WorkspaceId} 삭제됨", workspaceId);
        }

        await Task.CompletedTask;
    }

    public async Task SaveWorkspaceAsync(Workspace workspace)
    {
        SettingsValidator.SanitizeWorkspace(workspace);
        workspace.MigratePreferences();
        workspace.SchemaVersion = 2;
        workspace.UpdatedAt = DateTime.UtcNow;
        var path = Path.Combine(_workspacesDir, $"{workspace.Id}.json");
        var json = MigratingJsonWriter.Write(workspace, JsonDefaults.Options);
        await AtomicFileWriter.WriteAsync(path, json);
        OnWorkspaceSaved?.Invoke(workspace);
        logger.LogDebug("워크스페이스 {WorkspaceId} 저장됨: {Name}", workspace.Id, workspace.Name);
    }

    public async Task<GitRepoInfo?> FindExistingRepoAsync(string remoteUrl)
    {
        var normalizedUrl = NormalizeUrl(remoteUrl);

        if (!Directory.Exists(_repoInfoDir))
            return null;

        foreach (var file in Directory.GetFiles(_repoInfoDir, "*.json"))
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var (info, migrated, migratedJson) = MigratingJsonReader.Read<GitRepoInfo>(json, JsonDefaults.Options);
                if (info != null && NormalizeUrl(info.RemoteUrl) == normalizedUrl && Directory.Exists(info.LocalPath))
                {
                    if (migrated && migratedJson != null)
                        await AtomicFileWriter.WriteAsync(file, migratedJson);
                    return info;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "저장소 정보 파일 읽기 실패: {File}", file);
            }

        return null;
    }

    public async Task<List<Workspace>> GetWorkspacesAsync()
    {
        var workspaces = new List<Workspace>();
        if (!Directory.Exists(_workspacesDir))
            return workspaces;

        foreach (var file in Directory.GetFiles(_workspacesDir, "*.json"))
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var (workspace, migrated, migratedJson) =
                    MigratingJsonReader.Read<Workspace>(json, JsonDefaults.Options);
                if (workspace != null)
                {
                    workspace.MigratePreferences();
                    workspaces.Add(workspace);
                    if (migrated && migratedJson != null)
                        await AtomicFileWriter.WriteAsync(file, migratedJson);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "손상된 워크스페이스 파일 건너뜀: {File}", file);
            }

        return workspaces.OrderByDescending(w => w.UpdatedAt).ToList();
    }

    public async Task<string> GetWorktreesDirAsync()
    {
        var dir = Path.Combine(await GetBaseDirAsync(), "worktrees");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<Workspace?> LoadWorkspaceAsync(string workspaceId)
    {
        var path = Path.Combine(_workspacesDir, $"{workspaceId}.json");
        if (!File.Exists(path))
        {
            logger.LogDebug("워크스페이스 파일을 찾을 수 없음: {WorkspaceId}", workspaceId);
            return null;
        }

        var json = await File.ReadAllTextAsync(path);
        var (workspace, migrated, migratedJson) = MigratingJsonReader.Read<Workspace>(json, JsonDefaults.Options);
        workspace?.MigratePreferences();
        if (migrated && migratedJson != null)
            await AtomicFileWriter.WriteAsync(path, migratedJson);
        logger.LogDebug("워크스페이스 {WorkspaceId} 로드됨: {Name}", workspace?.Id, workspace?.Name);
        return workspace;
    }

    public async Task<Workspace> CreateFromLocalAsync(string localPath, string name, string model,
        CancellationToken ct = default)
    {
        var workspace = new Workspace
        {
            Name = name,
            RepoLocalPath = localPath,
            DefaultModel = model,
            Status = WorkspaceStatus.Initializing
        };

        await SaveWorkspaceAsync(workspace);

        try
        {
            if (!await gitService.IsGitRepoAsync(localPath))
            {
                workspace.Status = WorkspaceStatus.Error;
                workspace.Error = AppError.InvalidGitRepo("Not a valid git repository.");
                await DeleteWorkspaceAsync(workspace.Id);
                return workspace;
            }

            workspace.Status = WorkspaceStatus.Ready;
            workspace.Error = null;
            await SaveWorkspaceAsync(workspace);
            logger.LogInformation("워크스페이스 {Name} 로컬 경로에서 생성됨 {Path}", name, localPath);
            return workspace;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "로컬 경로에서 워크스페이스 생성 실패: {Path}", localPath);
            workspace.Status = WorkspaceStatus.Error;
            workspace.Error = AppError.FromException(ErrorCode.Unknown, ex);
            await DeleteWorkspaceAsync(workspace.Id);
            return workspace;
        }
    }

    public async Task<Workspace> CreateFromUrlAsync(string url, string name, string model,
        string? targetDir = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var workspace = new Workspace
        {
            Name = name,
            RepoUrl = url,
            DefaultModel = model,
            Status = WorkspaceStatus.Initializing
        };

        await SaveWorkspaceAsync(workspace);

        try
        {
            // Find or clone the repo
            var repoInfo = await FindExistingRepoAsync(url);
            string repoDir;

            if (repoInfo != null)
            {
                repoDir = repoInfo.LocalPath;
                progress?.Report("Using existing clone...");

                // Fetch latest
                progress?.Report("Fetching latest changes...");
                await gitService.FetchAsync(repoDir, ct);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    repoDir = targetDir;
                }
                else
                {
                    var slug = ExtractRepoSlug(url);
                    var reposDir = await GetReposDirAsync();
                    repoDir = Path.Combine(reposDir, slug);

                    // Avoid directory name collision
                    var counter = 1;
                    var originalDir = repoDir;
                    while (Directory.Exists(repoDir)) repoDir = $"{originalDir}-{counter++}";
                }

                progress?.Report("Cloning repository...");
                var cloneResult = await gitService.CloneAsync(url, repoDir, progress, ct);
                if (!cloneResult.Success)
                {
                    workspace.Status = WorkspaceStatus.Error;
                    workspace.Error = AppError.CloneFailed(cloneResult.Error);
                    await DeleteWorkspaceAsync(workspace.Id);
                    return workspace;
                }

                // Save repo info
                var defaultBranch = await gitService.DetectDefaultBranchAsync(repoDir) ?? "main";
                repoInfo = new GitRepoInfo
                {
                    RemoteUrl = NormalizeUrl(url),
                    LocalPath = repoDir,
                    DefaultBranch = defaultBranch
                };
                await SaveRepoInfoAsync(repoInfo);
            }

            workspace.RepoLocalPath = repoDir;

            workspace.Status = WorkspaceStatus.Ready;
            workspace.Error = null;
            await SaveWorkspaceAsync(workspace);
            progress?.Report("Workspace ready!");
            logger.LogInformation("워크스페이스 {Name} URL에서 생성됨 {Url}", name, url);
            return workspace;
        }
        catch (OperationCanceledException)
        {
            await DeleteWorkspaceAsync(workspace.Id);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "URL에서 워크스페이스 생성 실패: {Url}", url);
            workspace.Status = WorkspaceStatus.Error;
            workspace.Error = AppError.FromException(ErrorCode.Unknown, ex);
            await DeleteWorkspaceAsync(workspace.Id);
            return workspace;
        }
    }

    [GeneratedRegex(@"[^a-zA-Z0-9\-_.]")]
    private static partial Regex SlugRegex();

    private static string ExtractRepoSlug(string url)
    {
        var cleaned = url.TrimEnd('/');
        if (cleaned.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^4];

        var lastSlash = cleaned.LastIndexOfAny(['/', ':']);
        var slug = lastSlash >= 0 ? cleaned[(lastSlash + 1)..] : cleaned;

        return SlugRegex().Replace(slug, "-").Trim('-');
    }

    private static string NormalizeUrl(string url)
    {
        var normalized = url.Trim().TrimEnd('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];
        return normalized.ToLowerInvariant();
    }

    private async Task SaveRepoInfoAsync(GitRepoInfo repoInfo)
    {
        var path = Path.Combine(_repoInfoDir, $"{repoInfo.Id}.json");
        var json = MigratingJsonWriter.Write(repoInfo, JsonDefaults.Options);
        await AtomicFileWriter.WriteAsync(path, json);
    }

    private Task<string> GetBaseDirAsync()
    {
        var settings = appSettings.CurrentValue;
        var baseDir = !string.IsNullOrWhiteSpace(settings.DefaultCloneDirectory)
            ? settings.DefaultCloneDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Seoro");
        return Task.FromResult(baseDir);
    }

    private async Task<string> GetReposDirAsync()
    {
        var dir = Path.Combine(await GetBaseDirAsync(), "repos");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<string> GetDefaultClonePathAsync(string url)
    {
        var slug = ExtractRepoSlug(url);
        var reposDir = await GetReposDirAsync();
        return Path.Combine(reposDir, slug);
    }
}