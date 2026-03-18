using System.Text.Json;
using System.Text.RegularExpressions;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class McpService : IMcpService
{
    private readonly IShellService _shellService;
    private readonly IClaudeService _claudeService;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<McpService> _logger;

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
            var (claudePath, _) = await _claudeService.DetectCliAsync();
            if (!claudePath) return servers;

            var shell = await _shellService.GetShellAsync();
            var output = await RunClaudeCommandAsync(shell, "mcp list");
            if (string.IsNullOrEmpty(output)) return servers;

            // Parse output: each line is typically "name  transport  scope"
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("─") || trimmed.StartsWith("Name"))
                    continue;

                var parts = Regex.Split(trimmed, @"\s{2,}");
                if (parts.Length >= 2)
                {
                    servers.Add(new McpServer
                    {
                        Name = parts[0].Trim(),
                        Transport = parts.Length > 1 ? parts[1].Trim().ToLower() : "stdio",
                        Scope = parts.Length > 2 ? parts[2].Trim().ToLower() : "user",
                        IsActive = true,
                        Status = new McpServerStatus { Running = true, LastChecked = DateTime.UtcNow }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list MCP servers");
        }

        return servers;
    }

    public async Task<bool> AddServerAsync(string name, string transport, string? command, List<string>? args, Dictionary<string, string>? env, string? url, string scope)
    {
        try
        {
            var shell = await _shellService.GetShellAsync();
            var cmdParts = new List<string> { "mcp", "add" };

            if (scope != "user")
                cmdParts.AddRange(["--scope", scope]);

            cmdParts.Add(EscapeArg(name));

            if (transport == "sse" && !string.IsNullOrEmpty(url))
            {
                cmdParts.Add(EscapeArg(url));
            }
            else if (!string.IsNullOrEmpty(command))
            {
                cmdParts.Add(EscapeArg(command));
                if (args != null)
                    foreach (var arg in args)
                        cmdParts.Add(EscapeArg(arg));
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
            var output = await RunClaudeCommandAsync(shell, $"mcp remove{scopeArg} {EscapeArg(name)}");
            return output != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove MCP server {Name}", name);
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
            var escapedJson = json.Replace("'", "'\\''");
            var scopeArg = scope != "user" ? $" --scope {scope}" : "";
            var output = await RunClaudeCommandAsync(shell, $"mcp add-json{scopeArg} '{escapedJson}'");
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

    private static string EscapeArg(string arg)
    {
        // Use single quotes for shell safety — escape any embedded single quotes
        var escaped = arg.Replace("'", "'\\''");
        return $"'{escaped}'";
    }
}
