using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

public record ModelInfo(string Id, string DisplayName)
{
    [JsonPropertyName("keywords")]
    public string[] Keywords { get; init; } = [];

    [JsonPropertyName("pricing")]
    public ModelPricing? Pricing { get; init; }
}

public record ModelPricing(
    [property: JsonPropertyName("input")] decimal Input,
    [property: JsonPropertyName("output")] decimal Output,
    [property: JsonPropertyName("cacheWrite")] decimal CacheWrite,
    [property: JsonPropertyName("cacheRead")] decimal CacheRead);

public record ModelConfig
{
    [JsonPropertyName("defaultModelId")]
    public string DefaultModelId { get; init; } = "opus";

    [JsonPropertyName("defaultPricingFallbackId")]
    public string DefaultPricingFallbackId { get; init; } = "sonnet";

    [JsonPropertyName("models")]
    public ModelInfo[] Models { get; init; } = [];
}

public static class ModelDefinitions
{
    private static ModelConfig _config = CreateDefaultConfig();

    public static ModelInfo[] All => _config.Models;
    public static ModelInfo Default => All.FirstOrDefault(m => m.Id == _config.DefaultModelId) ?? All[0];
    public static string PricingFallbackId => _config.DefaultPricingFallbackId;

    /// <summary>
    /// Load models from a JSON file. Falls back to built-in defaults if the file is missing or invalid.
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

    public static string NormalizeModelId(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return Default.Id;
        if (All.Any(m => m.Id == modelId)) return modelId;

        // Keyword-based normalization
        foreach (var model in All)
        {
            foreach (var keyword in model.Keywords)
            {
                if (modelId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return model.Id;
            }
        }

        // Custom API model IDs (e.g. "claude-...") are kept as-is
        if (modelId.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
            return modelId;

        // Unknown short strings (e.g. "Model") — normalize to default
        return Default.Id;
    }

    public static ModelPricing? GetPricing(string modelId)
    {
        var normalized = NormalizeModelId(modelId);
        var model = All.FirstOrDefault(m => m.Id == normalized);
        if (model?.Pricing != null) return model.Pricing;

        // Fallback
        return All.FirstOrDefault(m => m.Id == PricingFallbackId)?.Pricing;
    }

    public static bool SupportsMaxEffort(string modelId)
    {
        var normalized = NormalizeModelId(modelId);
        return normalized == "opus";
    }

    private static ModelConfig CreateDefaultConfig() => new()
    {
        DefaultModelId = "opus",
        DefaultPricingFallbackId = "sonnet",
        Models =
        [
            new("opus", "Claude Opus")
            {
                Keywords = ["opus"],
                Pricing = new ModelPricing(15.0m, 75.0m, 18.75m, 1.50m)
            },
            new("sonnet", "Claude Sonnet")
            {
                Keywords = ["sonnet"],
                Pricing = new ModelPricing(3.0m, 15.0m, 3.75m, 0.30m)
            },
            new("haiku", "Claude Haiku")
            {
                Keywords = ["haiku"],
                Pricing = new ModelPricing(1.0m, 5.0m, 1.25m, 0.10m)
            },
        ]
    };
}
