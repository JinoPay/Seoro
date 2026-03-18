using System.Text.Json;
using System.Text.Json.Nodes;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class HooksEngine : IHooksEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<HooksEngine> _logger;
    private readonly IShellService _shellService;
    private readonly IProcessRunner _processRunner;
    private readonly string _hooksFile;
    private List<HookDefinition> _hooks = [];

    public HooksEngine(ILogger<HooksEngine> logger, IShellService shellService, IProcessRunner processRunner)
    {
        _logger = logger;
        _shellService = shellService;
        _processRunner = processRunner;
        _hooksFile = Path.Combine(AppPaths.Settings, "hooks.json");
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_hooksFile))
        {
            _hooks = [];
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_hooksFile);
            var node = JsonNode.Parse(json);

            if (node is JsonArray)
            {
                // Legacy bare-array format — migrate to envelope
                _hooks = JsonSerializer.Deserialize<List<HookDefinition>>(json, JsonOptions) ?? [];
                await SaveAsync(); // Write back in new envelope format
            }
            else if (node is JsonObject obj)
            {
                var envelope = JsonSerializer.Deserialize<HooksFileEnvelope>(json, JsonOptions);
                _hooks = envelope?.Hooks ?? [];
            }
            else
            {
                _hooks = [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load hooks configuration");
            _hooks = [];
        }
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_hooksFile)!);
        var envelope = new HooksFileEnvelope { Hooks = _hooks };
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        await AtomicFileWriter.WriteAsync(_hooksFile, json);
    }

    public async Task FireAsync(HookEvent hookEvent, Dictionary<string, string>? env = null)
    {
        var hooks = _hooks.Where(h => h.Event == hookEvent && h.Enabled).ToList();
        if (hooks.Count > 0)
            _logger.LogInformation("Firing hook event {Event} ({Count} hooks)", hookEvent, hooks.Count);

        foreach (var hook in hooks)
        {
            try
            {
                var shell = await _shellService.GetShellAsync();
                var escapedCommand = hook.Command.Replace("\"", "\\\"");

                var shellArg = shell.Type == ShellType.Cmd
                    ? $"/c \"{escapedCommand}\""
                    : $"-c \"{escapedCommand}\"";

                var envVars = new Dictionary<string, string>
                {
                    [CominomiConstants.Env.HookEvent] = hookEvent.ToString()
                };
                if (env != null)
                {
                    foreach (var (key, value) in env)
                        envVars[key] = value;
                }

                await _processRunner.RunAsync(new ProcessRunOptions
                {
                    FileName = shell.FileName,
                    // Shell commands must be passed as a single argument string via ArgumentList
                    Arguments = shell.Type == ShellType.Cmd
                        ? ["/c", escapedCommand]
                        : ["-c", escapedCommand],
                    WorkingDirectory = hook.WorkingDirectory ?? ".",
                    EnvironmentVariables = envVars,
                    Timeout = TimeSpan.FromSeconds(5)
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hook '{Command}' for event {Event} failed", hook.Command, hookEvent);
            }
        }
    }

    public List<HookDefinition> GetHooks() => [.. _hooks];

    public async Task AddHookAsync(HookDefinition hook)
    {
        _hooks.Add(hook);
        await SaveAsync();
    }

    public async Task RemoveHookAsync(HookEvent hookEvent, string command)
    {
        _hooks.RemoveAll(h => h.Event == hookEvent && h.Command == command);
        await SaveAsync();
    }
}
