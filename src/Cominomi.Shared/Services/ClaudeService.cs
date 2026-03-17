using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ClaudeService : IClaudeService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ClaudeService> _logger;
    private readonly ConcurrentDictionary<string, AgentProcess> _agents = new();
    private CliCapabilities? _capabilities;
    private readonly SemaphoreSlim _capLock = new(1, 1);

    private (string fileName, string argPrefix)? _resolvedCommand;
    private string? _resolvedCommandPath;
    private readonly SemaphoreSlim _resolveLock = new(1, 1);

    private const string DefaultAgentKey = "__default__";

    public ClaudeService(ISettingsService settingsService, ILogger<ClaudeService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async IAsyncEnumerable<StreamEvent> SendMessageAsync(
        string message,
        string workingDir,
        string model,
        string permissionMode = "bypassAll",
        bool thinkingEnabled = false,
        string? sessionId = null,
        string? conversationId = null,
        string? systemPrompt = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agentKey = sessionId ?? DefaultAgentKey;
        _logger.LogInformation("Starting Claude process for session {AgentKey} with model {Model}", agentKey, model);

        var settings = await _settingsService.LoadAsync();
        var (fileName, baseArgs) = await ResolveClaudeCommandCachedAsync(settings.ClaudePath);
        var caps = await DetectCapabilitiesAsync(fileName, baseArgs);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Cancel any previous process for this specific session
        if (_agents.TryRemove(agentKey, out var previous))
        {
            previous.Cancel();
        }

        var arguments = BuildArguments(baseArgs, model, permissionMode, caps, conversationId, systemPrompt, thinkingEnabled);
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
            catch (JsonException) { _logger.LogDebug("Skipping non-JSON line from Claude CLI"); }

            if (evt != null)
            {
                anyEvents = true;
                yield return evt;
            }
        }

        await FinishProcess(process, token);
        try { await stderrTask; } catch (Exception ex) { _logger.LogDebug(ex, "Stderr collection task ended"); }

        var stderr = stderrBuilder.ToString().Trim();

        // Retry with --verbose if process failed before producing events
        if (!anyEvents && process.ExitCode != 0
            && stderr.Contains("requires --verbose", StringComparison.OrdinalIgnoreCase)
            && !caps.RequiresVerboseForStreamJson)
        {
            _logger.LogInformation("Retrying Claude process with --verbose flag");
            caps.RequiresVerboseForStreamJson = true;
            caps.SupportsVerbose = true;
            process.Dispose();

            arguments = BuildArguments(baseArgs, model, permissionMode, caps, conversationId, systemPrompt, thinkingEnabled);
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
                catch (JsonException) { _logger.LogDebug("Skipping non-JSON line from Claude CLI (retry)"); }

                if (evt != null)
                    yield return evt;
            }

            await FinishProcess(process, token);
            try { await stderrTask; } catch (Exception ex) { _logger.LogDebug(ex, "Stderr collection task ended (retry)"); }
            stderr = stderrBuilder.ToString().Trim();
        }

        if (!string.IsNullOrEmpty(stderr) && process.ExitCode != 0)
        {
            _logger.LogWarning("Claude process exited with code {ExitCode}: {Stderr}", process.ExitCode, stderr);
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
        string? systemPrompt = null,
        bool thinkingEnabled = false)
    {
        var sb = new StringBuilder(baseArgs);
        sb.Append("--print --output-format stream-json ");
        if (caps.SupportsVerbose)
            sb.Append("--verbose ");
        sb.Append($"--model {model}");

        if (permissionMode == "plan")
            sb.Append(" --permission-mode plan");
        else if (permissionMode == "bypassAll")
            sb.Append(" --dangerously-skip-permissions");

        if (thinkingEnabled)
            sb.Append(" --effort max");

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

            _logger.LogInformation("Claude CLI detected: version={Version}, verbose={SupportsVerbose}", caps.Version, caps.SupportsVerbose);
            _capabilities = caps;
            return caps;
        }
        finally
        {
            _capLock.Release();
        }
    }

    private async Task<string?> RunSimpleCommand(string fileName, string arguments)
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run simple command: {FileName} {Args}", fileName, arguments);
            return null;
        }
    }

    public async Task<(bool found, string resolvedPath)> DetectCliAsync()
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            var (fileName, _) = await ResolveClaudeCommandCachedAsync(settings.ClaudePath);
            return (true, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect Claude CLI");
            return (false, "");
        }
    }

    public void Cancel(string? sessionId = null)
    {
        var key = sessionId ?? DefaultAgentKey;
        _logger.LogInformation("Cancelling Claude process for session {AgentKey}", key);
        if (_agents.TryRemove(key, out var agent))
        {
            agent.Cancel();
        }
    }

    private async Task<(string fileName, string argPrefix)> ResolveClaudeCommandCachedAsync(string? configuredPath)
    {
        await _resolveLock.WaitAsync();
        try
        {
            if (_resolvedCommand.HasValue && _resolvedCommandPath == configuredPath)
                return _resolvedCommand.Value;

            var result = await ResolveClaudeCommandAsync(configuredPath);
            _resolvedCommand = result;
            _resolvedCommandPath = configuredPath;
            return result;
        }
        finally
        {
            _resolveLock.Release();
        }
    }

    private static async Task<(string fileName, string argPrefix)> ResolveClaudeCommandAsync(string? configuredPath)
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
            var resolved = await TryWhichAsync("where.exe", "claude");
            if (resolved != null)
            {
                if (resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                    return ("cmd.exe", $"/c \"{resolved}\" ");
                return (resolved, "");
            }
            return ("claude.exe", "");
        }

        var path = await TryWhichAsync("/usr/bin/which", "claude");
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

    private static async Task<string?> TryWhichAsync(string whichCommand, string target)
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
            var output = (await proc.StandardOutput.ReadLineAsync())?.Trim();
            using var cts = new CancellationTokenSource(3000);
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { proc.Kill(); } catch { } }
            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                return output;
        }
        catch { }

        return null;
    }

    public async Task<string?> SummarizeAsync(string message, string workingDir)
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            var (fileName, baseArgs) = await ResolveClaudeCommandCachedAsync(settings.ClaudePath);

            var sb = new StringBuilder(baseArgs);
            sb.Append("--print --output-format text ");
            sb.Append("--model haiku ");
            sb.Append("--dangerously-skip-permissions ");
            sb.Append("--append-system-prompt \"Summarize the user message into a concise title (max 5 words, English). Output only the title, nothing else.\"");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = sb.ToString(),
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
            await process.StandardInput.WriteAsync(message);
            process.StandardInput.Close();

            var output = await process.StandardOutput.ReadToEndAsync();
            using var cts = new CancellationTokenSource(15000);
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch { } }
            process.Dispose();

            var summary = output.Trim();
            if (string.IsNullOrEmpty(summary))
                return null;

            // Take only the first line in case of extra output
            var firstLine = summary.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(firstLine) ? null : firstLine;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to summarize message with Haiku");
            return null;
        }
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
