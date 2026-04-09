using Seoro.Shared.Models.Knowledge;
using TaskStatus = Seoro.Shared.Models.Knowledge.TaskStatus;

namespace Seoro.Shared.Services.Knowledge;

public interface ITaskService
{
    Task DeleteAsync(string taskId);
    Task UpdateStatusAsync(string taskId, TaskStatus status);
    Task<List<TaskItem>> GetAllAsync();
    Task<List<TaskItem>> GetBySessionAsync(string sessionId);
    Task<TaskItem?> GetAsync(string taskId);
    Task<TaskItem> CreateAsync(string sessionId, string subject, string description = "");
}