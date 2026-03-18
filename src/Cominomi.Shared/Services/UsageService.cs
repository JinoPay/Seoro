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
        // Deduplication
        var hashInput = $"{entry.SessionId}|{entry.Timestamp:O}|{entry.InputTokens}|{entry.OutputTokens}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..16];

        if (!_seenHashes.Add(hash))
            return;

        var json = JsonSerializer.Serialize(entry);

        await _writeLock.WaitAsync();
        try
        {
            await AtomicFileWriter.AppendAsync(_usageFilePath, json + Environment.NewLine);
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

    private async Task<List<UsageEntry>> ReadEntriesAsync()
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
