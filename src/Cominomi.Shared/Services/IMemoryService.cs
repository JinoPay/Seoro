using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IMemoryService
{
    Task<List<MemoryEntry>> GetAllAsync();
    Task<List<MemoryEntry>> GetForWorkspaceAsync(string? workspaceId);
    Task<List<MemoryEntry>> GetByTypeAsync(MemoryType type);
    Task SaveAsync(MemoryEntry entry);
    Task DeleteAsync(string entryId);
    Task<MemoryEntry?> FindAsync(string entryId);
    string BuildMemoryPrompt(IEnumerable<MemoryEntry> entries);
}
