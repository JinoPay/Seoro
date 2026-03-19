using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Cominomi.Shared;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Services;

public class ClaudeService : IClaudeService, IDisposable
{
    private readonly IOptionsMonitor<AppSettings> _appSettings;
    private readonly ILogger<ClaudeService> _logger;
    private readonly ClaudeCliResolver _cliResolver;
    private readonly ConcurrentDictionary<string, AgentProcess> _agents = new();
    private CliCapabilities? _capabilities;
    private readonly SemaphoreSlim _capLock = new(1, 1);
    private bool _disposed;

    private const string DefaultAgentKey = "__default__";
    private static readonly TimeSpan GracefulShutdownTimeout = TimeSpan.FromSeconds(2);

    public ClaudeService(IOptionsMonitor<AppSettings> appSettings, IShellService shellService, IProcessRunner processRunner, ILogger<ClaudeService> logger)
    {
        _appSettings = appSettings;
        _logger = logger;
        _cliResolver = new ClaudeCliResolver(shellService, processRunner, logger);
    }

    public async IAsyncEnumerable<StreamEvent> SendMessageAsync(
        string message,
        string workingDir,
        string model,
        string permissionMode = CominomiConstants.DefaultPermissionMode,
        string effortLevel = CominomiConstants.DefaultEffortLevel,
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

        var settings = _appSettings.CurrentValue;
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

        // First attempt
        var ctx = new StreamingContext();
        await foreach (var evt in ExecuteClaudeProcessAsync(fileName, arguments, workingDir, message, continueMode, agentKey, cts, envVars, ctx, token))
        {
            yield return evt;
        }

        // Retry with --verbose if process failed before producing events
        if (!ctx.AnyEvents && ctx.ExitCode != 0
            && ctx.Stderr.Contains("requires --verbose", StringComparison.OrdinalIgnoreCase)
            && !caps.RequiresVerboseForStreamJson)
        {
            _logger.LogInformation("Retrying Claude process with --verbose flag");
            caps.RequiresVerboseForStreamJson = true;
            caps.SupportsVerbose = true;

            arguments = ClaudeArgumentBuilder.Build(baseArgs, model, permissionMode, caps, conversationId, systemPrompt, effortLevel, continueMode, forkSession, maxTurns, maxBudgetUsd, settings.FallbackModel, settings.McpConfigPath, settings.DebugMode, additionalDirs, allowedTools, disallowedTools);

            ctx = new StreamingContext();
            await foreach (var evt in ExecuteClaudeProcessAsync(fileName, arguments, workingDir, message, continueMode, agentKey, cts, envVars, ctx, token))
            {
                yield return evt;
            }
        }

        // Emit error event for stderr errors
        if (!string.IsNullOrEmpty(ctx.Stderr) && ctx.ExitCode != 0)
        {
            _logger.LogWarning("Claude process exited with code {ExitCode}: {Stderr}", ctx.ExitCode, ctx.Stderr);
            yield return new StreamEvent
            {
                Type = "error",
                Error = JsonSerializer.SerializeToElement(ctx.Stderr)
            };
        }
        // Crash recovery: non-zero exit with partial events but no stderr
        else if (ctx.AnyEvents && ctx.ExitCode != 0)
        {
            _logger.LogWarning("Claude process terminated unexpectedly with exit code {ExitCode} after emitting events", ctx.ExitCode);
            yield return new StreamEvent
            {
                Type = "error",
                Error = JsonSerializer.SerializeToElement(
                    $"Claude process terminated unexpectedly (exit code {ctx.ExitCode}). Response may be incomplete.")
            };
        }

        _agents.TryRemove(agentKey, out _);
    }

    private async IAsyncEnumerable<StreamEvent> ExecuteClaudeProcessAsync(
        string fileName, string arguments, string workingDir, string message,
        bool continueMode, string agentKey, CancellationTokenSource cts,
        Dictionary<string, string>? envVars, StreamingContext ctx,
        [EnumeratorCancellation] CancellationToken token)
    {
        _logger.LogDebug("Executing: {FileName} {Arguments}", fileName, arguments);
        var process = StartProcess(fileName, arguments, workingDir, envVars);
        var agent = new AgentProcess(process, cts, _logger);
        _agents[agentKey] = agent;

        // In continue mode, close stdin immediately without writing a message
        if (!continueMode)
            await process.StandardInput.WriteAsync(message);
        process.StandardInput.Close();

        var stderrBuilder = new StringBuilder();
        var stderrTask = CollectStderrAsync(process, stderrBuilder, token);

        var reader = process.StandardOutput;

        await foreach (var evt in ReadStreamEventsAsync(reader, token))
        {
            ctx.AnyEvents = true;
            yield return evt;
        }

        await FinishProcess(process, token);
        try { await stderrTask; }
        catch (OperationCanceledException) { /* expected on cancellation */ }
        catch (Exception ex) { _logger.LogWarning(ex, "Stderr collection task failed unexpectedly"); }

        try { ctx.ExitCode = process.ExitCode; } catch { /* process disposed during cancellation */ }
        ctx.Stderr = stderrBuilder.ToString().Trim();

        try { process.Dispose(); } catch { /* process may already be disposed */ }
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

    private Task CollectStderrAsync(Process process, StringBuilder sb, CancellationToken token)
    {
        return Task.Run(async () =>
        {
            try
            {
                var errLine = await process.StandardError.ReadToEndAsync(token);
                if (!string.IsNullOrWhiteSpace(errLine))
                    sb.Append(errLine);
            }
            catch (OperationCanceledException) { /* expected: stream cancelled during shutdown */ }
            catch (Exception ex) { _logger.LogWarning(ex, "Stderr collection encountered an error"); }
        }, token);
    }

    private static async Task FinishProcess(Process process, CancellationToken token)
    {
        try
        {
            if (token.IsCancellationRequested && process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort: process may have already exited */ }
            }

            if (process is { HasExited: false })
            {
                using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await process.WaitForExitAsync(exitCts.Token); }
                catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch { /* best-effort: process may have already exited */ } }
            }
        }
        catch { /* process already disposed by cancellation */ }
    }

    private async IAsyncEnumerable<StreamEvent> ReadStreamEventsAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
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
                yield return evt;
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
            var settings = _appSettings.CurrentValue;
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
            var settings = _appSettings.CurrentValue;
            var (fileName, baseArgs) = await _cliResolver.ResolveAsync(settings.ClaudePath);

            var sb = new StringBuilder(baseArgs);
            sb.Append("--print --output-format text ");
            sb.Append($"--model {settings.SummarizationModel} ");
            sb.Append("--dangerously-skip-permissions ");
            sb.Append($"--append-system-prompt \"{settings.SummarizationPrompt}\"");

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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.SummarizationTimeoutSeconds));
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch { /* best-effort: process may have already exited */ } }
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var key in _agents.Keys.ToList())
        {
            if (_agents.TryRemove(key, out var agent))
            {
                _logger.LogInformation("Shutting down Claude process for session {AgentKey}", key);
                agent.Cancel();
                agent.Dispose();
            }
        }

        _capLock.Dispose();
    }

    private sealed class StreamingContext
    {
        public bool AnyEvents;
        public int ExitCode;
        public string Stderr = "";
    }

    private sealed class AgentProcess : IDisposable
    {
        private readonly Process _process;
        private readonly CancellationTokenSource _cts;
        private readonly ILogger _logger;

        public AgentProcess(Process process, CancellationTokenSource cts, ILogger logger)
        {
            _process = process;
            _cts = cts;
            _logger = logger;
        }

        public void Cancel()
        {
            _cts.Cancel();
            try
            {
                if (_process is { HasExited: false })
                {
                    // Graceful shutdown: wait briefly for process to exit on its own after CTS cancel
                    if (!_process.WaitForExit(GracefulShutdownTimeout))
                    {
                        _logger.LogDebug("Process did not exit gracefully, sending kill signal");
                        try { _process.Kill(entireProcessTree: true); } catch { /* best-effort: process may have already exited */ }
                    }
                }
            }
            catch { /* process already disposed */ }
        }

        public void Dispose()
        {
            _cts.Dispose();
            try { _process.Dispose(); } catch { /* best-effort cleanup */ }
        }
    }
}
