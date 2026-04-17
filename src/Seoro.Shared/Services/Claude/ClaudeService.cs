using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace Seoro.Shared.Services.Claude;

public class ClaudeService(
    IOptionsMonitor<AppSettings> appSettings,
    IShellService shellService,
    IProcessRunner processRunner,
    ILogger<ClaudeService> logger)
    : IClaudeService, ICliProvider
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
        string permissionMode = SeoroConstants.DefaultPermissionMode,
        string effortLevel = SeoroConstants.DefaultEffortLevel,
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
        logger.LogInformation("세션 {AgentKey}에 대해 Claude 프로세스 시작, 모델: {Model}", agentKey, model);

        var settings = appSettings.CurrentValue;
        var (fileName, baseArgs) = await _cliResolver.ResolveAsync(settings.ClaudePath);
        var caps = await DetectCapabilitiesAsync(fileName, baseArgs);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // 이 특정 세션의 이전 프로세스 취소
        if (_agents.TryRemove(agentKey, out var previous)) previous.Cancel();

        var arguments = ClaudeArgumentBuilder.Build(baseArgs, model, permissionMode, caps, conversationId, systemPrompt,
            effortLevel, continueMode, forkSession, maxTurns, maxBudgetUsd, settings.FallbackModel,
            settings.McpConfigPath, settings.DebugMode, additionalDirs, allowedTools, disallowedTools);
        var token = cts.Token;
        var envVars = settings.EnvironmentVariables.Count > 0 ? settings.EnvironmentVariables : null;

        // 첫 번째 시도
        var ctx = new StreamingContext();
        await foreach (var evt in ExecuteClaudeProcessAsync(fileName, arguments, workingDir, message, continueMode,
                           agentKey, cts, envVars, ctx, token)) yield return evt;

        // 이벤트 생성 전에 프로세스가 실패한 경우 --verbose 플래그로 재시도
        if (!ctx.AnyEvents && ctx.ExitCode != 0
                           && ctx.Stderr.Contains("requires --verbose", StringComparison.OrdinalIgnoreCase)
                           && !caps.RequiresVerboseForStreamJson)
        {
            logger.LogInformation("--verbose 플래그로 Claude 프로세스 재시도 중");
            caps.RequiresVerboseForStreamJson = true;
            caps.SupportsVerbose = true;

            arguments = ClaudeArgumentBuilder.Build(baseArgs, model, permissionMode, caps, conversationId, systemPrompt,
                effortLevel, continueMode, forkSession, maxTurns, maxBudgetUsd, settings.FallbackModel,
                settings.McpConfigPath, settings.DebugMode, additionalDirs, allowedTools, disallowedTools);

            ctx = new StreamingContext();
            await foreach (var evt in ExecuteClaudeProcessAsync(fileName, arguments, workingDir, message, continueMode,
                               agentKey, cts, envVars, ctx, token)) yield return evt;
        }

        // stderr 오류에 대한 오류 이벤트 발생
        if (!string.IsNullOrEmpty(ctx.Stderr) && ctx.ExitCode != 0)
        {
            logger.LogWarning("Claude 프로세스가 코드 {ExitCode}로 종료됨: {Stderr}", ctx.ExitCode, ctx.Stderr);
            yield return new StreamEvent
            {
                Type = "error",
                Error = JsonSerializer.SerializeToElement(ctx.Stderr)
            };
        }
        // 크래시 복구: 부분 이벤트는 있지만 stderr가 없는 0이 아닌 종료
        else if (ctx.AnyEvents && ctx.ExitCode != 0)
        {
            logger.LogWarning("Claude 프로세스가 이벤트 발생 후 예기치 않게 종료됨, 종료 코드: {ExitCode}",
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
            logger.LogWarning(ex, "Claude CLI 감지 실패");
            return (false, "");
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
            logger.LogDebug(ex, "Claude CLI 버전 감지 실패");
            return null;
        }
    }

    public void Cancel(string? sessionId = null)
    {
        var key = sessionId ?? DefaultAgentKey;
        logger.LogInformation("세션 {AgentKey}의 Claude 프로세스 취소 중", key);
        if (_agents.TryRemove(key, out var agent)) agent.Cancel();
    }

    // ──────────────────────────────────────────────
    //  ICliProvider 구현 (Claude 프로바이더)
    // ──────────────────────────────────────────────

    public string ProviderId => "claude";
    public string DisplayName => "Claude";

    public ProviderCapabilities Capabilities { get; } = new()
    {
        SupportsEffortLevel = true,
        SupportsForkSession = true,
        SupportsPlanMode = true,
        SupportsToolFiltering = true,
        SupportsMaxBudget = true,
        SupportsFallbackModel = true,
        SupportsImageAttachment = true,
        SupportsWebSearch = true,
        SupportsMcp = true,
    };

    async IAsyncEnumerable<StreamEvent> ICliProvider.SendMessageAsync(
        CliSendOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in SendMessageAsync(
                           options.Message,
                           options.WorkingDir,
                           options.Model,
                           options.PermissionMode,
                           options.EffortLevel,
                           options.SessionId,
                           options.ConversationId,
                           options.SystemPrompt,
                           options.ContinueMode,
                           options.ForkSession,
                           options.MaxTurns,
                           options.MaxBudgetUsd,
                           options.AdditionalDirs,
                           options.AllowedTools,
                           options.DisallowedTools,
                           ct))
            yield return evt;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var key in _agents.Keys.ToList())
            if (_agents.TryRemove(key, out var agent))
            {
                logger.LogInformation("세션 {AgentKey}의 Claude 프로세스 종료 중", key);
                agent.Cancel();
                agent.Dispose();
            }

        _capLock.Dispose();
    }

    private static Process StartProcess(string fileName, string arguments, string workingDir,
        Dictionary<string, string>? envVars = null, string? loginShellPath = null)
    {
        // cmd.exe /c는 인자 문자열의 첫 번째와 마지막 따옴표를 제거하므로,
        // "Bash(git branch*)" 같은 특수 문자가 보호되지 않습니다.
        // 외부 따옴표를 추가로 감싸면 내부 따옴표가 유지됩니다.
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

        // 로그인 셸 PATH를 주입하여 도구 버전 관리자 바이너리(node, mise 등)가
        // GUI 앱이 최소 PATH를 상속받아도 접근 가능하도록 합니다.
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
        logger.LogDebug("실행 중: {FileName} {Arguments}", fileName, arguments);
        var loginPath = await shellService.GetLoginShellPathAsync();
        var process = StartProcess(fileName, arguments, workingDir, envVars, loginPath);
        var agent = new AgentProcess(process, cts, logger);
        _agents[agentKey] = agent;

        // continue 모드에서는 메시지를 작성하지 않고 stdin을 즉시 닫음
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
            logger.LogWarning(ex, "stderr 수집 작업이 예기치 않게 실패함");
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

            logger.LogDebug("Claude 원본 라인: {Line}", line);

            StreamEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<StreamEvent>(line);
            }
            catch (JsonException)
            {
                logger.LogDebug("Claude CLI의 non-JSON 라인 건너뜀");
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
                logger.LogWarning(ex, "stderr 수집 중 오류 발생");
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

            caps.SupportsOpus47 = !VersionComparer.IsOutdated(caps.Version, SeoroConstants.Claude47MinVersion);
            caps.SupportsXHighEffort = caps.SupportsOpus47;
            ModelDefinitions.ApplyCliVersion(caps.Version);

            logger.LogInformation("Claude CLI 감지됨: version={Version}, verbose={SupportsVerbose}, opus47={SupportsOpus47}",
                caps.Version, caps.SupportsVerbose, caps.SupportsOpus47);
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
                    // 우아한 종료: CTS 취소 후 프로세스가 자체 종료될 때까지 잠시 대기
                    if (!_process.WaitForExit(GracefulShutdownTimeout))
                    {
                        _logger.LogDebug("프로세스가 우아하게 종료되지 않음, kill 신호 전송 중");
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