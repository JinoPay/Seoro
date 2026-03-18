using System.Text.Json;
using System.Text.RegularExpressions;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Services;

public partial class WorkspaceService : IWorkspaceService
{
    private readonly IGitService _gitService;
    private readonly IOptionsMonitor<AppSettings> _appSettings;
    private readonly ILogger<WorkspaceService> _logger;
    private readonly string _workspacesDir = AppPaths.Workspaces;
    private readonly string _repoInfoDir = AppPaths.Repos;

    public WorkspaceService(IGitService gitService, IOptionsMonitor<AppSettings> appSettings, ILogger<WorkspaceService> logger)
    {
        _gitService = gitService;
        _appSettings = appSettings;
        _logger = logger;
    }

    private Task<string> GetBaseDirAsync()
    {
        var settings = _appSettings.CurrentValue;
        var baseDir = !string.IsNullOrWhiteSpace(settings.DefaultCloneDirectory)
            ? settings.DefaultCloneDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Cominomi");
        return Task.FromResult(baseDir);
    }

    private async Task<string> GetReposDirAsync()
    {
        var dir = Path.Combine(await GetBaseDirAsync(), "repos");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<string> GetWorktreesDirAsync()
    {
        var dir = Path.Combine(await GetBaseDirAsync(), "worktrees");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<List<Workspace>> GetWorkspacesAsync()
    {
        var workspaces = new List<Workspace>();
        if (!Directory.Exists(_workspacesDir))
            return workspaces;

        foreach (var file in Directory.GetFiles(_workspacesDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var workspace = JsonSerializer.Deserialize<Workspace>(json, JsonDefaults.Options);
                if (workspace != null)
                    workspaces.Add(workspace);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping corrupted workspace file: {File}", file);
            }
        }

        return workspaces.OrderByDescending(w => w.UpdatedAt).ToList();
    }

    public async Task<Workspace?> LoadWorkspaceAsync(string workspaceId)
    {
        var path = Path.Combine(_workspacesDir, $"{workspaceId}.json");
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Workspace>(json, JsonDefaults.Options);
    }

    public async Task SaveWorkspaceAsync(Workspace workspace)
    {
        workspace.UpdatedAt = DateTime.UtcNow;
        var path = Path.Combine(_workspacesDir, $"{workspace.Id}.json");
        var json = JsonSerializer.Serialize(workspace, JsonDefaults.Options);
        await AtomicFileWriter.WriteAsync(path, json);
    }

    public async Task DeleteWorkspaceAsync(string workspaceId)
    {
        var path = Path.Combine(_workspacesDir, $"{workspaceId}.json");
        if (File.Exists(path))
            File.Delete(path);
        await Task.CompletedTask;
    }

    public async Task<Workspace> CreateFromUrlAsync(string url, string name, string model, IProgress<string>? progress = null, CancellationToken ct = default)
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
                await _gitService.FetchAsync(repoDir, ct);
            }
            else
            {
                var slug = ExtractRepoSlug(url);
                var reposDir = await GetReposDirAsync();
                repoDir = Path.Combine(reposDir, slug);

                // Avoid directory name collision
                var counter = 1;
                var originalDir = repoDir;
                while (Directory.Exists(repoDir))
                {
                    repoDir = $"{originalDir}-{counter++}";
                }

                progress?.Report("Cloning repository...");
                var cloneResult = await _gitService.CloneAsync(url, repoDir, progress, ct);
                if (!cloneResult.Success)
                {
                    workspace.Status = WorkspaceStatus.Error;
                    workspace.Error = AppError.CloneFailed(cloneResult.Error);
                    await SaveWorkspaceAsync(workspace);
                    return workspace;
                }

                // Save repo info
                var defaultBranch = await _gitService.DetectDefaultBranchAsync(repoDir) ?? "main";
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
            _logger.LogInformation("Workspace {Name} created from {Url}", name, url);
            return workspace;
        }
        catch (OperationCanceledException)
        {
            await DeleteWorkspaceAsync(workspace.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workspace from URL: {Url}", url);
            workspace.Status = WorkspaceStatus.Error;
            workspace.Error = AppError.FromException(ErrorCode.Unknown, ex);
            await SaveWorkspaceAsync(workspace);
            return workspace;
        }
    }

    public async Task<Workspace> CreateFromLocalAsync(string localPath, string name, string model, CancellationToken ct = default)
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
            if (!await _gitService.IsGitRepoAsync(localPath))
            {
                workspace.Status = WorkspaceStatus.Error;
                workspace.Error = AppError.InvalidGitRepo("Not a valid git repository.");
                await SaveWorkspaceAsync(workspace);
                return workspace;
            }

            workspace.Status = WorkspaceStatus.Ready;
            workspace.Error = null;
            await SaveWorkspaceAsync(workspace);
            _logger.LogInformation("Workspace {Name} created from local path {Path}", name, localPath);
            return workspace;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workspace from local path: {Path}", localPath);
            workspace.Status = WorkspaceStatus.Error;
            workspace.Error = AppError.FromException(ErrorCode.Unknown, ex);
            await SaveWorkspaceAsync(workspace);
            return workspace;
        }
    }

    public async Task<GitRepoInfo?> FindExistingRepoAsync(string remoteUrl)
    {
        var normalizedUrl = NormalizeUrl(remoteUrl);

        if (!Directory.Exists(_repoInfoDir))
            return null;

        foreach (var file in Directory.GetFiles(_repoInfoDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var info = JsonSerializer.Deserialize<GitRepoInfo>(json, JsonDefaults.Options);
                if (info != null && NormalizeUrl(info.RemoteUrl) == normalizedUrl && Directory.Exists(info.LocalPath))
                    return info;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to read repo info file: {File}", file); }
        }

        return null;
    }

    private async Task SaveRepoInfoAsync(GitRepoInfo repoInfo)
    {
        var path = Path.Combine(_repoInfoDir, $"{repoInfo.Id}.json");
        var json = JsonSerializer.Serialize(repoInfo, JsonDefaults.Options);
        await AtomicFileWriter.WriteAsync(path, json);
    }

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

    [GeneratedRegex(@"[^a-zA-Z0-9\-_.]")]
    private static partial Regex SlugRegex();
}
