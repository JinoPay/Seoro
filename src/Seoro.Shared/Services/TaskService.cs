using Seoro.Shared.Models;
using Seoro.Shared.Services.Migration;
using Microsoft.Extensions.Logging;
using TaskStatus = Seoro.Shared.Models.TaskStatus;

namespace Seoro.Shared.Services;

public class TaskService(ILogger<TaskService> logger) : ITaskService
{
    private readonly string _tasksDir = AppPaths.Tasks;

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

    public async Task UpdateStatusAsync(string taskId, TaskStatus status)
    {
        var task = await GetAsync(taskId);
        if (task == null) return;

        task.Status = status;
        task.UpdatedAt = DateTime.UtcNow;

        var path = Path.Combine(_tasksDir, task.SessionId, $"{task.Id}.json");
        var json = MigratingJsonWriter.Write(task, JsonDefaults.Options);
        await AtomicFileWriter.WriteAsync(path, json);
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

    public Task<List<TaskItem>> GetBySessionAsync(string sessionId)
    {
        var sessionDir = Path.Combine(_tasksDir, sessionId);
        return LoadTasksFromDirAsync(sessionDir);
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

    private async Task<List<TaskItem>> LoadTasksFromDirAsync(string dir)
    {
        var tasks = new List<TaskItem>();
        if (!Directory.Exists(dir)) return tasks;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
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
            catch (Exception ex)
            {
                logger.LogWarning(ex, "손상된 작업 파일 건너뜀: {File}", file);
            }

        return tasks.OrderBy(t => t.CreatedAt).ToList();
    }
}