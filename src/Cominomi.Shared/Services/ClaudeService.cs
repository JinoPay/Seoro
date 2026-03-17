using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ClaudeService : IClaudeService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ClaudeService> _logger;
    private readonly ClaudeCliResolver _cliResolver;
    private readonly ConcurrentDictionary<string, AgentProcess> _agents = new();
    private CliCapabilities? _capabilities;
    private readonly SemaphoreSlim _capLock = new(1, 1);

    private const string DefaultAgentKey = "__default__";

    public ClaudeService(ISettingsService settingsService, IShellService shellService, ILogger<ClaudeService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        _cliResolver = new ClaudeCliResolver(shellService, logger);
    }

    public async IAsyncEnumerable<StreamEvent> SendMessageAsync(
        string message,
        string workingDir,
        string model,
        string permissionMode = "bypassAll",
        string effortLevel = "auto",
        string? sessionId = null,
        string? conversationId = null,
        string? systemPrompt = null,
        bool continueMode = false,
        bool forkSession = false,
        int? maxTurns = null,
        decimal? maxBudgetUsd = null,
        List<string>? additionalDirs = null,
        List<string>? allowedTools = null,
        List<string>? disallowedTools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agentKey = sessionId ?? DefaultAgentKey;
        _logger.LogInformation("Starting Claude process for session {AgentKey} with model {Model}", agentKey, model);

        var settings = await _settingsService.LoadAsync();
        var (fileName, baseArgs) = await _cliResolver.ResolveAsync(settings.ClaudePath);
        var caps = await DetectCapabilitiesAsync(fileName, baseArgs);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Cancel any previous process for this specific session
        if (_agents.TryRemove(agentKey, out var previous))
        {
            previous.Cancel();
        }

        var arguments = ClaudeArgumentBuilder.Build(baseArgs, model, permissionMode, caps, conversationId, systemPrompt, effortLevel, continueMode, forkSession, maxTurns, maxBudgetUsd, settings.FallbackModel, settings.McpConfigPath, settings.DebugMode, additionalDirs, allowedTools, disallowedTools);
        var token = cts.Token;
        var envVars = settings.EnvironmentVariables.Count > 0 ? settings.EnvironmentVariables : null;

        _logger.LogDebug("Executing: {FileName} {Arguments}", fileName, arguments);
        var process = StartProcess(fileName, arguments, workingDir, envVars);
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

            _logger.LogDebug("Claude raw line: {Line}", line);

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

            arguments = ClaudeArgumentBuilder.Build(baseArgs, model, permissionMode, caps, conversationId, systemPrompt, effortLevel, continueMode, forkSession, maxTurns, maxBudgetUsd, settings.FallbackModel, settings.McpConfigPath, settings.DebugMode, additionalDirs, allowedTools, disallowedTools);
            _logger.LogDebug("Executing (retry): {FileName} {Arguments}", fileName, arguments);
            process = StartProcess(fileName, arguments, workingDir, envVars);
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
                Error = JsonSerializer.SerializeToElement(stderr)
            };
        }

        process.Dispose();
        _agents.TryRemove(agentKey, out _);
    }

    private static Process StartProcess(string fileName, string arguments, string workingDir, Dictionary<string, string>? envVars = null)
    {
        var psi = new ProcessStartInfo
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
        };

        psi.Environment["NO_COLOR"] = "1";

        if (envVars != null)
        {
            foreach (var (key, value) in envVars)
                psi.Environment[key] = value;
        }

        var process = new Process { StartInfo = psi };
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

            var version = await _cliResolver.RunSimpleCommandAsync(fileName, $"{baseArgs}--version");
            caps.Version = version?.Trim() ?? "";

            var help = await _cliResolver.RunSimpleCommandAsync(fileName, $"{baseArgs}--help");
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

    public async Task<(bool found, string resolvedPath)> DetectCliAsync()
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            var (fileName, _) = await _cliResolver.ResolveAsync(settings.ClaudePath);
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

    public async Task<string?> SummarizeAsync(string message, string workingDir)
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            var (fileName, baseArgs) = await _cliResolver.ResolveAsync(settings.ClaudePath);

            var sb = new StringBuilder(baseArgs);
            sb.Append("--print --output-format text ");
            sb.Append("--model haiku ");
            sb.Append("--dangerously-skip-permissions ");
            sb.Append("--append-system-prompt \"Generate a short, natural title for this chat (3-7 words). Use the same language as the user's message. Use Title Case (capitalize each word). Output only the title text, nothing else.\"");

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

            _logger.LogDebug("Summarize: {FileName} {Arguments}", fileName, sb.ToString());
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
