using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Plugin;

/// <summary>
///     Plugin execution result containing stdout, stderr, exit code, and parsed structured data.
/// </summary>
public record PluginExecutionResult(bool Success, string Output, string Error, int ExitCode)
{
    /// <summary>
    ///     Structured data parsed from the plugin's JSON stdout response.
    ///     Null when the plugin does not emit a valid JSON response.
    /// </summary>
    public PluginResponse? Data { get; init; }
}

/// <summary>
///     JSON request envelope written to the plugin's stdin.
/// </summary>
public record PluginRequest
{
    public Dictionary<string, object?> Parameters { get; init; } = [];
    public string Action { get; init; } = "run";
    public string PluginId { get; init; } = "";
}

/// <summary>
///     JSON response envelope read from the plugin's stdout.
///     Plugins may emit a single JSON object to stdout to return structured data.
///     If stdout is not valid JSON, the raw text is still available via <see cref="PluginExecutionResult.Output" />.
/// </summary>
public record PluginResponse
{
    public bool Success { get; init; } = true;
    public JsonElement? Data { get; init; }
    public string? Message { get; init; }
}

/// <summary>
///     Context passed to plugin entry points via stdin JSON and environment variables.
/// </summary>
public record PluginExecutionContext
{
    public Dictionary<string, string> Parameters { get; init; } = [];
    public string Action { get; init; } = "run";
}

public interface IPluginExecutionEngine
{
    /// <summary>
    ///     Get IDs of currently loaded plugins.
    /// </summary>
    IReadOnlySet<string> LoadedPluginIds { get; }

    /// <summary>
    ///     Load all enabled, valid plugins on startup.
    /// </summary>
    Task LoadAllAsync();

    /// <summary>
    ///     Unload a plugin, removing its hooks and skills.
    /// </summary>
    Task UnloadPluginAsync(string pluginId);

    /// <summary>
    ///     Load and activate a valid, enabled plugin. Registers its declared hooks and skills.
    /// </summary>
    Task<bool> LoadPluginAsync(PluginInfo plugin);

    /// <summary>
    ///     Execute a plugin's entry point with the given context.
    /// </summary>
    Task<PluginExecutionResult> ExecuteAsync(string pluginId, PluginExecutionContext? context = null,
        CancellationToken ct = default);
}

public class PluginExecutionEngine(
    IPluginService pluginService,
    IProcessRunner processRunner,
    IShellService shellService,
    IHooksEngine hooksEngine,
    ISkillRegistry skillRegistry,
    ILogger<PluginExecutionEngine> logger)
    : IPluginExecutionEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    // Track hooks/skills registered by each plugin so we can unregister them
    private readonly Dictionary<string, List<(HookEvent evt, string cmd)>> _pluginHooks = [];
    private readonly Dictionary<string, List<string>> _pluginSkills = [];

    private readonly Dictionary<string, PluginInfo> _loaded = [];

    public IReadOnlySet<string> LoadedPluginIds => _loaded.Keys.ToHashSet();

    public async Task LoadAllAsync()
    {
        var plugins = await pluginService.GetInstalledPluginsAsync();

        foreach (var plugin in plugins.Where(p => p.IsEnabled && p.Status == PluginStatus.Valid))
            await LoadPluginAsync(plugin);

        logger.LogInformation("Plugin engine loaded {Count} plugin(s)", _loaded.Count);
    }

    public async Task UnloadPluginAsync(string pluginId)
    {
        if (!_loaded.Remove(pluginId, out var plugin))
            return;

        // Remove hooks registered by this plugin
        if (_pluginHooks.Remove(pluginId, out var hooks))
            foreach (var (evt, cmd) in hooks)
                await hooksEngine.RemoveHookAsync(evt, cmd);

        // Remove skills registered by this plugin
        if (_pluginSkills.Remove(pluginId, out var skills))
            foreach (var skillName in skills)
                await skillRegistry.DeleteCommandAsync(skillName, "plugin", null);

        plugin.Status = PluginStatus.Valid;
        logger.LogInformation("플러그인 '{Id}' 언로드됨", pluginId);
    }

    public async Task<bool> LoadPluginAsync(PluginInfo plugin)
    {
        if (_loaded.ContainsKey(plugin.Id))
        {
            logger.LogDebug("플러그인 '{Id}'은 이미 로드됨", plugin.Id);
            return true;
        }

        if (plugin.Status != PluginStatus.Valid)
        {
            logger.LogWarning("Cannot load plugin '{Id}': status is {Status}", plugin.Id, plugin.Status);
            return false;
        }

        try
        {
            // Read extended manifest for hooks/skills declarations
            var manifest = await ReadExtendedManifestAsync(plugin.Path);

            // Register plugin hooks
            if (manifest.Hooks is { Count: > 0 })
            {
                var registered = new List<(HookEvent, string)>();
                foreach (var hook in manifest.Hooks)
                {
                    if (!Enum.TryParse<HookEvent>(hook.Event, out var hookEvent))
                    {
                        logger.LogWarning("Plugin '{Id}': unknown hook event '{Event}'", plugin.Id, hook.Event);
                        continue;
                    }

                    var command = ResolveCommand(plugin, hook.Command);
                    await hooksEngine.AddHookAsync(new HookDefinition
                    {
                        Event = hookEvent,
                        Type = HookType.Command,
                        Command = command,
                        WorkingDirectory = plugin.Path,
                        Enabled = true
                    });
                    registered.Add((hookEvent, command));
                }

                _pluginHooks[plugin.Id] = registered;
            }

            // Register plugin skills
            if (manifest.Skills is { Count: > 0 })
            {
                var registered = new List<string>();
                foreach (var skill in manifest.Skills)
                {
                    var command = ResolveCommand(plugin, skill.Command);
                    skillRegistry.Register(new SkillDefinition
                    {
                        Name = skill.Name,
                        Description = skill.Description ?? $"플러그인 '{plugin.Name}' 스킬",
                        PromptTemplate = $"[Plugin:{plugin.Id}] 이 명령을 실행하세요: {command} {{{{args}}}}",
                        IsBuiltIn = false,
                        Scope = "plugin",
                        Namespace = plugin.Id,
                        AcceptsArguments = true
                    });
                    registered.Add(skill.Name);
                }

                _pluginSkills[plugin.Id] = registered;
            }

            plugin.Status = PluginStatus.Loaded;
            _loaded[plugin.Id] = plugin;

            logger.LogInformation("Plugin '{Id}' loaded (hooks: {Hooks}, skills: {Skills})",
                plugin.Id,
                _pluginHooks.GetValueOrDefault(plugin.Id)?.Count ?? 0,
                _pluginSkills.GetValueOrDefault(plugin.Id)?.Count ?? 0);

            return true;
        }
        catch (Exception ex)
        {
            plugin.Status = PluginStatus.Error;
            plugin.Error = $"로드 실패: {ex.Message}";
            logger.LogError(ex, "Failed to load plugin '{Id}'", plugin.Id);
            return false;
        }
    }

    public async Task<PluginExecutionResult> ExecuteAsync(string pluginId,
        PluginExecutionContext? context = null, CancellationToken ct = default)
    {
        if (!_loaded.TryGetValue(pluginId, out var plugin))
            return new PluginExecutionResult(false, "", $"Plugin '{pluginId}' is not loaded", -1);

        if (string.IsNullOrEmpty(plugin.EntryPoint))
            return new PluginExecutionResult(false, "", $"Plugin '{pluginId}' has no entry point", -1);

        try
        {
            var shell = await shellService.GetShellAsync();
            var entryPath = Path.Combine(plugin.Path, plugin.EntryPoint);
            var command = BuildEntryPointCommand(entryPath, plugin.EntryPoint);

            // Environment variables (backward compatible)
            var env = new Dictionary<string, string>
            {
                ["SEORO_PLUGIN_ID"] = pluginId,
                ["SEORO_PLUGIN_DIR"] = plugin.Path,
                ["SEORO_PLUGIN_ACTION"] = context?.Action ?? "run",
                ["SEORO_PROTOCOL"] = "json/1"
            };

            if (context?.Parameters is { Count: > 0 })
                foreach (var (key, value) in context.Parameters)
                    env[$"SEORO_PARAM_{key.ToUpperInvariant()}"] = value;

            // Build JSON request for stdin
            var request = new PluginRequest
            {
                PluginId = pluginId,
                Action = context?.Action ?? "run",
                Parameters = context?.Parameters?.ToDictionary(
                    kv => kv.Key, kv => (object?)kv.Value) ?? []
            };
            var stdinJson = JsonSerializer.Serialize(request, JsonOptions);

            var escapedCommand = EscapeShellCommand(command, shell.Type);
            var result = await processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = shell.FileName,
                Arguments = shell.Type == ShellType.Cmd
                    ? ["/c", escapedCommand]
                    : ["-c", escapedCommand],
                WorkingDirectory = plugin.Path,
                EnvironmentVariables = env,
                StandardInput = stdinJson,
                Timeout = DefaultTimeout
            }, ct);

            // Try to parse structured JSON response from stdout
            var response = TryParseResponse(result.Stdout);

            return new PluginExecutionResult(result.Success, result.Stdout, result.Stderr, result.ExitCode)
            {
                Data = response
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "플러그인 '{Id}' 실행 실패", pluginId);
            return new PluginExecutionResult(false, "", ex.Message, -1);
        }
    }

    /// <summary>
    ///     Build the shell command to execute a plugin entry point based on its file extension.
    /// </summary>
    private static string BuildEntryPointCommand(string entryPath, string entryPoint)
    {
        var ext = Path.GetExtension(entryPoint).ToLowerInvariant();
        return ext switch
        {
            ".js" => $"node \"{entryPath}\"",
            ".mjs" => $"node \"{entryPath}\"",
            ".ts" => $"npx tsx \"{entryPath}\"",
            ".py" => $"python3 \"{entryPath}\"",
            ".sh" => $"bash \"{entryPath}\"",
            ".ps1" => $"pwsh -File \"{entryPath}\"",
            _ => $"\"{entryPath}\""
        };
    }

    /// <summary>
    ///     Resolve a command string from the manifest, replacing {entryPoint} with the actual path.
    /// </summary>
    private static string ResolveCommand(PluginInfo plugin, string command)
    {
        return command
            .Replace("{entryPoint}", Path.Combine(plugin.Path, plugin.EntryPoint ?? ""))
            .Replace("{pluginDir}", plugin.Path);
    }

    /// <summary>
    ///     Try to parse a <see cref="PluginResponse" /> from the plugin's stdout.
    ///     Returns null if the output is not valid JSON or cannot be deserialized.
    /// </summary>
    private PluginResponse? TryParseResponse(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;

        var trimmed = stdout.Trim();
        if (!trimmed.StartsWith('{'))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PluginResponse>(trimmed, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "플러그인 stdout이 유효한 JSON이 아님, 순수 텍스트로 처리");
            return null;
        }
    }

    /// <summary>
    ///     Read extended manifest fields (hooks, skills) from manifest.json.
    /// </summary>
    private async Task<ExtendedManifest> ReadExtendedManifestAsync(string pluginDir)
    {
        var manifestPath = Path.Combine(pluginDir, "manifest.json");
        if (!File.Exists(manifestPath))
            return new ExtendedManifest();

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var manifest = new ExtendedManifest();

            if (root.TryGetProperty("hooks", out var hooks) && hooks.ValueKind == JsonValueKind.Array)
                foreach (var hook in hooks.EnumerateArray())
                {
                    var evt = hook.TryGetProperty("event", out var e) ? e.GetString() : null;
                    var cmd = hook.TryGetProperty("command", out var c) ? c.GetString() : null;
                    if (evt != null && cmd != null)
                        manifest.Hooks.Add(new ManifestHook { Event = evt, Command = cmd });
                }

            if (root.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Array)
                foreach (var skill in skills.EnumerateArray())
                {
                    var name = skill.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var cmd = skill.TryGetProperty("command", out var c) ? c.GetString() : null;
                    if (name != null && cmd != null)
                        manifest.Skills.Add(new ManifestSkill
                        {
                            Name = name,
                            Command = cmd,
                            Description = skill.TryGetProperty("description", out var d) ? d.GetString() : null
                        });
                }

            return manifest;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read extended manifest from {Path}", manifestPath);
            return new ExtendedManifest();
        }
    }

    private class ExtendedManifest
    {
        public List<ManifestHook> Hooks { get; } = [];
        public List<ManifestSkill> Skills { get; } = [];
    }

    private class ManifestHook
    {
        public string Command { get; set; } = "";
        public string Event { get; set; } = "";
    }

    private class ManifestSkill
    {
        public string Command { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
    }

    /// <summary>
    ///     Escape a command string for safe shell execution.
    ///     For cmd.exe: escapes &amp;, |, &gt;, &lt;, ^, %, and double-quotes.
    ///     For bash/zsh: escapes backslashes, double-quotes, backticks, $, and !.
    /// </summary>
    internal static string EscapeShellCommand(string command, ShellType shellType)
    {
        if (string.IsNullOrEmpty(command))
            return command;

        if (shellType == ShellType.Cmd)
            // cmd.exe uses ^ as escape character for special characters
            return command
                .Replace("^", "^^")
                .Replace("&", "^&")
                .Replace("|", "^|")
                .Replace("<", "^<")
                .Replace(">", "^>")
                .Replace("%", "%%");

        // POSIX shells (bash, zsh) — escape within double-quoted context
        return command
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("`", "\\`")
            .Replace("$", "\\$")
            .Replace("!", "\\!");
    }
}