using System.Text;
using System.Text.Json;
using Cominomi.Shared;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services.Migration;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class MemoryService : IMemoryService, IDisposable
{
    private readonly ILogger<MemoryService> _logger;
    private readonly string _memoryDir = AppPaths.Memory;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private List<MemoryEntry>? _cache;
    private FileSystemWatcher? _watcher;

    public MemoryService(ILogger<MemoryService> logger)
    {
        _logger = logger;
        InitializeWatcher();
    }

    private void InitializeWatcher()
    {
        try
        {
            if (!Directory.Exists(_memoryDir))
                Directory.CreateDirectory(_memoryDir);

            _watcher = new FileSystemWatcher(_memoryDir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += (_, _) => InvalidateCache();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize memory file watcher");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e) => InvalidateCache();

    private void InvalidateCache()
    {
        Volatile.Write(ref _cache, null);
    }

    private async Task<List<MemoryEntry>> GetCachedEntriesAsync()
    {
        var cached = Volatile.Read(ref _cache);
        if (cached != null)
            return cached;

        await _cacheLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            cached = Volatile.Read(ref _cache);
            if (cached != null)
                return cached;

            var entries = await LoadAllFromDiskAsync();
            Volatile.Write(ref _cache, entries);
            return entries;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<List<MemoryEntry>> LoadAllFromDiskAsync()
    {
        var entries = new List<MemoryEntry>();

        if (!Directory.Exists(_memoryDir))
            return entries;

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

    public async Task<List<MemoryEntry>> GetAllAsync()
    {
        var entries = await GetCachedEntriesAsync();
        return entries.ToList();
    }

    public async Task<List<MemoryEntry>> GetForWorkspaceAsync(string? workspaceId)
    {
        var all = await GetCachedEntriesAsync();
        return all.Where(e => e.WorkspaceId == null || e.WorkspaceId == workspaceId).ToList();
    }

    public async Task<List<MemoryEntry>> GetByTypeAsync(MemoryType type)
    {
        var all = await GetCachedEntriesAsync();
        return all.Where(e => e.Type == type).ToList();
    }

    public async Task<List<MemoryEntry>> SearchAsync(string query, string? workspaceId = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return workspaceId != null ? await GetForWorkspaceAsync(workspaceId) : await GetAllAsync();

        var all = await GetCachedEntriesAsync();
        var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var results = all.Where(e =>
        {
            // Workspace filter
            if (workspaceId != null && e.WorkspaceId != null && e.WorkspaceId != workspaceId)
                return false;

            // All keywords must match at least one field
            foreach (var keyword in keywords)
            {
                var found = e.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                         || e.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                         || e.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                if (!found) return false;
            }
            return true;
        });

        return results.ToList();
    }

    public async Task SaveAsync(MemoryEntry entry)
    {
        entry.UpdatedAt = DateTime.UtcNow;
        var path = Path.Combine(_memoryDir, $"{entry.Id}.json");
        var json = MigratingJsonWriter.Write(entry, JsonDefaults.Options);
        await AtomicFileWriter.WriteAsync(path, json);
        // Watcher will invalidate cache automatically
    }

    public Task DeleteAsync(string entryId)
    {
        var path = Path.Combine(_memoryDir, $"{entryId}.json");
        if (File.Exists(path))
            File.Delete(path);
        // Watcher will invalidate cache automatically
        return Task.CompletedTask;
    }

    public async Task<MemoryEntry?> FindAsync(string entryId)
    {
        // Try cache first for fast lookup
        var cached = Volatile.Read(ref _cache);
        if (cached != null)
        {
            var fromCache = cached.FirstOrDefault(e => e.Id == entryId);
            if (fromCache != null)
                return fromCache;
        }

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
        var maxEntryTokens = CominomiConstants.MaxMemoryEntryTokens;
        var maxTotalTokens = CominomiConstants.MaxMemoryPromptTokens;
        sb.AppendLine("## Persistent Memory");

        foreach (var group in entries.GroupBy(e => e.Type))
        {
            if (TokenEstimator.Estimate(sb.ToString()) >= maxTotalTokens) break;
            sb.AppendLine($"\n### {group.Key} Memory");
            foreach (var entry in group)
            {
                if (TokenEstimator.Estimate(sb.ToString()) >= maxTotalTokens) break;
                var content = TokenEstimator.Truncate(entry.Content, maxEntryTokens);
                sb.AppendLine($"- **{entry.Name}**: {content}");
            }
        }

        var result = sb.ToString();
        return TokenEstimator.Truncate(result, maxTotalTokens);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _cacheLock.Dispose();
    }
}
