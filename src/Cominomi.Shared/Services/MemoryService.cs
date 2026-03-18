using System.Text;
using System.Text.Json;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services.Migration;
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
                var (entry, migrated, migratedJson) = MigratingJsonReader.Read<MemoryEntry>(json, JsonDefaults.Options);
                if (entry != null)
                {
                    entries.Add(entry);
                    if (migrated && migratedJson != null)
                        await AtomicFileWriter.WriteAsync(file, migratedJson);
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Skipping corrupted memory file: {File}", file); }
        }

        return entries.OrderByDescending(e => e.UpdatedAt).ToList();
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
        var json = MigratingJsonWriter.Write(entry, JsonDefaults.Options);
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
        var (entry, migrated, migratedJson) = MigratingJsonReader.Read<MemoryEntry>(json, JsonDefaults.Options);
        if (migrated && migratedJson != null)
            await AtomicFileWriter.WriteAsync(path, migratedJson);
        return entry;
    }

    public string BuildMemoryPrompt(IEnumerable<MemoryEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Persistent Memory");

        foreach (var group in entries.GroupBy(e => e.Type))
        {
            sb.AppendLine($"\n### {group.Key} Memory");
            foreach (var entry in group)
            {
                sb.AppendLine($"- **{entry.Name}**: {entry.Content}");
            }
        }

        return sb.ToString();
    }
}
