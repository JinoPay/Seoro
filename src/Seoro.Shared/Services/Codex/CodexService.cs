using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seoro.Shared.Services.Codex;

/// <summary>
///     Codex CLI (openai/codex)를 ICliProvider로 구현한다.
///     Codex의 JSONL 이벤트(ThreadEvent 형식)를 Claude 형식의 StreamEvent로 변환하여 반환한다.
/// </summary>
public class CodexService(
    IOptionsMonitor<AppSettings> appSettings,
    IShellService shellService,
    IProcessRunner processRunner,
    ILogger<CodexService> logger)
    : ICliProvider
{
    private const string DefaultAgentKey = "__default__";
    private static readonly TimeSpan GracefulShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly CodexCliResolver _cliResolver = new(shellService, processRunner, logger);
    private readonly ConcurrentDictionary<string, AgentProcess> _agents = new();
    private bool _disposed;
    private string? _detectedVersion;

    // Codex tool type → Claude tool name 매핑
    private static readonly Dictionary<string, string> ToolNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["command_execution"] = "Bash",
        ["web_search"] = "WebSearch",
        ["mcp_tool_call"] = "McpTool",
        ["collab_tool_call"] = "Agent",
        ["file_search"] = "FileSearch",
        ["mcp_elicitation"] = "McpElicitation"
    };

    public string ProviderId => "codex";
    public string DisplayName => "Codex";

    public ProviderCapabilities Capabilities { get; } = new()
    {
        SupportsEffortLevel = true,
        SupportsForkSession = true,
        SupportsPlanMode = true,
        SupportsToolFiltering = false,
        SupportsMaxBudget = false,
        SupportsFallbackModel = false,
        SupportsImageAttachment = true,
        SupportsWebSearch = true,
        SupportsMcp = true,
    };

    public async IAsyncEnumerable<StreamEvent> SendMessageAsync(
        CliSendOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agentKey = options.SessionId ?? DefaultAgentKey;
        logger.LogInformation("세션 {AgentKey}에 대해 Codex 프로세스 시작, 모델: {Model}", agentKey, options.Model);

        var settings = appSettings.CurrentValue;
        var (fileName, baseArgs) = await _cliResolver.ResolveAsync(settings.CodexPath);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (_agents.TryRemove(agentKey, out var previous)) previous.Cancel();

        // EffortLevel 매핑: Seoro 형식 → Codex 형식
        var reasoningEffort = MapEffortLevel(options.EffortLevel, settings.CodexReasoningEffort);

        // PermissionMode → 승인/샌드박스 매핑
        var dangerouslyBypass = options.PermissionMode is "bypassAll" or "dangerouslySkipPermissions";
        var approvalPolicy = options.PermissionMode == "plan" ? "on-request" : settings.CodexApprovalPolicy;
        var sandboxMode = options.PermissionMode == "plan" ? "read-only" : settings.CodexSandboxMode;

        // ConversationId가 있으면 항상 exec resume 사용 (세션 컨텍스트 유지)
        // ContinueMode 여부와 무관하게, thread_id가 존재하는 한 이전 대화를 이어간다.
        string arguments;
        if (!string.IsNullOrEmpty(options.ConversationId))
            arguments = CodexArgumentBuilder.BuildResume(new CodexResumeBuildOptions
            {
                BaseArgs = baseArgs,
                ThreadId = options.ConversationId,
                Model = options.Model,
                WorkingDir = options.WorkingDir,
                DangerouslyBypass = dangerouslyBypass,
                ApprovalPolicy = approvalPolicy,
                SandboxMode = sandboxMode,
                ReasoningEffort = reasoningEffort,
                WebSearch = settings.CodexWebSearch,
                Ephemeral = settings.CodexEphemeral,
                AdditionalDirs = options.AdditionalDirs,
            });
        else
            arguments = CodexArgumentBuilder.Build(new CodexBuildOptions
            {
                BaseArgs = baseArgs,
                Model = options.Model,
                WorkingDir = options.WorkingDir,
                DangerouslyBypass = dangerouslyBypass,
                ApprovalPolicy = approvalPolicy,
                SandboxMode = sandboxMode,
                ReasoningEffort = reasoningEffort,
                WebSearch = settings.CodexWebSearch,
                Ephemeral = settings.CodexEphemeral,
                AdditionalDirs = options.AdditionalDirs,
            });

        var envVars = settings.EnvironmentVariables.Count > 0 ? settings.EnvironmentVariables : null;
        var token = cts.Token;

        await foreach (var evt in ExecuteCodexProcessAsync(
                           fileName, arguments, options.WorkingDir,
                           options.Message, options.SystemPrompt,
                           agentKey, cts, envVars, token))
            yield return evt;
    }

    public async Task<(bool found, string resolvedPath)> DetectCliAsync()
    {
        var settings = appSettings.CurrentValue;
        var result = await _cliResolver.DetectAsync(settings.CodexPath);
        if (result == null)
            return (false, string.Empty);

        var (fileName, baseArgs) = result.Value;
        return (true, fileName);
    }

    public async Task<string?> GetDetectedVersionAsync()
    {
        if (_detectedVersion != null) return _detectedVersion;
        try
        {
            var settings = appSettings.CurrentValue;
            var (fileName, baseArgs) = await _cliResolver.ResolveAsync(settings.CodexPath);
            var versionOutput = await _cliResolver.RunSimpleCommandAsync(fileName, $"{baseArgs}--version");
            _detectedVersion = versionOutput?.Trim();
            return _detectedVersion;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Codex CLI 버전 감지 실패");
            return null;
        }
    }

    /// <summary>
    ///     Codex CLI 로그인 상태를 확인한다.
    /// </summary>
    public async Task<bool> CheckLoginStatusAsync()
    {
        try
        {
            var settings = appSettings.CurrentValue;
            var (fileName, baseArgs) = await _cliResolver.ResolveAsync(settings.CodexPath);
            var output = await _cliResolver.RunSimpleCommandAsync(fileName, $"{baseArgs}login status");
            // exit code 0이면 인증됨
            return output != null;
        }
        catch
        {
            return false;
        }
    }

    public void Cancel(string? sessionId = null)
    {
        var key = sessionId ?? DefaultAgentKey;
        logger.LogInformation("세션 {AgentKey}의 Codex 프로세스 취소 중", key);
        if (_agents.TryRemove(key, out var agent)) agent.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var key in _agents.Keys.ToList())
            if (_agents.TryRemove(key, out var agent))
            {
                agent.Cancel();
                agent.Dispose();
            }
    }

    /// <summary>
    ///     Seoro effort level → Codex model_reasoning_effort 매핑.
    /// </summary>
    private static string MapEffortLevel(string effortLevel, string fallback)
    {
        return effortLevel.ToLowerInvariant() switch
        {
            "auto" => fallback, // 글로벌 설정 사용
            "low" or "minimal" => "low",
            "medium" => "medium",
            "high" => "high",
            "max" or "xhigh" => "xhigh",
            _ => fallback
        };
    }

    // ──────────────────────────────────────────────
    //  프로세스 실행 및 이벤트 변환
    // ──────────────────────────────────────────────

    private async IAsyncEnumerable<StreamEvent> ExecuteCodexProcessAsync(
        string fileName, string arguments, string workingDir,
        string message, string? systemPrompt,
        string agentKey, CancellationTokenSource cts,
        Dictionary<string, string>? envVars,
        [EnumeratorCancellation] CancellationToken token)
    {
        logger.LogDebug("실행 중: {FileName} {Arguments}", fileName, arguments);
        var loginPath = await shellService.GetLoginShellPathAsync();
        var process = StartProcess(fileName, arguments, workingDir, envVars, loginPath);
        var agent = new AgentProcess(process, cts, logger);
        _agents[agentKey] = agent;

        // 메시지를 stdin으로 전송 (system prompt + user message 조합)
        var stdinContent = BuildStdinContent(message, systemPrompt);
        await process.StandardInput.WriteAsync(stdinContent);
        process.StandardInput.Close();

        var stderrBuilder = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            try
            {
                var err = await process.StandardError.ReadToEndAsync(token);
                if (!string.IsNullOrWhiteSpace(err)) stderrBuilder.Append(err);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogWarning(ex, "stderr 수집 오류"); }
        }, token);

        // Codex JSONL 이벤트를 StreamEvent로 변환하여 yield
        var converter = new CodexEventConverter(logger);
        await foreach (var evt in ReadAndConvertCodexEventsAsync(process.StandardOutput, converter, token))
            yield return evt;

        // 프로세스 종료 대기
        try
        {
            if (!token.IsCancellationRequested && process is { HasExited: false })
            {
                using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await process.WaitForExitAsync(exitCts.Token);
            }
        }
        catch { /* best-effort */ }

        try { await stderrTask; } catch { }

        // stderr 에러 처리
        var stderr = stderrBuilder.ToString().Trim();
        if (!string.IsNullOrEmpty(stderr) && process.ExitCode != 0)
        {
            logger.LogWarning("Codex 프로세스가 코드 {ExitCode}로 종료됨: {Stderr}", process.ExitCode, stderr);
            yield return new StreamEvent
            {
                Type = "error",
                Error = JsonSerializer.SerializeToElement(stderr)
            };
        }

        try { process.Dispose(); } catch { }
    }

    private static string BuildStdinContent(string message, string? systemPrompt)
    {
        if (string.IsNullOrEmpty(systemPrompt))
            return message;

        // Codex는 system prompt를 별도 플래그 없이 메시지에 포함
        return $"[System Instructions]\n{systemPrompt}\n\n[User]\n{message}";
    }

    private async IAsyncEnumerable<StreamEvent> ReadAndConvertCodexEventsAsync(
        StreamReader reader,
        CodexEventConverter converter,
        [EnumeratorCancellation] CancellationToken token)
    {
        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(token);
            }
            catch (OperationCanceledException) { break; }

            if (string.IsNullOrWhiteSpace(line)) continue;

            logger.LogDebug("Codex 원본 라인: {Line}", line);

            JsonElement root;
            try
            {
                root = JsonSerializer.Deserialize<JsonElement>(line);
            }
            catch (JsonException)
            {
                logger.LogDebug("non-JSON 라인 건너뜀");
                continue;
            }

            foreach (var converted in converter.Convert(root))
                yield return converted;
        }
    }

    private static Process StartProcess(string fileName, string arguments, string workingDir,
        Dictionary<string, string>? envVars = null, string? loginShellPath = null)
    {
        if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
            && arguments.StartsWith("/c ", StringComparison.OrdinalIgnoreCase))
        {
            // chcp 65001: cmd.exe 세션의 코드 페이지를 UTF-8로 강제 설정.
            // Codex CLI가 하위 프로세스(PowerShell, rg 등)를 생성할 때
            // 콘솔 코드 페이지를 상속하므로 한글 등 멀티바이트 문자가 올바르게 전달된다.
            // > nul 2>&1 로 chcp 출력은 억제.
            arguments = $"/c \"chcp 65001 > nul 2>&1 & {arguments[3..].TrimEnd()}\"";
        }

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
        // Codex CLI 하위 프로세스의 UTF-8 출력을 돕는 추가 환경 변수
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        if (loginShellPath != null) psi.Environment["PATH"] = loginShellPath;
        if (envVars != null)
            foreach (var (key, value) in envVars)
                psi.Environment[key] = value;

        var process = new Process { StartInfo = psi };
        process.Start();
        return process;
    }

    private sealed class AgentProcess(Process process, CancellationTokenSource cts, ILogger logger) : IDisposable
    {
        public void Dispose()
        {
            cts.Dispose();
            try { process.Dispose(); } catch { }
        }

        public void Cancel()
        {
            cts.Cancel();
            try
            {
                if (process is { HasExited: false })
                    if (!process.WaitForExit(GracefulShutdownTimeout))
                        try { process.Kill(true); } catch { }
            }
            catch { }
        }
    }
}

/// <summary>
///     Codex CLI의 ThreadEvent JSONL을 Claude StreamEvent로 변환한다.
///     Codex item.* 라이프사이클 → content_block_* 라이프사이클 매핑.
/// </summary>
internal class CodexEventConverter(ILogger logger)
{
    // item ID별 누적 텍스트 추적 (text delta 계산용)
    private readonly Dictionary<string, string> _textAccum = new();
    // item ID → content_block index 매핑
    private readonly Dictionary<string, int> _itemIndex = new();
    private int _blockCounter;
    private bool _initEmitted;

    public IEnumerable<StreamEvent> Convert(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeEl)) yield break;
        var type = typeEl.GetString() ?? "";

        switch (type)
        {
            case "thread.started":
                // system init 이벤트 합성
                if (!_initEmitted)
                {
                    _initEmitted = true;
                    var threadId = root.TryGetProperty("thread_id", out var tid) ? tid.GetString() : null;
                    yield return new StreamEvent
                    {
                        Type = "system",
                        Subtype = "init",
                        SessionId = threadId
                    };
                }
                break;

            case "turn.started":
                // message_start 합성
                yield return new StreamEvent
                {
                    Type = "message_start",
                    Message = new StreamMessage
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Role = "assistant",
                        Model = "codex"
                    }
                };
                break;

            case "item.started":
            case "item.updated":
            case "item.completed":
                foreach (var evt in ConvertItemEvent(type, root))
                    yield return evt;
                break;

            case "turn.completed":
                // result 이벤트 합성
                var usage = ExtractUsage(root);
                yield return new StreamEvent
                {
                    Type = "result",
                    Usage = usage
                };
                break;

            case "turn.failed":
            {
                var errMsg = root.TryGetProperty("error", out var errEl)
                    ? (errEl.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : errEl.GetRawText())
                    : "Unknown Codex error";
                yield return new StreamEvent
                {
                    Type = "error",
                    Error = JsonSerializer.SerializeToElement(errMsg)
                };
                break;
            }

            case "error":
            {
                var errMsg = root.TryGetProperty("message", out var msgEl2) ? msgEl2.GetString() : root.GetRawText();
                yield return new StreamEvent
                {
                    Type = "error",
                    Error = JsonSerializer.SerializeToElement(errMsg)
                };
                break;
            }
        }
    }

    private IEnumerable<StreamEvent> ConvertItemEvent(string eventType, JsonElement root)
    {
        if (!root.TryGetProperty("item", out var item)) yield break;
        if (!item.TryGetProperty("id", out var idEl)) yield break;
        var itemId = idEl.GetString() ?? Guid.NewGuid().ToString("N");

        if (!item.TryGetProperty("type", out var itemTypeEl)) yield break;
        var itemType = itemTypeEl.GetString() ?? "";

        switch (itemType)
        {
            case "agent_message":
                foreach (var evt in ConvertAgentMessage(eventType, itemId, item))
                    yield return evt;
                break;

            case "reasoning":
                foreach (var evt in ConvertReasoning(eventType, itemId, item))
                    yield return evt;
                break;

            case "command_execution":
                foreach (var evt in ConvertCommandExecution(eventType, itemId, item))
                    yield return evt;
                break;

            case "file_change":
                foreach (var evt in ConvertFileChange(eventType, itemId, item))
                    yield return evt;
                break;

            case "mcp_tool_call":
            case "collab_tool_call":
            case "web_search":
            case "file_search":
            case "mcp_elicitation":
                foreach (var evt in ConvertGenericTool(eventType, itemId, itemType, item))
                    yield return evt;
                break;

            case "error":
            {
                var errText = item.TryGetProperty("message", out var em) ? em.GetString() : "Codex error";
                yield return new StreamEvent
                {
                    Type = "error",
                    Error = JsonSerializer.SerializeToElement(errText)
                };
                break;
            }
        }
    }

    private IEnumerable<StreamEvent> ConvertAgentMessage(string eventType, string itemId, JsonElement item)
    {
        var text = item.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";

        if (eventType == "item.started")
        {
            var index = GetOrCreateIndex(itemId);
            _textAccum[itemId] = "";
            yield return new StreamEvent
            {
                Type = "content_block_start",
                Index = index,
                ContentBlock = new ContentBlock { Type = "text", Text = "" }
            };
        }
        else if (eventType == "item.updated")
        {
            var index = GetOrCreateIndex(itemId);
            var prev = _textAccum.GetValueOrDefault(itemId, "");
            if (text.Length > prev.Length)
            {
                var delta = text[prev.Length..];
                _textAccum[itemId] = text;
                yield return new StreamEvent
                {
                    Type = "content_block_delta",
                    Index = index,
                    Delta = new ContentDelta { Type = "text_delta", Text = delta }
                };
            }
        }
        else if (eventType == "item.completed")
        {
            // item.started 없이 item.completed만 온 경우 content_block_start 보충
            var needsStart = !_itemIndex.ContainsKey(itemId);
            var index = GetOrCreateIndex(itemId);
            if (needsStart)
            {
                _textAccum[itemId] = "";
                yield return new StreamEvent
                {
                    Type = "content_block_start",
                    Index = index,
                    ContentBlock = new ContentBlock { Type = "text", Text = "" }
                };
            }

            var prev = _textAccum.GetValueOrDefault(itemId, "");
            if (text.Length > prev.Length)
            {
                var delta = text[prev.Length..];
                yield return new StreamEvent
                {
                    Type = "content_block_delta",
                    Index = index,
                    Delta = new ContentDelta { Type = "text_delta", Text = delta }
                };
            }
            yield return new StreamEvent { Type = "content_block_stop", Index = index };
            _textAccum.Remove(itemId);
        }
    }

    private IEnumerable<StreamEvent> ConvertReasoning(string eventType, string itemId, JsonElement item)
    {
        var text = item.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";

        if (eventType == "item.started")
        {
            var index = GetOrCreateIndex(itemId);
            _textAccum[itemId] = "";
            yield return new StreamEvent
            {
                Type = "content_block_start",
                Index = index,
                ContentBlock = new ContentBlock { Type = "thinking", Thinking = "" }
            };
        }
        else if (eventType == "item.updated")
        {
            var index = GetOrCreateIndex(itemId);
            var prev = _textAccum.GetValueOrDefault(itemId, "");
            if (text.Length > prev.Length)
            {
                var delta = text[prev.Length..];
                _textAccum[itemId] = text;
                yield return new StreamEvent
                {
                    Type = "content_block_delta",
                    Index = index,
                    Delta = new ContentDelta { Type = "thinking_delta", Thinking = delta }
                };
            }
        }
        else if (eventType == "item.completed")
        {
            // item.started 없이 item.completed만 온 경우 content_block_start 보충
            var needsStart = !_itemIndex.ContainsKey(itemId);
            var index = GetOrCreateIndex(itemId);
            if (needsStart)
            {
                _textAccum[itemId] = "";
                yield return new StreamEvent
                {
                    Type = "content_block_start",
                    Index = index,
                    ContentBlock = new ContentBlock { Type = "thinking", Thinking = "" }
                };
            }

            var prev = _textAccum.GetValueOrDefault(itemId, "");
            if (text.Length > prev.Length)
            {
                var delta = text[prev.Length..];
                yield return new StreamEvent
                {
                    Type = "content_block_delta",
                    Index = index,
                    Delta = new ContentDelta { Type = "thinking_delta", Thinking = delta }
                };
            }
            yield return new StreamEvent { Type = "content_block_stop", Index = index };
            _textAccum.Remove(itemId);
        }
    }

    private IEnumerable<StreamEvent> ConvertCommandExecution(string eventType, string itemId, JsonElement item)
    {
        var command = item.TryGetProperty("command", out var cmdEl) ? cmdEl.GetString() ?? "" : "";
        var output = item.TryGetProperty("aggregated_output", out var outEl) ? outEl.GetString() : null;

        if (eventType == "item.started")
        {
            var index = GetOrCreateIndex(itemId);
            _textAccum[itemId] = ""; // 출력 누적 초기화
            var inputJson = JsonSerializer.SerializeToElement(new { command });
            yield return new StreamEvent
            {
                Type = "content_block_start",
                Index = index,
                ContentBlock = new ContentBlock
                {
                    Type = "tool_use",
                    Id = itemId,
                    Name = "Bash",
                    Input = inputJson
                }
            };
        }
        else if (eventType == "item.updated")
        {
            // 스트리밍 명령 출력: aggregated_output 변경분을 delta로 전송
            if (output != null)
            {
                var index = GetOrCreateIndex(itemId);
                var prev = _textAccum.GetValueOrDefault(itemId, "");
                if (output.Length > prev.Length)
                {
                    var delta = output[prev.Length..];
                    _textAccum[itemId] = output;
                    yield return new StreamEvent
                    {
                        Type = "content_block_delta",
                        Index = index,
                        Delta = new ContentDelta { Type = "text_delta", Text = delta }
                    };
                }
            }
        }
        else if (eventType == "item.completed")
        {
            // item.started 없이 item.completed만 온 경우 content_block_start 보충
            var needsStart = !_itemIndex.ContainsKey(itemId);
            var index = GetOrCreateIndex(itemId);
            if (needsStart)
            {
                _textAccum[itemId] = "";
                var inputJson = JsonSerializer.SerializeToElement(new { command });
                yield return new StreamEvent
                {
                    Type = "content_block_start",
                    Index = index,
                    ContentBlock = new ContentBlock
                    {
                        Type = "tool_use",
                        Id = itemId,
                        Name = "Bash",
                        Input = inputJson
                    }
                };
            }

            _textAccum.Remove(itemId);
            yield return new StreamEvent { Type = "content_block_stop", Index = index };

            // tool_result 블록
            var resultIndex = ++_blockCounter;
            _itemIndex[$"{itemId}_result"] = resultIndex;
            var isError = item.TryGetProperty("exit_code", out var ec) && ec.GetInt32() != 0;
            yield return new StreamEvent
            {
                Type = "content_block_start",
                Index = resultIndex,
                ContentBlock = new ContentBlock
                {
                    Type = "tool_result",
                    ToolUseId = itemId,
                    Text = output ?? "",
                    IsError = isError
                }
            };
            yield return new StreamEvent { Type = "content_block_stop", Index = resultIndex };
        }
    }

    private IEnumerable<StreamEvent> ConvertFileChange(string eventType, string itemId, JsonElement item)
    {
        if (eventType == "item.started")
        {
            var index = GetOrCreateIndex(itemId);
            // 파일 변경 내용에서 첫 번째 파일 경로 추출 시도
            var changes = item.TryGetProperty("changes", out var chEl) ? chEl : default;
            var filePath = "";
            if (changes.ValueKind == JsonValueKind.Array && changes.GetArrayLength() > 0)
            {
                var first = changes[0];
                if (first.TryGetProperty("path", out var pathEl)) filePath = pathEl.GetString() ?? "";
            }

            var inputJson = JsonSerializer.SerializeToElement(new { path = filePath });
            yield return new StreamEvent
            {
                Type = "content_block_start",
                Index = index,
                ContentBlock = new ContentBlock
                {
                    Type = "tool_use",
                    Id = itemId,
                    Name = "Edit",
                    Input = inputJson
                }
            };
        }
        else if (eventType == "item.completed")
        {
            // item.started 없이 item.completed만 온 경우 content_block_start 보충
            var needsStart = !_itemIndex.ContainsKey(itemId);
            var index = GetOrCreateIndex(itemId);
            if (needsStart)
            {
                var changes = item.TryGetProperty("changes", out var chEl2) ? chEl2 : default;
                var fp = "";
                if (changes.ValueKind == JsonValueKind.Array && changes.GetArrayLength() > 0)
                {
                    var first = changes[0];
                    if (first.TryGetProperty("path", out var pathEl)) fp = pathEl.GetString() ?? "";
                }
                var inputJson = JsonSerializer.SerializeToElement(new { path = fp });
                yield return new StreamEvent
                {
                    Type = "content_block_start",
                    Index = index,
                    ContentBlock = new ContentBlock
                    {
                        Type = "tool_use",
                        Id = itemId,
                        Name = "Edit",
                        Input = inputJson
                    }
                };
            }
            yield return new StreamEvent { Type = "content_block_stop", Index = index };
        }
    }

    private IEnumerable<StreamEvent> ConvertGenericTool(string eventType, string itemId, string itemType, JsonElement item)
    {
        var toolName = itemType switch
        {
            "web_search" => "WebSearch",
            "mcp_tool_call" => item.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "McpTool" : "McpTool",
            "collab_tool_call" => "Agent",
            "file_search" => "FileSearch",
            "mcp_elicitation" => "McpElicitation",
            _ => itemType
        };

        if (eventType == "item.started")
        {
            var index = GetOrCreateIndex(itemId);
            yield return new StreamEvent
            {
                Type = "content_block_start",
                Index = index,
                ContentBlock = new ContentBlock
                {
                    Type = "tool_use",
                    Id = itemId,
                    Name = toolName
                }
            };
        }
        else if (eventType == "item.completed")
        {
            // item.started 없이 item.completed만 온 경우 content_block_start 보충
            var needsStart = !_itemIndex.ContainsKey(itemId);
            var index = GetOrCreateIndex(itemId);
            if (needsStart)
            {
                yield return new StreamEvent
                {
                    Type = "content_block_start",
                    Index = index,
                    ContentBlock = new ContentBlock
                    {
                        Type = "tool_use",
                        Id = itemId,
                        Name = toolName
                    }
                };
            }
            yield return new StreamEvent { Type = "content_block_stop", Index = index };
        }
    }

    private int GetOrCreateIndex(string itemId)
    {
        if (!_itemIndex.TryGetValue(itemId, out var index))
        {
            index = _blockCounter++;
            _itemIndex[itemId] = index;
        }
        return index;
    }

    private static UsageInfo? ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usageEl)) return null;
        return new UsageInfo
        {
            InputTokens = usageEl.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
            OutputTokens = usageEl.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0,
            // Codex의 cached_input_tokens → CacheReadInputTokens로 매핑
            CacheReadInputTokens = usageEl.TryGetProperty("cached_input_tokens", out var ct) && ct.GetInt32() > 0
                ? ct.GetInt32()
                : null
        };
    }
}
