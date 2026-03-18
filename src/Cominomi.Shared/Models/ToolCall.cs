namespace Cominomi.Shared.Models;

public class ToolCall
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public bool IsComplete { get; set; }
}
