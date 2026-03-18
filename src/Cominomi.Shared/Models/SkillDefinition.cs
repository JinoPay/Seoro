namespace Cominomi.Shared.Models;

public class SkillDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PromptTemplate { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; } = true;
    public string Scope { get; set; } = "default"; // "default", "user", "project"
    public List<string> AllowedTools { get; set; } = [];
    public string? Namespace { get; set; }
    public bool AcceptsArguments { get; set; }
    public string? FilePath { get; set; }
    public List<string> Chain { get; set; } = [];
}
