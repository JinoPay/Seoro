namespace Cominomi.Shared.Models;

public class McpServer
{
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = "stdio"; // "stdio" or "sse"
    public string? Command { get; set; }
    public List<string> Args { get; set; } = [];
    public Dictionary<string, string> Env { get; set; } = new();
    public string? Url { get; set; }
    public string Scope { get; set; } = "user"; // "local", "project", "user"
    public bool IsActive { get; set; } = true;
    public McpServerStatus Status { get; set; } = new();
}

public class McpServerStatus
{
    public bool Running { get; set; }
    public string? Error { get; set; }
    public DateTime? LastChecked { get; set; }
}
