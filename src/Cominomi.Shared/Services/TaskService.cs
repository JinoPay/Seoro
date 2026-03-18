using System.Text.Json;
using Cominomi.Shared;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services.Migration;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class TaskService : ITaskService
{
    private readonly ILogger<TaskService> _logger;
    private readonly string _tasksDir = AppPaths.Tasks;

    public TaskService(ILogger<TaskService> logger)
    {
        _logger = logger;
    }

    public Task<List<TaskItem>> GetBySessionAsync(string sessionId)
    {
        var sessionDir = Path.Combine(_tasksDir, sessionId);
        return LoadTasksFromDirAsync(sessionDir);
    }

    public async Task<TaskItem> CreateAsync(string sessionId, string subject, string description = "")
    {
        Guard.NotNullOrWhiteSpace(sessionId, nameof(sessionId));
        Guard.NotNullOrWhiteSpace(subject, nameof(subject));

        var task = new TaskItem
        {
            SessionId = sessionId,
            Subject = subject,
            Description = description
        };

        var sessionDir = Path.Combine(_tasksDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var path = Path.Combine(sessionDir, $"{task.Id}.json");
        var json = MigratingJsonWriter.Write(task, JsonDefaults.Options);
        await AtomicFileWriter.WriteAsync(path, json);

        return task;
    }

    public async Task<TaskItem?> GetAsync(string taskId)
    {
        foreach (var dir in Directory.GetDirectories(_tasksDir))
        {
            var path = Path.Combine(dir, $"{taskId}.json");
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                var (task, migrated, migratedJson) = MigratingJsonReader.Read<TaskItem>(json, JsonDefaults.Options);
                if (migrated && migratedJson != null)
                    await AtomicFileWriter.WriteAsync(path, migratedJson);
                return task;
            }
        }
        return null;
    }

    public async Task UpdateStatusAsync(string taskId, Models.TaskStatus status)
    {
        var task = await GetAsync(taskId);
        if (task == null) return;

        task.Status = status;
        task.UpdatedAt = DateTime.UtcNow;

        var path = Path.Combine(_tasksDir, task.SessionId, $"{task.Id}.json");
        var json = MigratingJsonWriter.Write(task, JsonDefaults.Options);
        await AtomicFileWriter.WriteAsync(path, json);
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

    private async Task<List<TaskItem>> LoadTasksFromDirAsync(string dir)
    {
        var tasks = new List<TaskItem>();
        if (!Directory.Exists(dir)) return tasks;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var (task, migrated, migratedJson) = MigratingJsonReader.Read<TaskItem>(json, JsonDefaults.Options);
                if (task != null)
                {
                    tasks.Add(task);
                    if (migrated && migratedJson != null)
                        await AtomicFileWriter.WriteAsync(file, migratedJson);
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Skipping corrupted task file: {File}", file); }
        }

        return tasks.OrderBy(t => t.CreatedAt).ToList();
    }
}
