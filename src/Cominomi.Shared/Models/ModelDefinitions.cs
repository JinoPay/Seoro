namespace Cominomi.Shared.Models;

public record ModelInfo(string Id, string DisplayName);

public static class ModelDefinitions
{
    public static readonly ModelInfo[] All =
    [
        new("claude-opus-4-20250514", "Claude Opus"),
        new("claude-sonnet-4-20250514", "Claude Sonnet"),
        new("claude-haiku-3-20250307", "Claude Haiku"),
    ];

    public static ModelInfo Default => All[0];
}
