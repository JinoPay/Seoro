namespace Cominomi.Shared.Models;

public record ModelInfo(string Id, string DisplayName);

public static class ModelDefinitions
{
    public static readonly ModelInfo[] All =
    [
        new("opus", "Claude Opus"),
        new("sonnet", "Claude Sonnet"),
        new("haiku", "Claude Haiku"),
    ];

    public static ModelInfo Default => All[0];

    public static string NormalizeModelId(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return Default.Id;
        if (All.Any(m => m.Id == modelId)) return modelId;
        if (modelId.Contains("opus", StringComparison.OrdinalIgnoreCase)) return "opus";
        if (modelId.Contains("sonnet", StringComparison.OrdinalIgnoreCase)) return "sonnet";
        if (modelId.Contains("haiku", StringComparison.OrdinalIgnoreCase)) return "haiku";
        return modelId;
    }
}
