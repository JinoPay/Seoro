namespace Cominomi.Shared.Models;

public class InstructionFile
{
    public required ClaudeSettingsScope Scope { get; set; }
    public required string FilePath { get; set; }
    public string Content { get; set; } = "";
    public bool Exists { get; set; }
    public List<string> ImportRefs { get; set; } = [];
}
