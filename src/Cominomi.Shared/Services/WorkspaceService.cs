using System.Text.Json;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class WorkspaceService : IWorkspaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _workspacesDir;

    public WorkspaceService()
    {
        _workspacesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cominomi", "workspaces");
        Directory.CreateDirectory(_workspacesDir);
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

        return workspaces.OrderBy(w => w.CreatedAt).ToList();
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

    public Task DeleteWorkspaceAsync(string workspaceId)
    {
        var path = Path.Combine(_workspacesDir, $"{workspaceId}.json");
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task EnsureDefaultWorkspaceAsync()
    {
        var path = Path.Combine(_workspacesDir, "default.json");
        if (File.Exists(path))
            return;

        var defaultWorkspace = new Workspace
        {
            Id = "default",
            Name = "Default",
            DefaultWorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        var json = JsonSerializer.Serialize(defaultWorkspace, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }
}
