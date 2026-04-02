namespace Cominomi.Shared.Models;

public class McpServer
{
    public bool IsActive { get; set; } = true;
    public Dictionary<string, string> Env { get; set; } = new();
    public List<string> Args { get; set; } = [];
    public McpServerStatus Status { get; set; } = new();
    public string Name { get; set; } = string.Empty;
    public string Scope { get; set; } = "user"; // "local", "project", "user"
    public string Transport { get; set; } = "stdio"; // "stdio" or "sse"
    public string? Command { get; set; }
    public string? Url { get; set; }
}

public class McpServerStatus
{
    public bool Running { get; set; }
    public DateTime? LastChecked { get; set; }
    public string? Error { get; set; }
}