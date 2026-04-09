
namespace Seoro.Shared.Services.Knowledge;

public interface IMemoryService
{
    string BuildMemoryPrompt(IEnumerable<MemoryEntry> entries);
    Task DeleteAsync(string entryId);
    Task SaveAsync(MemoryEntry entry);
    Task<List<MemoryEntry>> GetAllAsync();
    Task<List<MemoryEntry>> GetByTypeAsync(MemoryType type);
    Task<List<MemoryEntry>> GetForWorkspaceAsync(string? workspaceId);
    Task<List<MemoryEntry>> SearchAsync(string query, string? workspaceId = null);
    Task<MemoryEntry?> FindAsync(string entryId);
}