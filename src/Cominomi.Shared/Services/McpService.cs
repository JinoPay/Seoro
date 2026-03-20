using System.Text.Json;
using System.Text.Json.Serialization;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class McpService : IMcpService
{
    private readonly IShellService _shellService;
    private readonly IClaudeService _claudeService;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<McpService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public McpService(IShellService shellService, IClaudeService claudeService, IProcessRunner processRunner, ILogger<McpService> logger)
    {
        _shellService = shellService;
        _claudeService = claudeService;
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<List<McpServer>> ListServersAsync()
    {
        var servers = new List<McpServer>();
        try
        {
            // Read user-scope config: ~/.claude/mcp.json
            var userConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "mcp.json");
            await LoadServersFromConfigAsync(servers, userConfigPath, "user");

            // Read project-scope config: <project>/.claude/mcp.json
            var projectConfigPath = Path.Combine(
                Directory.GetCurrentDirectory(), ".claude", "mcp.json");
            await LoadServersFromConfigAsync(servers, projectConfigPath, "project");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list MCP servers");
        }

        return servers;
    }

    private async Task LoadServersFromConfigAsync(List<McpServer> servers, string configPath, string scope)
    {
        if (!File.Exists(configPath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<McpConfigFile>(json, JsonOptions);
            if (config?.McpServers == null) return;

            foreach (var (name, entry) in config.McpServers)
            {
                var transport = entry.Type?.ToLower() ?? "stdio";
                servers.Add(new McpServer
                {
                    Name = name,
                    Transport = transport,
                    Command = entry.Command,
                    Args = entry.Args ?? [],
                    Env = entry.Env ?? new(),
                    Url = entry.Url,
                    Scope = scope,
                    IsActive = true,
                    Status = new McpServerStatus { Running = true, LastChecked = DateTime.UtcNow }
                });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse MCP config at {Path}", configPath);
        }
    }

    private sealed class McpConfigFile
    {
        [JsonPropertyName("mcpServers")]
        public Dictionary<string, McpServerEntry>? McpServers { get; set; }
    }

    private sealed class McpServerEntry
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("command")]
        public string? Command { get; set; }

        [JsonPropertyName("args")]
        public List<string>? Args { get; set; }

        [JsonPropertyName("env")]
        public Dictionary<string, string>? Env { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    public async Task<bool> AddServerAsync(string name, string transport, string? command, List<string>? args, Dictionary<string, string>? env, string? url, string scope)
    {
        try
        {
            var shell = await _shellService.GetShellAsync();
            var cmdParts = new List<string> { "mcp", "add" };

            if (scope != "user")
                cmdParts.AddRange(["--scope", scope]);

            cmdParts.Add(QuoteForShell(name, shell.Type));

            if (transport == "sse" && !string.IsNullOrEmpty(url))
            {
                cmdParts.Add(QuoteForShell(url, shell.Type));
            }
            else if (!string.IsNullOrEmpty(command))
            {
                cmdParts.Add(QuoteForShell(command, shell.Type));
                if (args != null)
                    foreach (var arg in args)
                        cmdParts.Add(QuoteForShell(arg, shell.Type));
            }

            // Add environment variables
            if (env != null)
            {
                foreach (var (key, value) in env)
                {
                    cmdParts.AddRange(["-e", $"{key}={value}"]);
                }
            }

            var cmd = string.Join(" ", cmdParts);
            var output = await RunClaudeCommandAsync(shell, cmd);
            return output != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add MCP server {Name}", name);
            return false;
        }
    }

    public async Task<bool> RemoveServerAsync(string name, string scope)
    {
        try
        {
            var shell = await _shellService.GetShellAsync();
            var scopeArg = scope != "user" ? $" --scope {scope}" : "";
            var output = await RunClaudeCommandAsync(shell, $"mcp remove{scopeArg} {QuoteForShell(name, shell.Type)}");
            return output != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove MCP server {Name}", name);
            return false;
        }
    }

    public async Task<bool> UpdateServerAsync(string oldName, string oldScope, string name, string transport, string? command, List<string>? args, Dictionary<string, string>? env, string? url, string scope)
    {
        try
        {
            var removed = await RemoveServerAsync(oldName, oldScope);
            if (!removed)
                return false;

            var added = await AddServerAsync(name, transport, command, args, env, url, scope);
            if (!added)
            {
                _logger.LogWarning("Failed to add server after removing old one during update. Old: {OldName}/{OldScope}", oldName, oldScope);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update MCP server {OldName} -> {Name}", oldName, name);
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(string name)
    {
        // Best-effort test: just try listing and see if the server is present
        var servers = await ListServersAsync();
        return servers.Any(s => s.Name == name);
    }

    public async Task<bool> ImportFromClaudeDesktopAsync()
    {
        try
        {
            var shell = await _shellService.GetShellAsync();
            var output = await RunClaudeCommandAsync(shell, "mcp add-from-claude-desktop");
            return output != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import from Claude Desktop");
            return false;
        }
    }

    public async Task<bool> ImportFromJsonAsync(string json, string scope)
    {
        try
        {
            var shell = await _shellService.GetShellAsync();
            var scopeArg = scope != "user" ? $" --scope {scope}" : "";
            var quotedJson = QuoteForShell(json, shell.Type);
            var output = await RunClaudeCommandAsync(shell, $"mcp add-json{scopeArg} {quotedJson}");
            return output != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import MCP from JSON");
            return false;
        }
    }

    private async Task<string?> RunClaudeCommandAsync(ShellInfo shell, string subCommand)
    {
        var (found, claudePath) = await _claudeService.DetectCliAsync();
        if (!found || string.IsNullOrEmpty(claudePath))
        {
            _logger.LogWarning("Claude CLI not found");
            return null;
        }

        var shellCommand = $"{claudePath} {subCommand}";
        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = shell.FileName,
            Arguments = shell.Type == ShellType.Cmd
                ? ["/c", shellCommand]
                : ["-c", shellCommand],
            Timeout = TimeSpan.FromSeconds(30)
        });

        if (!result.Success)
        {
            _logger.LogWarning("Claude MCP command failed: {Error}", result.Stderr);
            return null;
        }

        return result.Stdout;
    }

    private static string QuoteForShell(string arg, ShellType shellType)
    {
        if (shellType == ShellType.Cmd)
        {
            // cmd.exe: use double quotes, escape internal double quotes with backslash
            var escaped = arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }
        // bash/zsh: use single quotes, escape embedded single quotes
        var bashEscaped = arg.Replace("'", "'\\''");
        return $"'{bashEscaped}'";
    }
}
