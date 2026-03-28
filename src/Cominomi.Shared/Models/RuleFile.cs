namespace Cominomi.Shared.Models;

public class RuleFile
{
    public required string FileName { get; set; }
    public required string FilePath { get; set; }
    public required ClaudeSettingsScope Scope { get; set; }
    public string Content { get; set; } = "";
    public List<string> PathFilters { get; set; } = [];
    public string? Description { get; set; }
}
