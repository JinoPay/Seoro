namespace Seoro.Shared.Models.Knowledge;

public class RuleFile
{
    public required ClaudeSettingsScope Scope { get; set; }
    public List<string> PathFilters { get; set; } = [];
    public string Content { get; set; } = "";
    public required string FileName { get; set; }
    public required string FilePath { get; set; }
    public string? Description { get; set; }
}