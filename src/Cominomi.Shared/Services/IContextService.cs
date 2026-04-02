using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IContextService
{
    string BuildContextPrompt(ContextInfo context);
    Task ArchiveContextAsync(string worktreePath, string archivePath);
    Task DeletePlanAsync(string worktreePath, string planName);
    Task EnsureContextDirectoryAsync(string worktreePath);
    Task SaveNotesAsync(string worktreePath, string content);
    Task SavePlanAsync(string worktreePath, string planName, string content);
    Task SaveTodosAsync(string worktreePath, string content);
    Task<ContextInfo> LoadContextAsync(string worktreePath);
    Task<List<PlanFile>> GetPlansAsync(string worktreePath);
}