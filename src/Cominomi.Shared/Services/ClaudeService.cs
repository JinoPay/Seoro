using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class ClaudeService : IClaudeService
{
    private readonly ISettingsService _settingsService;
    private readonly ConcurrentDictionary<string, AgentProcess> _agents = new();
    private CliCapabilities? _capabilities;
    private readonly SemaphoreSlim _capLock = new(1, 1);

    private const string DefaultAgentKey = "__default__";

    public ClaudeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async IAsyncEnumerable<StreamEvent> SendMessageAsync(
        string message,
        string workingDir,
        string model,
        string permissionMode = "default",
        string? sessionId = null,
        string? conversationId = null,
        string? systemPrompt = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agentKey = sessionId ?? DefaultAgentKey;

        var settings = await _settingsService.LoadAsync();
        var (fileName, baseArgs) = ResolveClaudeCommand(settings.ClaudePath);
        var caps = await DetectCapabilitiesAsync(fileName, baseArgs);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Cancel any previous process for this specific session
        if (_agents.TryRemove(agentKey, out var previous))
        {
            previous.Cancel();
        }

        var arguments = BuildArguments(baseArgs, model, permissionMode, caps, conversationId, systemPrompt);
        var token = cts.Token;

        var process = StartProcess(fileName, arguments, workingDir);
        var agent = new AgentProcess(process, cts);
        _agents[agentKey] = agent;

        await process.StandardInput.WriteAsync(message);
        process.StandardInput.Close();

        var stderrBuilder = new StringBuilder();
        var stderrTask = CollectStderrAsync(process, stderrBuilder, token);

        var reader = process.StandardOutput;
        bool anyEvents = false;

        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(token); }
            catch (OperationCanceledException) { break; }

            if (string.IsNullOrWhiteSpace(line)) continue;

            StreamEvent? evt = null;
            try { evt = JsonSerializer.Deserialize<StreamEvent>(line); }
            catch (JsonException) { }

            if (evt != null)
            {
                anyEvents = true;
                yield return evt;
            }
        }

        await FinishProcess(process, token);
        try { await stderrTask; } catch { }

        var stderr = stderrBuilder.ToString().Trim();

        // Retry with --verbose if process failed before producing events
        if (!anyEvents && process.ExitCode != 0
            && stderr.Contains("requires --verbose", StringComparison.OrdinalIgnoreCase)
            && !caps.RequiresVerboseForStreamJson)
        {
            caps.RequiresVerboseForStreamJson = true;
            caps.SupportsVerbose = true;
            process.Dispose();

            arguments = BuildArguments(baseArgs, model, permissionMode, caps, conversationId, systemPrompt);
            process = StartProcess(fileName, arguments, workingDir);
            agent = new AgentProcess(process, cts);
            _agents[agentKey] = agent;

            await process.StandardInput.WriteAsync(message);
            process.StandardInput.Close();

            stderrBuilder.Clear();
            stderrTask = CollectStderrAsync(process, stderrBuilder, token);
            reader = process.StandardOutput;

            while (!reader.EndOfStream && !token.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync(token); }
                catch (OperationCanceledException) { break; }

                if (string.IsNullOrWhiteSpace(line)) continue;

                StreamEvent? evt = null;
                try { evt = JsonSerializer.Deserialize<StreamEvent>(line); }
                catch (JsonException) { }

                if (evt != null)
                    yield return evt;
            }

            await FinishProcess(process, token);
            try { await stderrTask; } catch { }
            stderr = stderrBuilder.ToString().Trim();
        }

        if (!string.IsNullOrEmpty(stderr) && process.ExitCode != 0)
        {
            yield return new StreamEvent
            {
                Type = "error",
                Error = stderr
            };
        }

        process.Dispose();
        _agents.TryRemove(agentKey, out _);
    }

    private static Process StartProcess(string fileName, string arguments, string workingDir)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                Environment = { ["NO_COLOR"] = "1" }
            }
        };

        process.Start();
        return process;
    }

    private static Task CollectStderrAsync(Process process, StringBuilder sb, CancellationToken token)
    {
        return Task.Run(async () =>
        {
            try
            {
                var errLine = await process.StandardError.ReadToEndAsync(token);
                if (!string.IsNullOrWhiteSpace(errLine))
                    sb.Append(errLine);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private static async Task FinishProcess(Process process, CancellationToken token)
    {
        if (token.IsCancellationRequested && process is { HasExited: false })
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }

        if (process is { HasExited: false })
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static string BuildArguments(
        string baseArgs,
        string model,
        string permissionMode,
        CliCapabilities caps,
        string? conversationId = null,
        string? systemPrompt = null)
    {
        var sb = new StringBuilder(baseArgs);
        sb.Append("--print --output-format stream-json ");
        if (caps.RequiresVerboseForStreamJson)
            sb.Append("--verbose ");
        sb.Append($"--model {model}");

        if (permissionMode == "plan")
            sb.Append(" --permission-mode plan");
        else if (permissionMode == "bypassAll")
            sb.Append(" --dangerously-skip-permissions");

        // Resume existing conversation
        if (!string.IsNullOrEmpty(conversationId))
            sb.Append($" --resume {conversationId}");

        // System prompt injection
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            var escaped = systemPrompt.Replace("\\", "\\\\").Replace("\"", "\\\"");
            sb.Append($" --append-system-prompt \"{escaped}\"");
        }

        return sb.ToString();
    }

    private async Task<CliCapabilities> DetectCapabilitiesAsync(string fileName, string baseArgs)
    {
        if (_capabilities != null)
            return _capabilities;

        await _capLock.WaitAsync();
        try
        {
            if (_capabilities != null)
                return _capabilities;

            var caps = new CliCapabilities();

            var version = await RunSimpleCommand(fileName, $"{baseArgs}--version");
            caps.Version = version?.Trim() ?? "";

            var help = await RunSimpleCommand(fileName, $"{baseArgs}--help");
            if (help != null)
            {
                caps.SupportsVerbose = help.Contains("--verbose", StringComparison.OrdinalIgnoreCase);
            }

            _capabilities = caps;
            return caps;
        }
        finally
        {
            _capLock.Release();
        }
    }

    private static async Task<string?> RunSimpleCommand(string fileName, string arguments)
    {
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Environment = { ["NO_COLOR"] = "1" }
                }
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            proc.Dispose();
            return output;
        }
        catch
        {
            return null;
        }
    }

    public void Cancel(string? sessionId = null)
    {
        var key = sessionId ?? DefaultAgentKey;
        if (_agents.TryRemove(key, out var agent))
        {
            agent.Cancel();
        }
    }

    private static (string fileName, string argPrefix) ResolveClaudeCommand(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && configuredPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                return ("cmd.exe", $"/c \"{configuredPath}\" ");
            }
            return (configuredPath, "");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var resolved = TryWhich("where.exe", "claude");
            if (resolved != null)
            {
                if (resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                    return ("cmd.exe", $"/c \"{resolved}\" ");
                return (resolved, "");
            }
            return ("claude.exe", "");
        }

        var path = TryWhich("/usr/bin/which", "claude");
        if (path != null)
            return (path, "");

        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm", "bin", "claude"),
            "/usr/local/bin/claude",
            "/opt/homebrew/bin/claude"
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return (candidate, "");
        }

        return ("claude", "");
    }

    private static string? TryWhich(string whichCommand, string target)
    {
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = whichCommand,
                    Arguments = target,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadLine()?.Trim();
            proc.WaitForExit(3000);
            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                return output;
        }
        catch { }

        return null;
    }

    private sealed class AgentProcess
    {
        private readonly Process _process;
        private readonly CancellationTokenSource _cts;

        public AgentProcess(Process process, CancellationTokenSource cts)
        {
            _process = process;
            _cts = cts;
        }

        public void Cancel()
        {
            _cts.Cancel();
            if (_process is { HasExited: false })
            {
                try { _process.Kill(entireProcessTree: true); } catch { }
                _process.Dispose();
            }
        }
    }
}
