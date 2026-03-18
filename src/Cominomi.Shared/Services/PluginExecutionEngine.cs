using System.Text.Json;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

/// <summary>
/// Plugin execution result containing stdout, stderr, and exit code.
/// </summary>
public record PluginExecutionResult(bool Success, string Output, string Error, int ExitCode);

/// <summary>
/// Context passed to plugin entry points via environment variables.
/// </summary>
public record PluginExecutionContext
{
    public string Action { get; init; } = "run";
    public Dictionary<string, string> Parameters { get; init; } = [];
}

public interface IPluginExecutionEngine
{
    /// <summary>
    /// Load and activate a valid, enabled plugin. Registers its declared hooks and skills.
    /// </summary>
    Task<bool> LoadPluginAsync(PluginInfo plugin);

    /// <summary>
    /// Unload a plugin, removing its hooks and skills.
    /// </summary>
    Task UnloadPluginAsync(string pluginId);

    /// <summary>
    /// Execute a plugin's entry point with the given context.
    /// </summary>
    Task<PluginExecutionResult> ExecuteAsync(string pluginId, PluginExecutionContext? context = null,
        CancellationToken ct = default);

    /// <summary>
    /// Load all enabled, valid plugins on startup.
    /// </summary>
    Task LoadAllAsync();

    /// <summary>
    /// Get IDs of currently loaded plugins.
    /// </summary>
    IReadOnlySet<string> LoadedPluginIds { get; }
}

public class PluginExecutionEngine : IPluginExecutionEngine
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly IPluginService _pluginService;
    private readonly IProcessRunner _processRunner;
    private readonly IShellService _shellService;
    private readonly IHooksEngine _hooksEngine;
    private readonly ISkillRegistry _skillRegistry;
    private readonly ILogger<PluginExecutionEngine> _logger;

    private readonly Dictionary<string, PluginInfo> _loaded = [];
    // Track hooks/skills registered by each plugin so we can unregister them
    private readonly Dictionary<string, List<(HookEvent evt, string cmd)>> _pluginHooks = [];
    private readonly Dictionary<string, List<string>> _pluginSkills = [];

    public IReadOnlySet<string> LoadedPluginIds => _loaded.Keys.ToHashSet();

    public PluginExecutionEngine(
        IPluginService pluginService,
        IProcessRunner processRunner,
        IShellService shellService,
        IHooksEngine hooksEngine,
        ISkillRegistry skillRegistry,
        ILogger<PluginExecutionEngine> logger)
    {
        _pluginService = pluginService;
        _processRunner = processRunner;
        _shellService = shellService;
        _hooksEngine = hooksEngine;
        _skillRegistry = skillRegistry;
        _logger = logger;
    }

    public async Task LoadAllAsync()
    {
        var plugins = await _pluginService.GetInstalledPluginsAsync();

        foreach (var plugin in plugins.Where(p => p.IsEnabled && p.Status == PluginStatus.Valid))
        {
            await LoadPluginAsync(plugin);
        }

        _logger.LogInformation("Plugin engine loaded {Count} plugin(s)", _loaded.Count);
    }

    public async Task<bool> LoadPluginAsync(PluginInfo plugin)
    {
        if (_loaded.ContainsKey(plugin.Id))
        {
            _logger.LogDebug("Plugin '{Id}' is already loaded", plugin.Id);
            return true;
        }

        if (plugin.Status != PluginStatus.Valid)
        {
            _logger.LogWarning("Cannot load plugin '{Id}': status is {Status}", plugin.Id, plugin.Status);
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
                        _logger.LogWarning("Plugin '{Id}': unknown hook event '{Event}'", plugin.Id, hook.Event);
                        continue;
                    }

                    var command = ResolveCommand(plugin, hook.Command);
                    await _hooksEngine.AddHookAsync(new HookDefinition
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
                    _skillRegistry.Register(new SkillDefinition
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

            _logger.LogInformation("Plugin '{Id}' loaded (hooks: {Hooks}, skills: {Skills})",
                plugin.Id,
                _pluginHooks.GetValueOrDefault(plugin.Id)?.Count ?? 0,
                _pluginSkills.GetValueOrDefault(plugin.Id)?.Count ?? 0);

            return true;
        }
        catch (Exception ex)
        {
            plugin.Status = PluginStatus.Error;
            plugin.Error = $"로드 실패: {ex.Message}";
            _logger.LogError(ex, "Failed to load plugin '{Id}'", plugin.Id);
            return false;
        }
    }

    public async Task UnloadPluginAsync(string pluginId)
    {
        if (!_loaded.Remove(pluginId, out var plugin))
            return;

        // Remove hooks registered by this plugin
        if (_pluginHooks.Remove(pluginId, out var hooks))
        {
            foreach (var (evt, cmd) in hooks)
                await _hooksEngine.RemoveHookAsync(evt, cmd);
        }

        // Remove skills registered by this plugin
        if (_pluginSkills.Remove(pluginId, out var skills))
        {
            foreach (var skillName in skills)
                await _skillRegistry.DeleteCommandAsync(skillName, "plugin", null);
        }

        plugin.Status = PluginStatus.Valid;
        _logger.LogInformation("Plugin '{Id}' unloaded", pluginId);
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
            var shell = await _shellService.GetShellAsync();
            var entryPath = Path.Combine(plugin.Path, plugin.EntryPoint);
            var command = BuildEntryPointCommand(entryPath, plugin.EntryPoint);

            var env = new Dictionary<string, string>
            {
                ["COMINOMI_PLUGIN_ID"] = pluginId,
                ["COMINOMI_PLUGIN_DIR"] = plugin.Path,
                ["COMINOMI_PLUGIN_ACTION"] = context?.Action ?? "run"
            };

            if (context?.Parameters is { Count: > 0 })
            {
                foreach (var (key, value) in context.Parameters)
                    env[$"COMINOMI_PARAM_{key.ToUpperInvariant()}"] = value;
            }

            var escapedCommand = command.Replace("\"", "\\\"");
            var result = await _processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = shell.FileName,
                Arguments = shell.Type == ShellType.Cmd
                    ? ["/c", escapedCommand]
                    : ["-c", escapedCommand],
                WorkingDirectory = plugin.Path,
                EnvironmentVariables = env,
                Timeout = DefaultTimeout
            }, ct);

            return new PluginExecutionResult(result.Success, result.Stdout, result.Stderr, result.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin '{Id}' execution failed", pluginId);
            return new PluginExecutionResult(false, "", ex.Message, -1);
        }
    }

    /// <summary>
    /// Build the shell command to execute a plugin entry point based on its file extension.
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
    /// Resolve a command string from the manifest, replacing {entryPoint} with the actual path.
    /// </summary>
    private static string ResolveCommand(PluginInfo plugin, string command)
    {
        return command
            .Replace("{entryPoint}", Path.Combine(plugin.Path, plugin.EntryPoint ?? ""))
            .Replace("{pluginDir}", plugin.Path);
    }

    /// <summary>
    /// Read extended manifest fields (hooks, skills) from manifest.json.
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
            {
                foreach (var hook in hooks.EnumerateArray())
                {
                    var evt = hook.TryGetProperty("event", out var e) ? e.GetString() : null;
                    var cmd = hook.TryGetProperty("command", out var c) ? c.GetString() : null;
                    if (evt != null && cmd != null)
                        manifest.Hooks.Add(new ManifestHook { Event = evt, Command = cmd });
                }
            }

            if (root.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Array)
            {
                foreach (var skill in skills.EnumerateArray())
                {
                    var name = skill.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var cmd = skill.TryGetProperty("command", out var c) ? c.GetString() : null;
                    if (name != null && cmd != null)
                    {
                        manifest.Skills.Add(new ManifestSkill
                        {
                            Name = name,
                            Command = cmd,
                            Description = skill.TryGetProperty("description", out var d) ? d.GetString() : null
                        });
                    }
                }
            }

            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read extended manifest from {Path}", manifestPath);
            return new ExtendedManifest();
        }
    }

    private class ExtendedManifest
    {
        public List<ManifestHook> Hooks { get; set; } = [];
        public List<ManifestSkill> Skills { get; set; } = [];
    }

    private class ManifestHook
    {
        public string Event { get; set; } = "";
        public string Command { get; set; } = "";
    }

    private class ManifestSkill
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string? Description { get; set; }
    }
}
