using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class McpService(
    IProcessRunner processRunner,
    HttpClient httpClient,
    IClaudeSettingsService claudeSettingsService,
    ILogger<McpService> logger)
    : IMcpService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string ClaudeHomeDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    private readonly SemaphoreSlim _lock = new(1, 1);

    // ── CRUD ────────────────────────────────────────────────────────────────

    public async Task<McpOperationResult> AddServerAsync(McpServer server)
    {
        await _lock.WaitAsync();
        try
        {
            var configPath = GetConfigPath(server.Scope);
            var config = await ReadConfigAsync(configPath);
            config.McpServers ??= new Dictionary<string, McpServerEntry>();

            if (config.McpServers.ContainsKey(server.Name))
                return McpOperationResult.Fail($"서버 '{server.Name}'이 이미 존재합니다.");

            config.McpServers[server.Name] = ToEntry(server);
            await WriteConfigAsync(configPath, config);
            logger.LogInformation("MCP server '{Name}' added to scope '{Scope}'", server.Name, server.Scope);
            return McpOperationResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add MCP server {Name}", server.Name);
            return McpOperationResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<McpOperationResult> RemoveServerAsync(string name, string scope)
    {
        await _lock.WaitAsync();
        try
        {
            var configPath = GetConfigPath(scope);
            var config = await ReadConfigAsync(configPath);
            if (config.McpServers == null || !config.McpServers.ContainsKey(name))
                return McpOperationResult.Fail($"서버 '{name}'을 찾을 수 없습니다.");

            config.McpServers.Remove(name);
            await WriteConfigAsync(configPath, config);
            logger.LogInformation("MCP server '{Name}' removed from scope '{Scope}'", name, scope);
            return McpOperationResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove MCP server {Name}", name);
            return McpOperationResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<McpOperationResult> UpdateServerAsync(string oldName, string oldScope, McpServer server)
    {
        await _lock.WaitAsync();
        try
        {
            if (oldScope == server.Scope)
            {
                // Same scope: single read-write cycle
                var configPath = GetConfigPath(server.Scope);
                var config = await ReadConfigAsync(configPath);
                config.McpServers ??= new Dictionary<string, McpServerEntry>();

                if (!config.McpServers.ContainsKey(oldName))
                    return McpOperationResult.Fail($"서버 '{oldName}'을 찾을 수 없습니다.");

                if (oldName != server.Name && config.McpServers.ContainsKey(server.Name))
                    return McpOperationResult.Fail($"서버 '{server.Name}'이 이미 존재합니다.");

                config.McpServers.Remove(oldName);
                config.McpServers[server.Name] = ToEntry(server);
                await WriteConfigAsync(configPath, config);
            }
            else
            {
                // Cross-scope: remove from old, add to new
                var oldConfigPath = GetConfigPath(oldScope);
                var oldConfig = await ReadConfigAsync(oldConfigPath);
                if (oldConfig.McpServers == null || !oldConfig.McpServers.ContainsKey(oldName))
                    return McpOperationResult.Fail($"서버 '{oldName}'을 찾을 수 없습니다.");

                oldConfig.McpServers.Remove(oldName);
                await WriteConfigAsync(oldConfigPath, oldConfig);

                var newConfigPath = GetConfigPath(server.Scope);
                var newConfig = await ReadConfigAsync(newConfigPath);
                newConfig.McpServers ??= new Dictionary<string, McpServerEntry>();

                if (newConfig.McpServers.ContainsKey(server.Name))
                    return McpOperationResult.Fail($"서버 '{server.Name}'이 이미 존재합니다.");

                newConfig.McpServers[server.Name] = ToEntry(server);
                await WriteConfigAsync(newConfigPath, newConfig);
            }

            logger.LogInformation("MCP server '{OldName}' updated to '{Name}'", oldName, server.Name);
            return McpOperationResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update MCP server {OldName} -> {Name}", oldName, server.Name);
            return McpOperationResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<McpServer>> ListServersAsync(string? projectPath = null)
    {
        var servers = new List<McpServer>();
        try
        {
            var userConfigPath = Path.Combine(ClaudeHomeDir, "mcp.json");
            await LoadServersFromConfigAsync(servers, userConfigPath, "user");

            var projectDir = projectPath ?? Directory.GetCurrentDirectory();
            var projectConfigPath = Path.Combine(projectDir, ".claude", "mcp.json");
            await LoadServersFromConfigAsync(servers, projectConfigPath, "project");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list MCP servers");
        }

        return servers;
    }

    // ── Import ───────────────────────────────────────────────────────────────

    public async Task<McpOperationResult> ImportFromClaudeDesktopAsync(string scope = "user")
    {
        try
        {
            var desktopConfigPath = GetClaudeDesktopConfigPath();
            if (desktopConfigPath == null)
                return McpOperationResult.Fail("이 플랫폼에서는 Claude Desktop 설정을 찾을 수 없습니다.");

            if (!File.Exists(desktopConfigPath))
                return McpOperationResult.Fail($"Claude Desktop 설정 파일을 찾을 수 없습니다: {desktopConfigPath}");

            var json = await File.ReadAllTextAsync(desktopConfigPath);
            var desktopConfig = JsonSerializer.Deserialize<McpConfigFile>(json, ReadOptions);
            if (desktopConfig?.McpServers == null || desktopConfig.McpServers.Count == 0)
                return McpOperationResult.Fail("Claude Desktop 설정에 MCP 서버가 없습니다.");

            return await MergeServersAsync(desktopConfig.McpServers, scope);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse Claude Desktop config");
            return McpOperationResult.Fail($"설정 파일 파싱 실패: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import from Claude Desktop");
            return McpOperationResult.Fail(ex.Message);
        }
    }

    public async Task<McpOperationResult> ImportFromJsonAsync(string json, string scope)
    {
        try
        {
            var importConfig = JsonSerializer.Deserialize<McpConfigFile>(json, ReadOptions);
            if (importConfig?.McpServers == null || importConfig.McpServers.Count == 0)
                return McpOperationResult.Fail("JSON에 MCP 서버가 없습니다.");

            return await MergeServersAsync(importConfig.McpServers, scope);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse MCP JSON");
            return McpOperationResult.Fail($"JSON 파싱 실패: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import MCP from JSON");
            return McpOperationResult.Fail(ex.Message);
        }
    }

    // ── Connection Test ──────────────────────────────────────────────────────

    public async Task<McpServerStatus> TestConnectionAsync(McpServer server, CancellationToken ct = default)
    {
        var status = new McpServerStatus
        {
            ConnectionStatus = McpConnectionStatus.Checking,
            LastChecked = DateTime.UtcNow
        };

        try
        {
            if (server.Transport == "sse" || !string.IsNullOrEmpty(server.Url))
            {
                status = await TestSseConnectionAsync(server.Url!, ct);
            }
            else if (!string.IsNullOrEmpty(server.Command))
            {
                status = await TestStdioConnectionAsync(server, ct);
            }
            else
            {
                status.ConnectionStatus = McpConnectionStatus.Error;
                status.Error = "command 또는 URL이 없습니다.";
            }
        }
        catch (OperationCanceledException)
        {
            status.ConnectionStatus = McpConnectionStatus.Unreachable;
            status.Error = "연결 시간 초과";
        }
        catch (Exception ex)
        {
            status.ConnectionStatus = McpConnectionStatus.Error;
            status.Error = ex.Message;
        }

        status.LastChecked = DateTime.UtcNow;
        return status;
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    private static string GetConfigPath(string scope)
    {
        return scope switch
        {
            "user" => Path.Combine(ClaudeHomeDir, "mcp.json"),
            _ => Path.Combine(Directory.GetCurrentDirectory(), ".claude", "mcp.json")
        };
    }

    private static string? GetClaudeDesktopConfigPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Claude", "claude_desktop_config.json");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "Claude", "claude_desktop_config.json");
        }

        return null;
    }

    private async Task<McpConfigFile> ReadConfigAsync(string configPath)
    {
        if (!File.Exists(configPath))
            return new McpConfigFile();

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            return JsonSerializer.Deserialize<McpConfigFile>(json, ReadOptions) ?? new McpConfigFile();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse MCP config at {Path}, starting fresh", configPath);
            return new McpConfigFile();
        }
    }

    private static async Task WriteConfigAsync(string configPath, McpConfigFile config)
    {
        var json = JsonSerializer.Serialize(config, WriteOptions);
        await AtomicFileWriter.WriteAsync(configPath, json);
    }

    private async Task LoadServersFromConfigAsync(List<McpServer> servers, string configPath, string scope)
    {
        if (!File.Exists(configPath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<McpConfigFile>(json, ReadOptions);
            if (config?.McpServers == null) return;

            foreach (var (name, entry) in config.McpServers)
            {
                servers.Add(new McpServer
                {
                    Name = name,
                    Transport = entry.Type?.ToLowerInvariant() ?? "stdio",
                    Command = entry.Command,
                    Args = entry.Args ?? [],
                    Env = entry.Env ?? new Dictionary<string, string>(),
                    Url = entry.Url,
                    Scope = scope,
                    IsActive = true,
                    Status = new McpServerStatus { ConnectionStatus = McpConnectionStatus.Unknown }
                });
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse MCP config at {Path}", configPath);
        }
    }

    private async Task<McpOperationResult> MergeServersAsync(Dictionary<string, McpServerEntry> entries, string scope)
    {
        await _lock.WaitAsync();
        try
        {
            var configPath = GetConfigPath(scope);
            var config = await ReadConfigAsync(configPath);
            config.McpServers ??= new Dictionary<string, McpServerEntry>();

            var imported = 0;
            var skipped = new List<string>();

            foreach (var (name, entry) in entries)
            {
                if (config.McpServers.ContainsKey(name))
                {
                    skipped.Add(name);
                    continue;
                }

                config.McpServers[name] = entry;
                imported++;
            }

            await WriteConfigAsync(configPath, config);

            var message = $"{imported}개 서버를 가져왔습니다.";
            if (skipped.Count > 0)
                message += $" {skipped.Count}개 건너뜀 (이미 존재): {string.Join(", ", skipped)}";

            logger.LogInformation("MCP import: {Imported} added, {Skipped} skipped", imported, skipped.Count);
            return imported == 0 && skipped.Count > 0
                ? McpOperationResult.Fail($"모든 서버가 이미 존재합니다: {string.Join(", ", skipped)}")
                : new McpOperationResult(true, message);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<McpServerStatus> TestStdioConnectionAsync(McpServer server, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var result = await processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = server.Command!,
                Arguments = server.Args.ToArray(),
                Timeout = TimeSpan.FromSeconds(5),
                EnvironmentVariables = server.Env.Count > 0 ? server.Env : null
            }, cts.Token);

            // Process started and produced output = reachable
            var isReachable = !string.IsNullOrEmpty(result.Stdout) || result.ExitCode != -1;
            return new McpServerStatus
            {
                ConnectionStatus = isReachable ? McpConnectionStatus.Reachable : McpConnectionStatus.Unreachable,
                LastChecked = DateTime.UtcNow,
                Error = isReachable ? null : result.Stderr
            };
        }
        catch (OperationCanceledException)
        {
            // Timeout likely means the server started and is waiting for input — that's reachable
            return new McpServerStatus
            {
                ConnectionStatus = McpConnectionStatus.Reachable,
                LastChecked = DateTime.UtcNow
            };
        }
    }

    private async Task<McpServerStatus> TestSseConnectionAsync(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var response = await httpClient.GetAsync(url, cts.Token);
            return new McpServerStatus
            {
                ConnectionStatus = response.IsSuccessStatusCode || (int)response.StatusCode < 500
                    ? McpConnectionStatus.Reachable
                    : McpConnectionStatus.Unreachable,
                LastChecked = DateTime.UtcNow,
                Error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (HttpRequestException ex)
        {
            return new McpServerStatus
            {
                ConnectionStatus = McpConnectionStatus.Unreachable,
                LastChecked = DateTime.UtcNow,
                Error = ex.Message
            };
        }
    }

    private static McpServerEntry ToEntry(McpServer server) => new()
    {
        Command = string.IsNullOrEmpty(server.Command) ? null : server.Command,
        Args = server.Args.Count > 0 ? server.Args : null,
        Env = server.Env.Count > 0 ? server.Env : null,
        Type = server.Transport != "stdio" ? server.Transport : null,
        Url = string.IsNullOrEmpty(server.Url) ? null : server.Url
    };

    // ── Scope-aware CRUD ─────────────────────────────────────────────────────

    public async Task<List<McpServer>> ListServersByScopeAsync(McpScope scope, string? projectPath = null)
    {
        try
        {
            return scope switch
            {
                McpScope.Desktop => await ListDesktopServersAsync(),
                McpScope.Global => await ListFromSettingsAsync(ClaudeSettingsScope.Global, null),
                McpScope.Local => await ListFromMcpJsonAsync(
                    Path.Combine(projectPath ?? Directory.GetCurrentDirectory(), ".mcp.json"), "local"),
                McpScope.Project => await ListFromSettingsAsync(ClaudeSettingsScope.Project, projectPath),
                _ => []
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list MCP servers for scope {Scope}", scope);
            return [];
        }
    }

    public async Task<McpOperationResult> AddServerToScopeAsync(McpServer server, McpScope scope, string? projectPath = null)
    {
        if (scope == McpScope.Desktop)
            return McpOperationResult.Fail("Desktop scope는 읽기 전용입니다.");

        await _lock.WaitAsync();
        try
        {
            if (scope == McpScope.Local)
            {
                var configPath = Path.Combine(projectPath ?? Directory.GetCurrentDirectory(), ".mcp.json");
                var config = await ReadConfigAsync(configPath);
                config.McpServers ??= new Dictionary<string, McpServerEntry>();
                if (config.McpServers.ContainsKey(server.Name))
                    return McpOperationResult.Fail($"서버 '{server.Name}'이 이미 존재합니다.");
                config.McpServers[server.Name] = ToEntry(server);
                await WriteConfigAsync(configPath, config);
            }
            else
            {
                var settingsScope = scope == McpScope.Global ? ClaudeSettingsScope.Global : ClaudeSettingsScope.Project;
                var settings = await claudeSettingsService.ReadAsync(settingsScope, projectPath);
                settings.McpServers ??= new Dictionary<string, ClaudeMcpServerConfig>();
                if (settings.McpServers.ContainsKey(server.Name))
                    return McpOperationResult.Fail($"서버 '{server.Name}'이 이미 존재합니다.");
                settings.McpServers[server.Name] = ToClaudeEntry(server);
                await claudeSettingsService.WriteAsync(settingsScope, settings, projectPath);
            }

            logger.LogInformation("MCP server '{Name}' added to scope '{Scope}'", server.Name, scope);
            return McpOperationResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add MCP server {Name} to scope {Scope}", server.Name, scope);
            return McpOperationResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<McpOperationResult> RemoveServerFromScopeAsync(string name, McpScope scope, string? projectPath = null)
    {
        if (scope == McpScope.Desktop)
            return McpOperationResult.Fail("Desktop scope는 읽기 전용입니다.");

        await _lock.WaitAsync();
        try
        {
            if (scope == McpScope.Local)
            {
                var configPath = Path.Combine(projectPath ?? Directory.GetCurrentDirectory(), ".mcp.json");
                var config = await ReadConfigAsync(configPath);
                if (config.McpServers == null || !config.McpServers.ContainsKey(name))
                    return McpOperationResult.Fail($"서버 '{name}'을 찾을 수 없습니다.");
                config.McpServers.Remove(name);
                await WriteConfigAsync(configPath, config);
            }
            else
            {
                var settingsScope = scope == McpScope.Global ? ClaudeSettingsScope.Global : ClaudeSettingsScope.Project;
                var settings = await claudeSettingsService.ReadAsync(settingsScope, projectPath);
                if (settings.McpServers == null || !settings.McpServers.ContainsKey(name))
                    return McpOperationResult.Fail($"서버 '{name}'을 찾을 수 없습니다.");
                settings.McpServers.Remove(name);
                await claudeSettingsService.WriteAsync(settingsScope, settings, projectPath);
            }

            logger.LogInformation("MCP server '{Name}' removed from scope '{Scope}'", name, scope);
            return McpOperationResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove MCP server {Name} from scope {Scope}", name, scope);
            return McpOperationResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<McpOperationResult> UpdateServerInScopeAsync(string oldName, McpServer server, McpScope scope, string? projectPath = null)
    {
        if (scope == McpScope.Desktop)
            return McpOperationResult.Fail("Desktop scope는 읽기 전용입니다.");

        await _lock.WaitAsync();
        try
        {
            if (scope == McpScope.Local)
            {
                var configPath = Path.Combine(projectPath ?? Directory.GetCurrentDirectory(), ".mcp.json");
                var config = await ReadConfigAsync(configPath);
                config.McpServers ??= new Dictionary<string, McpServerEntry>();
                if (!config.McpServers.ContainsKey(oldName))
                    return McpOperationResult.Fail($"서버 '{oldName}'을 찾을 수 없습니다.");
                if (oldName != server.Name && config.McpServers.ContainsKey(server.Name))
                    return McpOperationResult.Fail($"서버 '{server.Name}'이 이미 존재합니다.");
                config.McpServers.Remove(oldName);
                config.McpServers[server.Name] = ToEntry(server);
                await WriteConfigAsync(configPath, config);
            }
            else
            {
                var settingsScope = scope == McpScope.Global ? ClaudeSettingsScope.Global : ClaudeSettingsScope.Project;
                var settings = await claudeSettingsService.ReadAsync(settingsScope, projectPath);
                settings.McpServers ??= new Dictionary<string, ClaudeMcpServerConfig>();
                if (!settings.McpServers.ContainsKey(oldName))
                    return McpOperationResult.Fail($"서버 '{oldName}'을 찾을 수 없습니다.");
                if (oldName != server.Name && settings.McpServers.ContainsKey(server.Name))
                    return McpOperationResult.Fail($"서버 '{server.Name}'이 이미 존재합니다.");
                settings.McpServers.Remove(oldName);
                settings.McpServers[server.Name] = ToClaudeEntry(server);
                await claudeSettingsService.WriteAsync(settingsScope, settings, projectPath);
            }

            logger.LogInformation("MCP server '{OldName}' updated to '{Name}' in scope '{Scope}'", oldName, server.Name, scope);
            return McpOperationResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update MCP server {OldName} in scope {Scope}", oldName, scope);
            return McpOperationResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Cloud Servers ─────────────────────────────────────────────────────────

    public async Task<List<McpServer>> ListCloudServersAsync()
    {
        var cachePath = Path.Combine(ClaudeHomeDir, "mcp-needs-auth-cache.json");
        if (!File.Exists(cachePath)) return [];

        try
        {
            var json = await File.ReadAllTextAsync(cachePath);
            using var doc = JsonDocument.Parse(json);
            var servers = new List<McpServer>();

            // The cache file is expected to be a flat object: { "serverName": {...} }
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    servers.Add(new McpServer
                    {
                        Name = prop.Name,
                        Scope = "cloud",
                        Transport = "sse",
                        IsActive = false,
                        Status = new McpServerStatus { ConnectionStatus = McpConnectionStatus.Unknown }
                    });
                }
            }

            return servers;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read cloud MCP cache at {Path}", cachePath);
            return [];
        }
    }

    // ── Tool Permissions ──────────────────────────────────────────────────────

    public List<McpToolPermission> ExtractToolPermissions(string serverName, PermissionRules? permissions)
    {
        if (permissions == null) return [];
        var prefix = $"mcp__{serverName}__";
        var result = new List<McpToolPermission>();

        void Collect(IEnumerable<string>? patterns, McpPermissionLevel level)
        {
            if (patterns == null) return;
            foreach (var p in patterns)
            {
                if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new McpToolPermission
                {
                    RawPattern = p,
                    ToolName = p[prefix.Length..],
                    Level = level
                });
            }
        }

        Collect(permissions.Allow, McpPermissionLevel.Allow);
        Collect(permissions.Ask, McpPermissionLevel.Ask);
        Collect(permissions.Deny, McpPermissionLevel.Deny);
        return result;
    }

    public PermissionRules ApplyToolPermission(PermissionRules? permissions, string serverName, string toolName, McpPermissionLevel level)
    {
        permissions ??= new PermissionRules();
        var pattern = $"mcp__{serverName}__{toolName}";

        // Remove from all lists first
        permissions.Allow = permissions.Allow?.Where(p => p != pattern).ToList();
        permissions.Ask = permissions.Ask?.Where(p => p != pattern).ToList();
        permissions.Deny = permissions.Deny?.Where(p => p != pattern).ToList();

        // Add to the correct list
        switch (level)
        {
            case McpPermissionLevel.Allow:
                permissions.Allow ??= [];
                permissions.Allow.Add(pattern);
                break;
            case McpPermissionLevel.Ask:
                permissions.Ask ??= [];
                permissions.Ask.Add(pattern);
                break;
            case McpPermissionLevel.Deny:
                permissions.Deny ??= [];
                permissions.Deny.Add(pattern);
                break;
        }

        return permissions;
    }

    public PermissionRules RemoveToolPermission(PermissionRules? permissions, string rawPattern)
    {
        permissions ??= new PermissionRules();
        permissions.Allow = permissions.Allow?.Where(p => p != rawPattern).ToList();
        permissions.Ask = permissions.Ask?.Where(p => p != rawPattern).ToList();
        permissions.Deny = permissions.Deny?.Where(p => p != rawPattern).ToList();
        return permissions;
    }

    // ── Private Scope Helpers ─────────────────────────────────────────────────

    private async Task<List<McpServer>> ListDesktopServersAsync()
    {
        var desktopConfigPath = GetClaudeDesktopConfigPath();
        if (desktopConfigPath == null || !File.Exists(desktopConfigPath)) return [];

        try
        {
            var json = await File.ReadAllTextAsync(desktopConfigPath);
            var config = JsonSerializer.Deserialize<McpConfigFile>(json, ReadOptions);
            if (config?.McpServers == null) return [];

            return config.McpServers.Select(kvp => new McpServer
            {
                Name = kvp.Key,
                Transport = kvp.Value.Type?.ToLowerInvariant() ?? "stdio",
                Command = kvp.Value.Command,
                Args = kvp.Value.Args ?? [],
                Env = kvp.Value.Env ?? new Dictionary<string, string>(),
                Url = kvp.Value.Url,
                Scope = "desktop",
                IsActive = false,
                Status = new McpServerStatus { ConnectionStatus = McpConnectionStatus.Unknown }
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read Claude Desktop config");
            return [];
        }
    }

    private async Task<List<McpServer>> ListFromSettingsAsync(ClaudeSettingsScope settingsScope, string? projectPath)
    {
        var settings = await claudeSettingsService.ReadAsync(settingsScope, projectPath);
        if (settings.McpServers == null) return [];

        var scopeStr = settingsScope == ClaudeSettingsScope.Global ? "user" : "project";
        return settings.McpServers.Select(kvp => new McpServer
        {
            Name = kvp.Key,
            Transport = kvp.Value.Url != null ? "sse" : "stdio",
            Command = kvp.Value.Command,
            Args = kvp.Value.Args ?? [],
            Env = kvp.Value.Env ?? new Dictionary<string, string>(),
            Url = kvp.Value.Url,
            Scope = scopeStr,
            IsActive = true,
            Status = new McpServerStatus { ConnectionStatus = McpConnectionStatus.Unknown }
        }).ToList();
    }

    private async Task<List<McpServer>> ListFromMcpJsonAsync(string configPath, string scope)
    {
        var servers = new List<McpServer>();
        await LoadServersFromConfigAsync(servers, configPath, scope);
        return servers;
    }

    private static ClaudeMcpServerConfig ToClaudeEntry(McpServer server) => new()
    {
        Command = string.IsNullOrEmpty(server.Command) ? null : server.Command,
        Args = server.Args.Count > 0 ? server.Args : null,
        Env = server.Env.Count > 0 ? server.Env : null,
        Url = string.IsNullOrEmpty(server.Url) ? null : server.Url
    };

    // ── JSON models ──────────────────────────────────────────────────────────

    private sealed class McpConfigFile
    {
        [JsonPropertyName("mcpServers")] public Dictionary<string, McpServerEntry>? McpServers { get; set; }
    }

    private sealed class McpServerEntry
    {
        [JsonPropertyName("env")] public Dictionary<string, string>? Env { get; set; }
        [JsonPropertyName("args")] public List<string>? Args { get; set; }
        [JsonPropertyName("command")] public string? Command { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }
}
