using System.Text.Json;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class TaskService : ITaskService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<TaskService> _logger;
    private readonly string _tasksDir;

    public TaskService(ILogger<TaskService> logger)
    {
        _logger = logger;
        _tasksDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cominomi", "tasks");
        Directory.CreateDirectory(_tasksDir);
    }

    public Task<List<TaskItem>> GetBySessionAsync(string sessionId)
    {
        var sessionDir = Path.Combine(_tasksDir, sessionId);
        return LoadTasksFromDirAsync(sessionDir);
    }

    public async Task<TaskItem> CreateAsync(string sessionId, string subject, string description = "")
    {
        var task = new TaskItem
        {
            SessionId = sessionId,
            Subject = subject,
            Description = description
        };

        var sessionDir = Path.Combine(_tasksDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var path = Path.Combine(sessionDir, $"{task.Id}.json");
        var json = JsonSerializer.Serialize(task, JsonOptions);
        await File.WriteAllTextAsync(path, json);

        return task;
    }

    public Task<TaskItem?> GetAsync(string taskId)
    {
        foreach (var dir in Directory.GetDirectories(_tasksDir))
        {
            var path = Path.Combine(dir, $"{taskId}.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return Task.FromResult(JsonSerializer.Deserialize<TaskItem>(json, JsonOptions));
            }
        }
        return Task.FromResult<TaskItem?>(null);
    }

    public async Task UpdateStatusAsync(string taskId, Models.TaskStatus status)
    {
        var task = await GetAsync(taskId);
        if (task == null) return;

        task.Status = status;
        task.UpdatedAt = DateTime.UtcNow;

        var path = Path.Combine(_tasksDir, task.SessionId, $"{task.Id}.json");
        var json = JsonSerializer.Serialize(task, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public Task DeleteAsync(string taskId)
    {
        foreach (var dir in Directory.GetDirectories(_tasksDir))
        {
            var path = Path.Combine(dir, $"{taskId}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                break;
            }
        }
        return Task.CompletedTask;
    }

    public async Task<List<TaskItem>> GetAllAsync()
    {
        var all = new List<TaskItem>();
        if (!Directory.Exists(_tasksDir)) return all;

        foreach (var dir in Directory.GetDirectories(_tasksDir))
        {
            var tasks = await LoadTasksFromDirAsync(dir);
            all.AddRange(tasks);
        }

        return all.OrderByDescending(t => t.UpdatedAt).ToList();
    }

    private Task<List<TaskItem>> LoadTasksFromDirAsync(string dir)
    {
        var tasks = new List<TaskItem>();
        if (!Directory.Exists(dir)) return Task.FromResult(tasks);

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var task = JsonSerializer.Deserialize<TaskItem>(json, JsonOptions);
                if (task != null)
                    tasks.Add(task);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Skipping corrupted task file: {File}", file); }
        }

        return Task.FromResult(tasks.OrderBy(t => t.CreatedAt).ToList());
    }
}
