namespace Cominomi.Shared.Models;

public enum McpScope
{
    Desktop, // Claude Desktop's claude_desktop_config.json (read-only)
    Global,  // ~/.claude/settings.json -> mcpServers
    Local,   // <project>/.mcp.json
    Project  // <project>/.claude/settings.json -> mcpServers
}

public enum McpPermissionLevel { Allow, Ask, Deny }

public class McpToolPermission
{
    public string ToolName { get; set; } = "";
    public McpPermissionLevel Level { get; set; }
    public string RawPattern { get; set; } = ""; // e.g. "mcp__serverName__toolName"
}

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

public enum McpConnectionStatus
{
    Unknown,
    Checking,
    Reachable,
    Unreachable,
    Error
}

public class McpServerStatus
{
    public McpConnectionStatus ConnectionStatus { get; set; } = McpConnectionStatus.Unknown;
    public DateTime? LastChecked { get; set; }
    public string? Error { get; set; }
}

public record McpOperationResult(bool Success, string? Error = null)
{
    public static McpOperationResult Ok() => new(true);
    public static McpOperationResult Fail(string error) => new(false, error);
}