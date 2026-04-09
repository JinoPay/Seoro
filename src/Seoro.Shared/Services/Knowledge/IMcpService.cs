
namespace Seoro.Shared.Services.Knowledge;

public interface IMcpService
{
    // ── Legacy CRUD (mcp.json 기반, 기존 McpManagerDialog에서 사용) ──────────
    Task<McpOperationResult> AddServerAsync(McpServer server);
    Task<McpOperationResult> RemoveServerAsync(string name, string scope);
    Task<McpOperationResult> UpdateServerAsync(string oldName, string oldScope, McpServer server);
    Task<List<McpServer>> ListServersAsync(string? projectPath = null);
    Task<McpOperationResult> ImportFromClaudeDesktopAsync(string scope = "user");
    Task<McpOperationResult> ImportFromJsonAsync(string json, string scope);
    Task<McpServerStatus> TestConnectionAsync(McpServer server, CancellationToken ct = default);

    // ── Scope-aware CRUD (새 McpPage에서 사용) ────────────────────────────────
    Task<List<McpServer>> ListServersByScopeAsync(McpScope scope, string? projectPath = null);
    Task<McpOperationResult> AddServerToScopeAsync(McpServer server, McpScope scope, string? projectPath = null);
    Task<McpOperationResult> RemoveServerFromScopeAsync(string name, McpScope scope, string? projectPath = null);
    Task<McpOperationResult> UpdateServerInScopeAsync(string oldName, McpServer server, McpScope scope, string? projectPath = null);

    // ── Cloud MCP (읽기 전용, ~/.claude/mcp-needs-auth-cache.json) ───────────
    Task<List<McpServer>> ListCloudServersAsync();

    // ── Tool Permissions ──────────────────────────────────────────────────────
    List<McpToolPermission> ExtractToolPermissions(string serverName, PermissionRules? permissions);
    PermissionRules ApplyToolPermission(PermissionRules? permissions, string serverName, string toolName, McpPermissionLevel level);
    PermissionRules RemoveToolPermission(PermissionRules? permissions, string rawPattern);

    // ── Tool Discovery ────────────────────────────────────────────────────────
    Task<McpToolListResult> ListToolsAsync(McpServer server, CancellationToken ct = default);
}
