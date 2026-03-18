using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cominomi.Shared;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class UsageService : IUsageService
{
    private readonly ILogger<UsageService> _logger;
    private readonly string _usageFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly HashSet<string> _seenHashes = new();
    private bool _hashesLoaded;

    /// <summary>
    /// Maximum file size before automatic rotation (10 MB).
    /// </summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Entries older than this are removed during rotation.
    /// </summary>
    private const int DefaultRetentionDays = 90;

    public UsageService(ILogger<UsageService> logger)
    {
        _logger = logger;
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CominomiConstants.AppName);
        Directory.CreateDirectory(appDataDir);
        _usageFilePath = Path.Combine(appDataDir, "usage.jsonl");
    }

    public decimal CalculateCost(string model, long inputTokens, long outputTokens, long cacheCreationTokens, long cacheReadTokens)
    {
        var pricing = ModelDefinitions.GetPricing(model);
        if (pricing == null) return 0m;

        return (inputTokens * pricing.Input / 1_000_000m)
             + (outputTokens * pricing.Output / 1_000_000m)
             + (cacheCreationTokens * pricing.CacheWrite / 1_000_000m)
             + (cacheReadTokens * pricing.CacheRead / 1_000_000m);
    }

    public async Task RecordUsageAsync(UsageEntry entry)
    {
        var hashInput = $"{entry.SessionId}|{entry.Timestamp:O}|{entry.InputTokens}|{entry.OutputTokens}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..16];

        await _writeLock.WaitAsync();
        try
        {
            // Load existing hashes on first write to survive restarts
            if (!_hashesLoaded)
            {
                await LoadExistingHashesAsync();
                _hashesLoaded = true;
            }

            if (!_seenHashes.Add(hash))
                return;

            var json = JsonSerializer.Serialize(entry);
            await AtomicFileWriter.AppendAsync(_usageFilePath, json + Environment.NewLine);

            // Auto-rotate if file is too large
            await RotateIfNeededAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write usage entry");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<UsageStats> GetStatsAsync(int? days = null)
    {
        var cutoff = days.HasValue ? DateTime.UtcNow.AddDays(-days.Value) : DateTime.MinValue;
        var entries = await ReadEntriesAsync();
        var filtered = entries.Where(e => e.Timestamp >= cutoff).ToList();
        return Aggregate(filtered);
    }

    public async Task<UsageStats> GetStatsByDateRangeAsync(DateTime start, DateTime end)
    {
        var entries = await ReadEntriesAsync();
        var filtered = entries.Where(e => e.Timestamp >= start && e.Timestamp <= end).ToList();
        return Aggregate(filtered);
    }

    public async Task<string> ExportCsvAsync(int? days = null)
    {
        var cutoff = days.HasValue ? DateTime.UtcNow.AddDays(-days.Value) : DateTime.MinValue;
        var entries = await ReadEntriesAsync();
        var filtered = entries.Where(e => e.Timestamp >= cutoff).OrderBy(e => e.Timestamp).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("timestamp,model,input_tokens,output_tokens,cache_creation_tokens,cache_read_tokens,cost_usd,session_id,project_path");

        foreach (var e in filtered)
        {
            sb.AppendLine(string.Join(",",
                e.Timestamp.ToString("O"),
                CsvEscape(e.Model),
                e.InputTokens,
                e.OutputTokens,
                e.CacheCreationTokens,
                e.CacheReadTokens,
                e.CostUsd.ToString("F6"),
                CsvEscape(e.SessionId),
                CsvEscape(e.ProjectPath)));
        }

        return sb.ToString();
    }

    public async Task<int> PurgeOldEntriesAsync(int retentionDays = DefaultRetentionDays)
    {
        await _writeLock.WaitAsync();
        try
        {
            return await RotateCoreAsync(retentionDays);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // --- private helpers ---

    private async Task LoadExistingHashesAsync()
    {
        if (!File.Exists(_usageFilePath))
            return;

        try
        {
            var lines = await File.ReadAllLinesAsync(_usageFilePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<UsageEntry>(line);
                    if (entry != null)
                    {
                        var hashInput = $"{entry.SessionId}|{entry.Timestamp:O}|{entry.InputTokens}|{entry.OutputTokens}";
                        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..16];
                        _seenHashes.Add(hash);
                    }
                }
                catch
                {
                    // Skip malformed lines
                }
            }

            _logger.LogInformation("Loaded {Count} usage hashes for deduplication", _seenHashes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load existing usage hashes");
        }
    }

    private async Task RotateIfNeededAsync()
    {
        try
        {
            var fileInfo = new FileInfo(_usageFilePath);
            if (!fileInfo.Exists || fileInfo.Length < MaxFileSizeBytes)
                return;

            var purged = await RotateCoreAsync(DefaultRetentionDays);
            if (purged > 0)
                _logger.LogInformation("Auto-rotated usage log: removed {Count} old entries", purged);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate usage log");
        }
    }

    /// <summary>
    /// Removes entries older than retentionDays, rewrites the file atomically,
    /// and rebuilds the dedup hash set. Returns count of removed entries.
    /// Must be called under _writeLock.
    /// </summary>
    private async Task<int> RotateCoreAsync(int retentionDays)
    {
        var entries = await ReadEntriesInternalAsync();
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var kept = entries.Where(e => e.Timestamp >= cutoff).ToList();
        var removedCount = entries.Count - kept.Count;

        if (removedCount == 0)
            return 0;

        // Rewrite file atomically
        var sb = new StringBuilder();
        foreach (var entry in kept)
            sb.AppendLine(JsonSerializer.Serialize(entry));

        await AtomicFileWriter.WriteAsync(_usageFilePath, sb.ToString());

        // Rebuild hash set with only kept entries
        _seenHashes.Clear();
        foreach (var entry in kept)
        {
            var hashInput = $"{entry.SessionId}|{entry.Timestamp:O}|{entry.InputTokens}|{entry.OutputTokens}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..16];
            _seenHashes.Add(hash);
        }

        return removedCount;
    }

    private async Task<List<UsageEntry>> ReadEntriesAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            return await ReadEntriesInternalAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Reads entries without acquiring lock. Caller must hold _writeLock.
    /// </summary>
    private async Task<List<UsageEntry>> ReadEntriesInternalAsync()
    {
        var entries = new List<UsageEntry>();
        if (!File.Exists(_usageFilePath))
            return entries;

        try
        {
            var lines = await File.ReadAllLinesAsync(_usageFilePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<UsageEntry>(line);
                    if (entry != null)
                        entries.Add(entry);
                }
                catch
                {
                    // Skip malformed lines
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read usage file");
        }

        return entries;
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static UsageStats Aggregate(List<UsageEntry> entries)
    {
        var stats = new UsageStats();
        if (entries.Count == 0) return stats;

        var sessionIds = new HashSet<string>();
        var modelMap = new Dictionary<string, ModelUsage>();
        var dateMap = new Dictionary<string, DailyUsage>();
        var projectMap = new Dictionary<string, ProjectUsage>();

        foreach (var e in entries)
        {
            stats.TotalCost += e.CostUsd;
            stats.TotalInputTokens += e.InputTokens;
            stats.TotalOutputTokens += e.OutputTokens;
            stats.TotalCacheCreationTokens += e.CacheCreationTokens;
            stats.TotalCacheReadTokens += e.CacheReadTokens;
            sessionIds.Add(e.SessionId);

            // By model
            if (!modelMap.TryGetValue(e.Model, out var mu))
            {
                mu = new ModelUsage { Model = e.Model };
                modelMap[e.Model] = mu;
            }
            mu.TotalCost += e.CostUsd;
            mu.InputTokens += e.InputTokens;
            mu.OutputTokens += e.OutputTokens;
            mu.CacheCreationTokens += e.CacheCreationTokens;
            mu.CacheReadTokens += e.CacheReadTokens;
            mu.TotalTokens += e.InputTokens + e.OutputTokens;

            // By date
            var dateKey = e.Timestamp.ToString("yyyy-MM-dd");
            if (!dateMap.TryGetValue(dateKey, out var du))
            {
                du = new DailyUsage { Date = dateKey };
                dateMap[dateKey] = du;
            }
            du.TotalCost += e.CostUsd;
            du.TotalTokens += e.InputTokens + e.OutputTokens;
            if (!du.ModelsUsed.Contains(e.Model))
                du.ModelsUsed.Add(e.Model);

            // By project
            if (!string.IsNullOrEmpty(e.ProjectPath))
            {
                if (!projectMap.TryGetValue(e.ProjectPath, out var pu))
                {
                    pu = new ProjectUsage
                    {
                        ProjectPath = e.ProjectPath,
                        ProjectName = Path.GetFileName(e.ProjectPath.TrimEnd('/', '\\'))
                    };
                    projectMap[e.ProjectPath] = pu;
                }
                pu.TotalCost += e.CostUsd;
                pu.TotalTokens += e.InputTokens + e.OutputTokens;
                if (string.Compare(e.Timestamp.ToString("O"), pu.LastUsed, StringComparison.Ordinal) > 0)
                    pu.LastUsed = e.Timestamp.ToString("O");
            }
        }

        stats.TotalTokens = stats.TotalInputTokens + stats.TotalOutputTokens;
        stats.TotalSessions = sessionIds.Count;

        // Count sessions per model/project
        var sessionModels = entries.GroupBy(e => e.SessionId).ToDictionary(g => g.Key, g => g.Select(e => e.Model).Distinct().ToList());
        foreach (var (_, models) in sessionModels)
            foreach (var m in models)
                if (modelMap.TryGetValue(m, out var mm))
                    mm.SessionCount++;

        var sessionProjects = entries.GroupBy(e => e.SessionId).ToDictionary(g => g.Key, g => g.Select(e => e.ProjectPath).Distinct().ToList());
        foreach (var (_, projects) in sessionProjects)
            foreach (var p in projects)
                if (!string.IsNullOrEmpty(p) && projectMap.TryGetValue(p, out var pp))
                    pp.SessionCount++;

        stats.ByModel = modelMap.Values.OrderByDescending(m => m.TotalCost).ToList();
        stats.ByDate = dateMap.Values.OrderBy(d => d.Date).ToList();
        stats.ByProject = projectMap.Values.OrderByDescending(p => p.TotalCost).ToList();

        return stats;
    }
}
