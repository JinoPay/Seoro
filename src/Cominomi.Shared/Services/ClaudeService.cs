using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Services;

public class ClaudeService(
    IOptionsMonitor<AppSettings> appSettings,
    IShellService shellService,
    IProcessRunner processRunner,
    ILogger<ClaudeService> logger)
    : IClaudeService
{
    private const string DefaultAgentKey = "__default__";
    private static readonly TimeSpan GracefulShutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly ClaudeCliResolver _cliResolver = new(shellService, processRunner, logger);
    private readonly ConcurrentDictionary<string, AgentProcess> _agents = new();
    private readonly SemaphoreSlim _capLock = new(1, 1);
    private bool _disposed;
    private CliCapabilities? _capabilities;

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
        logger.LogInformation("Starting Claude process for session {AgentKey} with model {Model}", agentKey, model);

        var settings = appSettings.CurrentValue;
        var (fileName, baseArgs) = await _cliResolver.ResolveAsync(settings.ClaudePath);
        var caps = await DetectCapabilitiesAsync(fileName, baseArgs);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Cancel any previous process for this specific session
        if (_agents.TryRemove(agentKey, out var previous)) previous.Cancel();

        var arguments = ClaudeArgumentBuilder.Build(baseArgs, model, permissionMode, caps, conversationId, systemPrompt,
            effortLevel, continueMode, forkSession, maxTurns, maxBudgetUsd, settings.FallbackModel,
            settings.McpConfigPath, settings.DebugMode, additionalDirs, allowedTools, disallowedTools);
        var token = cts.Token;
        var envVars = settings.EnvironmentVariables.Count > 0 ? settings.EnvironmentVariables : null;

        // First attempt
        var ctx = new StreamingContext();
        await foreach (var evt in ExecuteClaudeProcessAsync(fileName, arguments, workingDir, message, continueMode,
                           agentKey, cts, envVars, ctx, token)) yield return evt;

        // Retry with --verbose if process failed before producing events
        if (!ctx.AnyEvents && ctx.ExitCode != 0
                           && ctx.Stderr.Contains("requires --verbose", StringComparison.OrdinalIgnoreCase)
                           && !caps.RequiresVerboseForStreamJson)
        {
            logger.LogInformation("Retrying Claude process with --verbose flag");
            caps.RequiresVerboseForStreamJson = true;
            caps.SupportsVerbose = true;

            arguments = ClaudeArgumentBuilder.Build(baseArgs, model, permissionMode, caps, conversationId, systemPrompt,
                effortLevel, continueMode, forkSession, maxTurns, maxBudgetUsd, settings.FallbackModel,
                settings.McpConfigPath, settings.DebugMode, additionalDirs, allowedTools, disallowedTools);

            ctx = new StreamingContext();
            await foreach (var evt in ExecuteClaudeProcessAsync(fileName, arguments, workingDir, message, continueMode,
                               agentKey, cts, envVars, ctx, token)) yield return evt;
        }

        // Emit error event for stderr errors
        if (!string.IsNullOrEmpty(ctx.Stderr) && ctx.ExitCode != 0)
        {
            logger.LogWarning("Claude process exited with code {ExitCode}: {Stderr}", ctx.ExitCode, ctx.Stderr);
            yield return new StreamEvent
            {
                Type = "error",
                Error = JsonSerializer.SerializeToElement(ctx.Stderr)
            };
        }
        // Crash recovery: non-zero exit with partial events but no stderr
        else if (ctx.AnyEvents && ctx.ExitCode != 0)
        {
            logger.LogWarning("Claude process terminated unexpectedly with exit code {ExitCode} after emitting events",
                ctx.ExitCode);
            yield return new StreamEvent
            {
                Type = "error",
                Error = JsonSerializer.SerializeToElement(
                    $"Claude process terminated unexpectedly (exit code {ctx.ExitCode}). Response may be incomplete.")
            };
        }

        _agents.TryRemove(agentKey, out _);
    }

    public async Task<(bool found, string resolvedPath)> DetectCliAsync()
    {
        try
        {
            var settings = appSettings.CurrentValue;
            var result = await _cliResolver.DetectAsync(settings.ClaudePath);
            if (result is null)
                return (false, "");
            return (true, result.Value.fileName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to detect Claude CLI");
            return (false, "");
        }
    }

    public async Task<string?> GenerateCommitMessageAsync(string diff, string workingDir)
    {
        try
        {
            var settings = appSettings.CurrentValue;
            var (fileName, baseArgs) = await _cliResolver.ResolveAsync(settings.ClaudePath);
            var loginPath = await shellService.GetLoginShellPathAsync();

            var sb = new StringBuilder(baseArgs);
            sb.Append("--print --output-format text ");
            sb.Append("--model haiku ");
            sb.Append("--dangerously-skip-permissions ");
            sb.Append(
                "--append-system-prompt \"You are a commit message generator. Given a unified diff, write a single concise commit message in the imperative mood. Use the same language as code comments or strings if present, otherwise use English. Output ONLY the commit message, nothing else. No prefix like feat: or fix:.\"");

            var finalArgs = sb.ToString();
            if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
                && finalArgs.StartsWith("/c ", StringComparison.OrdinalIgnoreCase))
                finalArgs = $"/c \"{finalArgs[3..].TrimEnd()}\"";

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = finalArgs,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["NO_COLOR"] = "1";
            if (loginPath != null)
                psi.Environment["PATH"] = loginPath;

            var process = new Process { StartInfo = psi };

            logger.LogDebug("GenerateCommitMessage: {FileName} {Arguments}", fileName, finalArgs);
            process.Start();

            const int maxDiffLength = 8000;
            var truncatedDiff = diff.Length > maxDiffLength ? diff[..maxDiffLength] + "\n... (truncated)" : diff;
            await process.StandardInput.WriteAsync(truncatedDiff);
            process.StandardInput.Close();

            var output = await process.StandardOutput.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    /* best-effort */
                }
            }

            process.Dispose();

            var msg = output.Trim();
            if (string.IsNullOrEmpty(msg))
                return null;

            var firstLine = msg.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(firstLine) ? null : firstLine;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate commit message");
            return null;
        }
    }

    public async Task<string?> GetDetectedVersionAsync()
    {
        try
        {
            var settings = appSettings.CurrentValue;
            var (fileName, baseArgs) = await _cliResolver.ResolveAsync(settings.ClaudePath);
            var caps = await DetectCapabilitiesAsync(fileName, baseArgs);
            return string.IsNullOrEmpty(caps.Version) ? null : caps.Version;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Claude CLI version detection failed");
            return null;
        }
    }

    public void Cancel(string? sessionId = null)
    {
        var key = sessionId ?? DefaultAgentKey;
        logger.LogInformation("Cancelling Claude process for session {AgentKey}", key);
        if (_agents.TryRemove(key, out var agent)) agent.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var key in _agents.Keys.ToList())
            if (_agents.TryRemove(key, out var agent))
            {
                logger.LogInformation("Shutting down Claude process for session {AgentKey}", key);
                agent.Cancel();
                agent.Dispose();
            }

        _capLock.Dispose();
    }

    private static Process StartProcess(string fileName, string arguments, string workingDir,
        Dictionary<string, string>? envVars = null, string? loginShellPath = null)
    {
        // cmd.exe /c strips the first and last quote characters from the argument string,
        // which leaves special characters like parentheses (e.g. in "Bash(git branch*)")
        // unprotected. Wrapping in an extra pair of outer quotes ensures inner quotes survive.
        if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
            && arguments.StartsWith("/c ", StringComparison.OrdinalIgnoreCase))
            arguments = $"/c \"{arguments[3..].TrimEnd()}\"";

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
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.Environment["NO_COLOR"] = "1";

        // Inject login shell PATH so tool version managers' binaries (node, mise, etc.)
        // are accessible even when the GUI app inherits a minimal PATH.
        if (loginShellPath != null)
            psi.Environment["PATH"] = loginShellPath;

        if (envVars != null)
            foreach (var (key, value) in envVars)
                psi.Environment[key] = value;

        var process = new Process { StartInfo = psi };
        process.Start();
        return process;
    }

    private static async Task FinishProcess(Process process, CancellationToken token)
    {
        try
        {
            if (token.IsCancellationRequested && process is { HasExited: false })
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    /* best-effort: process may have already exited */
                }

            if (process is { HasExited: false })
            {
                using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await process.WaitForExitAsync(exitCts.Token);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                        /* best-effort: process may have already exited */
                    }
                }
            }
        }
        catch
        {
            /* process already disposed by cancellation */
        }
    }

    private async IAsyncEnumerable<StreamEvent> ExecuteClaudeProcessAsync(
        string fileName, string arguments, string workingDir, string message,
        bool continueMode, string agentKey, CancellationTokenSource cts,
        Dictionary<string, string>? envVars, StreamingContext ctx,
        [EnumeratorCancellation] CancellationToken token)
    {
        logger.LogDebug("Executing: {FileName} {Arguments}", fileName, arguments);
        var loginPath = await shellService.GetLoginShellPathAsync();
        var process = StartProcess(fileName, arguments, workingDir, envVars, loginPath);
        var agent = new AgentProcess(process, cts, logger);
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
        try
        {
            await stderrTask;
        }
        catch (OperationCanceledException)
        {
            /* expected on cancellation */
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stderr collection task failed unexpectedly");
        }

        try
        {
            ctx.ExitCode = process.ExitCode;
        }
        catch
        {
            /* process disposed during cancellation */
        }

        ctx.Stderr = stderrBuilder.ToString().Trim();

        try
        {
            process.Dispose();
        }
        catch
        {
            /* process may already be disposed */
        }
    }

    private async IAsyncEnumerable<StreamEvent> ReadStreamEventsAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line)) continue;

            logger.LogDebug("Claude raw line: {Line}", line);

            StreamEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<StreamEvent>(line);
            }
            catch (JsonException)
            {
                logger.LogDebug("Skipping non-JSON line from Claude CLI");
            }

            if (evt != null)
                yield return evt;
        }
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
            catch (OperationCanceledException)
            {
                /* expected: stream cancelled during shutdown */
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Stderr collection encountered an error");
            }
        }, token);
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
            if (help != null) caps.SupportsVerbose = help.Contains("--verbose", StringComparison.OrdinalIgnoreCase);

            logger.LogInformation("Claude CLI detected: version={Version}, verbose={SupportsVerbose}", caps.Version,
                caps.SupportsVerbose);
            _capabilities = caps;
            return caps;
        }
        finally
        {
            _capLock.Release();
        }
    }

    private sealed class AgentProcess : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly ILogger _logger;
        private readonly Process _process;

        public AgentProcess(Process process, CancellationTokenSource cts, ILogger logger)
        {
            _process = process;
            _cts = cts;
            _logger = logger;
        }

        public void Dispose()
        {
            _cts.Dispose();
            try
            {
                _process.Dispose();
            }
            catch
            {
                /* best-effort cleanup */
            }
        }

        public void Cancel()
        {
            _cts.Cancel();
            try
            {
                if (_process is { HasExited: false })
                    // Graceful shutdown: wait briefly for process to exit on its own after CTS cancel
                    if (!_process.WaitForExit(GracefulShutdownTimeout))
                    {
                        _logger.LogDebug("Process did not exit gracefully, sending kill signal");
                        try
                        {
                            _process.Kill(true);
                        }
                        catch
                        {
                            /* best-effort: process may have already exited */
                        }
                    }
            }
            catch
            {
                /* process already disposed */
            }
        }
    }

    private sealed class StreamingContext
    {
        public bool AnyEvents;
        public int ExitCode;
        public string Stderr = "";
    }
}