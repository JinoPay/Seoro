using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

public class UsageEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("cache_creation_tokens")]
    public long CacheCreationTokens { get; set; }

    [JsonPropertyName("cache_read_tokens")]
    public long CacheReadTokens { get; set; }

    [JsonPropertyName("cost")]
    public decimal CostUsd { get; set; }

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("project_path")]
    public string ProjectPath { get; set; } = string.Empty;
}

public class UsageStats
{
    public decimal TotalCost { get; set; }
    public long TotalTokens { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheCreationTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public int TotalSessions { get; set; }
    public List<ModelUsage> ByModel { get; set; } = [];
    public List<DailyUsage> ByDate { get; set; } = [];
    public List<ProjectUsage> ByProject { get; set; } = [];
}

public class ModelUsage
{
    public string Model { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public long TotalTokens { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public int SessionCount { get; set; }
}

public class DailyUsage
{
    public string Date { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public long TotalTokens { get; set; }
    public List<string> ModelsUsed { get; set; } = [];
}

public class ProjectUsage
{
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public long TotalTokens { get; set; }
    public int SessionCount { get; set; }
    public string LastUsed { get; set; } = string.Empty;
}
