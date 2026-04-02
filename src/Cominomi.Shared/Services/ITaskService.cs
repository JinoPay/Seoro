using Cominomi.Shared.Models;
using TaskStatus = Cominomi.Shared.Models.TaskStatus;

namespace Cominomi.Shared.Services;

public interface ITaskService
{
    Task DeleteAsync(string taskId);
    Task UpdateStatusAsync(string taskId, TaskStatus status);
    Task<List<TaskItem>> GetAllAsync();
    Task<List<TaskItem>> GetBySessionAsync(string sessionId);
    Task<TaskItem?> GetAsync(string taskId);
    Task<TaskItem> CreateAsync(string sessionId, string subject, string description = "");
}