using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IMcpService
{
    Task<List<McpServer>> ListServersAsync();
    Task<bool> AddServerAsync(string name, string transport, string? command, List<string>? args, Dictionary<string, string>? env, string? url, string scope);
    Task<bool> RemoveServerAsync(string name, string scope);
    Task<bool> UpdateServerAsync(string oldName, string oldScope, string name, string transport, string? command, List<string>? args, Dictionary<string, string>? env, string? url, string scope);
    Task<bool> TestConnectionAsync(string name);
    Task<bool> ImportFromClaudeDesktopAsync();
    Task<bool> ImportFromJsonAsync(string json, string scope);
}
