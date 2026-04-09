namespace Seoro.Shared.Models.Plugin;

public class SkillDefinition
{
    public bool AcceptsArguments { get; set; }
    public bool IsBuiltIn { get; set; } = true;
    public List<string> AllowedTools { get; set; } = [];
    public List<string> Chain { get; set; } = [];
    public string Description { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PromptTemplate { get; set; } = string.Empty;
    public string Scope { get; set; } = "default"; // "default", "user", "project" 중 하나
    public string? FilePath { get; set; }
    public string? Namespace { get; set; }
}