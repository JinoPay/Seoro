using System.Text.Json;
using System.Text.Json.Nodes;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Services;

public class HooksEngine(
    ILogger<HooksEngine> logger,
    IShellService shellService,
    IProcessRunner processRunner,
    IOptionsMonitor<AppSettings> appSettings)
    : IHooksEngine
{
    private const int MaxConcurrency = 8;
    private const int MaxTimeoutSeconds = 300;

    private const int MinTimeoutSeconds = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _hooksFile = Path.Combine(AppPaths.Settings, "hooks.json");
    private List<HookDefinition> _hooks = [];

    public List<HookDefinition> GetHooks()
    {
        return [.. _hooks];
    }

    public async Task AddHookAsync(HookDefinition hook)
    {
        _hooks.Add(hook);
        await SaveAsync();
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
            logger.LogWarning(ex, "Failed to load hooks configuration");
            _hooks = [];
        }
    }

    public async Task RemoveHookAsync(HookEvent hookEvent, string command)
    {
        _hooks.RemoveAll(h => h.Event == hookEvent && h.Command == command);
        await SaveAsync();
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_hooksFile)!);
        var envelope = new HooksFileEnvelope { Hooks = _hooks };
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        await AtomicFileWriter.WriteAsync(_hooksFile, json);
    }

    public async Task<List<HookExecutionResult>> FireAsync(HookEvent hookEvent, Dictionary<string, string>? env = null)
    {
        var hooks = _hooks.Where(h => h.Event == hookEvent && h.Enabled).ToList();
        if (hooks.Count == 0) return [];

        logger.LogInformation("Firing hook event {Event} ({Count} hooks)", hookEvent, hooks.Count);

        var shell = await shellService.GetShellAsync();

        // Concurrent execution with bounded parallelism to prevent resource exhaustion
        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = hooks.Select(async hook =>
        {
            await semaphore.WaitAsync();
            try
            {
                return await ExecuteHookAsync(hook, hookEvent, shell, env);
            }
            finally
            {
                semaphore.Release();
            }
        });
        var results = await Task.WhenAll(tasks);
        return [.. results];
    }

    private async Task<HookExecutionResult> ExecuteHookAsync(
        HookDefinition hook, HookEvent hookEvent, ShellInfo shell, Dictionary<string, string>? env)
    {
        try
        {
            var envVars = new Dictionary<string, string>
            {
                [CominomiConstants.Env.HookEvent] = hookEvent.ToString()
            };
            if (env != null)
                foreach (var (key, value) in env)
                    envVars[key] = value;

            var globalDefault = appSettings.CurrentValue.HookTimeoutSeconds;
            var rawTimeout = hook.TimeoutSeconds ?? globalDefault;
            var timeoutSeconds = Math.Clamp(rawTimeout, MinTimeoutSeconds, MaxTimeoutSeconds);
            if (rawTimeout != timeoutSeconds)
                logger.LogWarning(
                    "Hook '{Command}' timeout {Original}s clamped to {Clamped}s (allowed: {Min}-{Max})",
                    hook.Command, rawTimeout, timeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);

            var result = await processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = shell.FileName,
                Arguments = shell.Type == ShellType.Cmd
                    ? ["/c", hook.Command]
                    : ["-c", hook.Command],
                WorkingDirectory = hook.WorkingDirectory ?? ".",
                EnvironmentVariables = envVars,
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            });

            if (!string.IsNullOrWhiteSpace(result.Stdout))
                logger.LogDebug("Hook '{Command}' stdout: {Stdout}", hook.Command, result.Stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.Stderr))
                logger.LogWarning("Hook '{Command}' stderr: {Stderr}", hook.Command, result.Stderr.TrimEnd());
            if (!result.Success)
                logger.LogWarning("Hook '{Command}' for event {Event} exited with code {ExitCode}",
                    hook.Command, hookEvent, result.ExitCode);

            var timedOut = result.ExitCode == -1 && result.Stderr.Contains("timed out");
            return new HookExecutionResult(hook.Command, result.Success, result.ExitCode,
                result.Stdout, result.Stderr, timedOut);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hook '{Command}' for event {Event} failed", hook.Command, hookEvent);
            return new HookExecutionResult(hook.Command, false, -1, "", ex.Message, false);
        }
    }
}