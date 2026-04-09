namespace Seoro.Shared.Models.Plugin;

public class SkillChainStep
{
    public string ExpandedText { get; set; } = string.Empty;
    public string SkillName { get; set; } = string.Empty;
    public string? Args { get; set; }
}