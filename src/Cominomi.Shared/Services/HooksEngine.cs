using System.Diagnostics;
using System.Text.Json;
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
    private readonly string _hooksFile;
    private List<HookDefinition> _hooks = [];

    public HooksEngine(ILogger<HooksEngine> logger)
    {
        _logger = logger;
        _hooksFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cominomi", "hooks.json");
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
            _hooks = JsonSerializer.Deserialize<List<HookDefinition>>(json, JsonOptions) ?? [];
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
        var json = JsonSerializer.Serialize(_hooks, JsonOptions);
        await File.WriteAllTextAsync(_hooksFile, json);
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
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"{hook.Command.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                if (!string.IsNullOrEmpty(hook.WorkingDirectory))
                    psi.WorkingDirectory = hook.WorkingDirectory;

                if (env != null)
                {
                    foreach (var (key, value) in env)
                        psi.Environment[key] = value;
                }

                psi.Environment["COMINOMI_HOOK_EVENT"] = hookEvent.ToString();

                using var process = Process.Start(psi);
                if (process != null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await process.WaitForExitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        try { process.Kill(); } catch { }
                    }
                }
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
