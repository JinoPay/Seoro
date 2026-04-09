namespace Seoro.Shared.Models.Knowledge;

public class InstructionFile
{
    public bool Exists { get; set; }
    public required ClaudeSettingsScope Scope { get; set; }
    public List<string> ImportRefs { get; set; } = [];
    public string Content { get; set; } = "";
    public required string FilePath { get; set; }
}