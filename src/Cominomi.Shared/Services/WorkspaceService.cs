using System.Text.Json;
using System.Text.RegularExpressions;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public partial class WorkspaceService : IWorkspaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IGitService _gitService;
    private readonly ISettingsService _settingsService;
    private readonly string _workspacesDir;
    private readonly string _repoInfoDir;

    public WorkspaceService(IGitService gitService, ISettingsService settingsService)
    {
        _gitService = gitService;
        _settingsService = settingsService;

        _workspacesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cominomi", "workspaces");
        _repoInfoDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cominomi", "repos");

        Directory.CreateDirectory(_workspacesDir);
        Directory.CreateDirectory(_repoInfoDir);
    }

    private async Task<string> GetBaseDirAsync()
    {
        var settings = await _settingsService.LoadAsync();
        var baseDir = !string.IsNullOrWhiteSpace(settings.DefaultCloneDirectory)
            ? settings.DefaultCloneDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Cominomi");
        return baseDir;
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
                var workspace = JsonSerializer.Deserialize<Workspace>(json, JsonOptions);
                if (workspace != null)
                    workspaces.Add(workspace);
            }
            catch
            {
                // skip corrupted files
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
        return JsonSerializer.Deserialize<Workspace>(json, JsonOptions);
    }

    public async Task SaveWorkspaceAsync(Workspace workspace)
    {
        workspace.UpdatedAt = DateTime.UtcNow;
        var path = Path.Combine(_workspacesDir, $"{workspace.Id}.json");
        var json = JsonSerializer.Serialize(workspace, JsonOptions);
        await File.WriteAllTextAsync(path, json);
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
                await RunGitFetchAsync(repoDir, ct);
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
                    workspace.ErrorMessage = cloneResult.Error;
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
            workspace.ErrorMessage = null;
            await SaveWorkspaceAsync(workspace);
            progress?.Report("Workspace ready!");
            return workspace;
        }
        catch (OperationCanceledException)
        {
            await DeleteWorkspaceAsync(workspace.Id);
            throw;
        }
        catch (Exception ex)
        {
            workspace.Status = WorkspaceStatus.Error;
            workspace.ErrorMessage = ex.Message;
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
                workspace.ErrorMessage = "Not a valid git repository.";
                await SaveWorkspaceAsync(workspace);
                return workspace;
            }

            workspace.Status = WorkspaceStatus.Ready;
            workspace.ErrorMessage = null;
            await SaveWorkspaceAsync(workspace);
            return workspace;
        }
        catch (Exception ex)
        {
            workspace.Status = WorkspaceStatus.Error;
            workspace.ErrorMessage = ex.Message;
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
                var info = JsonSerializer.Deserialize<GitRepoInfo>(json, JsonOptions);
                if (info != null && NormalizeUrl(info.RemoteUrl) == normalizedUrl && Directory.Exists(info.LocalPath))
                    return info;
            }
            catch { }
        }

        return null;
    }

    private async Task SaveRepoInfoAsync(GitRepoInfo repoInfo)
    {
        var path = Path.Combine(_repoInfoDir, $"{repoInfo.Id}.json");
        var json = JsonSerializer.Serialize(repoInfo, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    private static async Task RunGitFetchAsync(string repoDir, CancellationToken ct)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "fetch --all --prune",
                WorkingDirectory = repoDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Environment = { ["GIT_TERMINAL_PROMPT"] = "0" }
            }
        };
        process.Start();
        await process.StandardOutput.ReadToEndAsync(ct);
        await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        process.Dispose();
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
