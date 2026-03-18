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
        if (hooks.Count == 0) return;

        _logger.LogInformation("Firing hook event {Event} ({Count} hooks)", hookEvent, hooks.Count);

        var shell = await _shellService.GetShellAsync();
        var tasks = hooks.Select(hook => ExecuteHookAsync(hook, hookEvent, shell, env));
        await Task.WhenAll(tasks);
    }

    private async Task ExecuteHookAsync(
        HookDefinition hook, HookEvent hookEvent, ShellInfo shell, Dictionary<string, string>? env)
    {
        try
        {
            var envVars = new Dictionary<string, string>
            {
                [CominomiConstants.Env.HookEvent] = hookEvent.ToString()
            };
            if (env != null)
            {
                foreach (var (key, value) in env)
                    envVars[key] = value;
            }

            var result = await _processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = shell.FileName,
                Arguments = shell.Type == ShellType.Cmd
                    ? ["/c", hook.Command]
                    : ["-c", hook.Command],
                WorkingDirectory = hook.WorkingDirectory ?? ".",
                EnvironmentVariables = envVars,
                Timeout = TimeSpan.FromSeconds(hook.TimeoutSeconds)
            });

            if (!string.IsNullOrWhiteSpace(result.Stdout))
                _logger.LogDebug("Hook '{Command}' stdout: {Stdout}", hook.Command, result.Stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.Stderr))
                _logger.LogWarning("Hook '{Command}' stderr: {Stderr}", hook.Command, result.Stderr.TrimEnd());
            if (!result.Success)
                _logger.LogWarning("Hook '{Command}' for event {Event} exited with code {ExitCode}",
                    hook.Command, hookEvent, result.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hook '{Command}' for event {Event} failed", hook.Command, hookEvent);
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
