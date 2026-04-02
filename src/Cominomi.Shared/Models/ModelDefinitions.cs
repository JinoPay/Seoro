using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

public record ModelInfo(string Id, string DisplayName)
{
    [JsonPropertyName("pricing")] public ModelPricing? Pricing { get; init; }

    [JsonPropertyName("contextWindow")] public int ContextWindow { get; init; } = 200_000;

    [JsonPropertyName("keywords")] public string[] Keywords { get; init; } = [];

    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>1 = slow/powerful, 2 = balanced, 3 = fast</summary>
    [JsonPropertyName("speedTier")]
    public int SpeedTier { get; init; } = 2;

    [JsonPropertyName("isAlias")] public bool IsAlias { get; init; }

    /// <summary>"standard", "extended", "hybrid", "alias"</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }
}

public record ModelPricing(
    [property: JsonPropertyName("input")] decimal Input,
    [property: JsonPropertyName("output")] decimal Output,
    [property: JsonPropertyName("cacheWrite")]
    decimal CacheWrite,
    [property: JsonPropertyName("cacheRead")]
    decimal CacheRead);

public record ModelConfig
{
    [JsonPropertyName("models")] public ModelInfo[] Models { get; init; } = [];

    [JsonPropertyName("defaultModelId")] public string DefaultModelId { get; init; } = "opus";

    [JsonPropertyName("defaultPricingFallbackId")]
    public string DefaultPricingFallbackId { get; init; } = "sonnet";
}

public static class ModelDefinitions
{
    private static ModelConfig _config = CreateDefaultConfig();
    public static ModelInfo Default => All.FirstOrDefault(m => m.Id == _config.DefaultModelId) ?? All[0];

    public static ModelInfo[] All => _config.Models;
    public static string PricingFallbackId => _config.DefaultPricingFallbackId;

    public static int GetContextWindow(string modelId)
    {
        var normalized = NormalizeModelId(modelId);
        var model = All.FirstOrDefault(m => m.Id == normalized);
        return model?.ContextWindow ?? 200_000;
    }

    public static bool SupportsMaxEffort(string modelId)
    {
        var normalized = NormalizeModelId(modelId);
        return normalized is "opus" or "opus[1m]" or "opusplan";
    }

    public static string GetMaxEffortLevel(string modelId)
    {
        var normalized = NormalizeModelId(modelId);
        return normalized is "opus" or "opus[1m]" or "opusplan" ? "max" : "high";
    }

    public static ModelPricing? GetPricing(string modelId)
    {
        var normalized = NormalizeModelId(modelId);
        var model = All.FirstOrDefault(m => m.Id == normalized);
        if (model?.Pricing != null) return model.Pricing;

        // Fallback
        return All.FirstOrDefault(m => m.Id == PricingFallbackId)?.Pricing;
    }

    public static string NormalizeModelId(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return Default.Id;
        if (All.Any(m => m.Id == modelId)) return modelId;

        // Keyword-based normalization
        foreach (var model in All)
        foreach (var keyword in model.Keywords)
            if (modelId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return model.Id;

        // Custom API model IDs (e.g. "claude-...") are kept as-is
        if (modelId.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
            return modelId;

        // Unknown short strings (e.g. "Model") — normalize to default
        return Default.Id;
    }

    /// <summary>
    ///     Load models from a JSON file. Falls back to built-in defaults if the file is missing or invalid.
    /// </summary>
    public static async Task LoadFromFileAsync(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var config = JsonSerializer.Deserialize<ModelConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (config?.Models is { Length: > 0 })
                _config = config;
        }
        catch
        {
            // keep built-in defaults
        }
    }

    /// <summary>Helper to retrieve only "real" (non-alias) models for popover display grouping.</summary>
    public static IEnumerable<IGrouping<string?, ModelInfo>> GetGroupedModels()
    {
        return All.GroupBy(m => m.Category);
    }

    private static ModelConfig CreateDefaultConfig()
    {
        return new ModelConfig
        {
            DefaultModelId = "opus",
            DefaultPricingFallbackId = "sonnet",
            Models =
            [
                // ── Standard ──
                new ModelInfo("opus", "Claude Opus 4.6")
                {
                    Keywords = ["opus"],
                    Pricing = new ModelPricing(5.0m, 25.0m, 6.25m, 0.50m),
                    Description = "최고 지능, 적응형 사고",
                    SpeedTier = 1,
                    Category = "standard"
                },
                new ModelInfo("sonnet", "Claude Sonnet 4.5")
                {
                    Keywords = ["sonnet"],
                    Pricing = new ModelPricing(3.0m, 15.0m, 3.75m, 0.30m),
                    Description = "속도와 지능의 균형",
                    SpeedTier = 2,
                    Category = "standard"
                },
                new ModelInfo("haiku", "Claude Haiku 4.5")
                {
                    Keywords = ["haiku"],
                    Pricing = new ModelPricing(1.0m, 5.0m, 1.25m, 0.10m),
                    Description = "빠르고 가벼운 모델",
                    SpeedTier = 3,
                    Category = "standard"
                },
                // ── Extended Context (1M) ──
                new ModelInfo("opus[1m]", "Opus (Extended 1M)")
                {
                    Keywords = ["opus[1m]"],
                    Pricing = new ModelPricing(5.0m, 25.0m, 6.25m, 0.50m),
                    ContextWindow = 1_000_000,
                    Description = "Opus + 1M 컨텍스트 윈도우",
                    SpeedTier = 1,
                    Category = "extended"
                },
                new ModelInfo("sonnet[1m]", "Sonnet (Extended 1M)")
                {
                    Keywords = ["sonnet[1m]"],
                    Pricing = new ModelPricing(3.0m, 15.0m, 3.75m, 0.30m),
                    ContextWindow = 1_000_000,
                    Description = "Sonnet + 1M 컨텍스트 윈도우",
                    SpeedTier = 2,
                    Category = "extended"
                },
                // ── Hybrid / Alias ──
                new ModelInfo("opusplan", "Opus Plan")
                {
                    Keywords = ["opusplan"],
                    Description = "Opus가 플래닝, Sonnet이 실행",
                    SpeedTier = 1,
                    IsAlias = true,
                    Category = "hybrid"
                },
                new ModelInfo("default", "Default")
                {
                    Keywords = ["default"],
                    Description = "Anthropic 권장 모델 자동 선택",
                    SpeedTier = 2,
                    IsAlias = true,
                    Category = "alias"
                }
            ]
        };
    }
}