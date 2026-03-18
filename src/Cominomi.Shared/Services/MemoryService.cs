using System.Text;
using System.Text.Json;
using Cominomi.Shared;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class MemoryService : IMemoryService
{
    private readonly ILogger<MemoryService> _logger;
    private readonly string _memoryDir = AppPaths.Memory;

    public MemoryService(ILogger<MemoryService> logger)
    {
        _logger = logger;
    }

    public async Task<List<MemoryEntry>> GetAllAsync()
    {
        var entries = new List<MemoryEntry>();

        foreach (var file in Directory.GetFiles(_memoryDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var entry = JsonSerializer.Deserialize<MemoryEntry>(json, JsonDefaults.Options);
                if (entry != null)
                    entries.Add(entry);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Skipping corrupted memory file: {File}", file); }
        }

        return entries.OrderByDescending(e => e.UpdatedAt).ToList();
    }

    public async Task<List<MemoryEntry>> GetForWorkspaceAsync(string? workspaceId)
    {
        var all = await GetAllAsync();
        return all.Where(e => e.WorkspaceId == null || e.WorkspaceId == workspaceId).ToList();
    }

    public async Task<List<MemoryEntry>> GetByTypeAsync(MemoryType type)
    {
        var all = await GetAllAsync();
        return all.Where(e => e.Type == type).ToList();
    }

    public async Task SaveAsync(MemoryEntry entry)
    {
        entry.UpdatedAt = DateTime.UtcNow;
        var path = Path.Combine(_memoryDir, $"{entry.Id}.json");
        var json = JsonSerializer.Serialize(entry, JsonDefaults.Options);
        await AtomicFileWriter.WriteAsync(path, json);
    }

    public Task DeleteAsync(string entryId)
    {
        var path = Path.Combine(_memoryDir, $"{entryId}.json");
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<MemoryEntry?> FindAsync(string entryId)
    {
        var path = Path.Combine(_memoryDir, $"{entryId}.json");
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<MemoryEntry>(json, JsonDefaults.Options);
    }

    public string BuildMemoryPrompt(IEnumerable<MemoryEntry> entries)
    {
        var sb = new StringBuilder();
        var maxEntry = CominomiConstants.MaxMemoryEntryChars;
        var maxTotal = CominomiConstants.MaxMemoryPromptChars;
        sb.AppendLine("## Persistent Memory");

        foreach (var group in entries.GroupBy(e => e.Type))
        {
            if (sb.Length >= maxTotal) break;
            sb.AppendLine($"\n### {group.Key} Memory");
            foreach (var entry in group)
            {
                if (sb.Length >= maxTotal) break;
                var content = entry.Content.Length > maxEntry
                    ? entry.Content[..maxEntry] + string.Format(CominomiConstants.TruncationMarker, entry.Content.Length)
                    : entry.Content;
                sb.AppendLine($"- **{entry.Name}**: {content}");
            }
        }

        var result = sb.ToString();
        return result.Length <= maxTotal
            ? result
            : result[..maxTotal] + string.Format(CominomiConstants.TruncationMarker, result.Length);
    }
}
