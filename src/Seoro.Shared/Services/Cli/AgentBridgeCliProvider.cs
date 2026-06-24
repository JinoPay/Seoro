using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using AB = AgentBridge;
using AgentBridge.Claude;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seoro.Shared.Models.Chat;
using Seoro.Shared.Models.Settings;

namespace Seoro.Shared.Services.Cli;

/// <summary>
/// AgentBridge.NET의 <see cref="AB.IAgentProvider"/>를 Seoro의 <see cref="ICliProvider"/> 계약에 맞춰 감싸는 어댑터.
/// 프로바이더(claude/codex)별로 인스턴스 1개를 생성한다. 메시지 전송마다 <see cref="AB.IAgentProvider.QueryAsync"/>를
/// 호출하고(메시지당 모델 + ConversationId 재개), 결과 <see cref="AB.AgentMessage"/> 스트림을
/// <see cref="StreamEventTranslator"/>로 <see cref="StreamEvent"/>로 변환한다.
/// </summary>
public sealed class AgentBridgeCliProvider : ICliProvider
{
    private const string DefaultKey = "__default__";

    private readonly AB.IAgentProvider _agent;
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _ctsBySession = new();
    private readonly ProviderCapabilities _capabilities;
    private readonly bool _isCodex;

    public AgentBridgeCliProvider(AB.IAgentProvider agent, IOptionsMonitor<AppSettings> settings, ILogger logger)
    {
        _agent = agent;
        _settings = settings;
        _logger = logger;
        _isCodex = string.Equals(agent.Id, "codex", StringComparison.OrdinalIgnoreCase);
        _capabilities = BuildCapabilities(_isCodex);
    }

    public string ProviderId => _agent.Id;

    public string DisplayName => _agent.DisplayName;

    public ProviderCapabilities Capabilities => _capabilities;

    public async IAsyncEnumerable<StreamEvent> SendMessageAsync(
        CliSendOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var key = options.SessionId ?? DefaultKey;

        // 같은 세션에 진행 중인 스트림이 있으면 취소한다(기존 ClaudeService 동작 재현).
        if (_ctsBySession.TryRemove(key, out var prev))
        {
            prev.Cancel();
            prev.Dispose();
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ctsBySession[key] = cts;

        var settings = _settings.CurrentValue;
        var translator = new StreamEventTranslator(_agent.Id);

        AB.AgentPrompt prompt;
        AB.AgentOptions agentOptions;
        if (_isCodex)
        {
            prompt = new AB.AgentPrompt(CliOptionsMapper.ComposeCodexPrompt(options.Message, options.SystemPrompt));
            agentOptions = CliOptionsMapper.BuildCodexOptions(options, settings);
        }
        else
        {
            prompt = new AB.AgentPrompt(options.Message);
            agentOptions = CliOptionsMapper.BuildClaudeOptions(options, settings);
        }

        // 대화형 권한 핸들러가 있고 Claude면 세션 기반 양방향 경로로 구동한다.
        // 권한 콜백이 사용자 응답(도구 응답/플랜 승인)을 같은 턴 안에서 CLI로 회신할 수 있어야 하기 때문.
        var messages = !_isCodex && options.PermissionHandler is not null && agentOptions is ClaudeAgentOptions claudeOptions
            ? RunWithPermissionSession(prompt, claudeOptions, options.PermissionHandler, cts.Token)
            : _agent.QueryAsync(prompt, agentOptions, cts.Token);

        try
        {
            await foreach (var msg in SafeIterate(messages, cts.Token).ConfigureAwait(false))
            {
                foreach (var ev in translator.Translate(msg))
                    yield return ev;
            }
        }
        finally
        {
            if (_ctsBySession.TryGetValue(key, out var current) && ReferenceEquals(current, cts))
                _ctsBySession.TryRemove(key, out _);
            cts.Dispose();
        }
    }

    /// <summary>
    /// 권한 콜백을 위해 명시적 세션을 띄워 구동한다(QueryAsync와 동일한 1회성 시맨틱: ResultMessage에서 종료).
    /// 세션 핸들을 권한 어댑터에 넘겨 ExitPlanMode 승인 시 권한 모드 전환(ApprovePlanAsync)을 가능하게 한다.
    /// </summary>
    private async IAsyncEnumerable<AB.AgentMessage> RunWithPermissionSession(
        AB.AgentPrompt prompt,
        ClaudeAgentOptions claudeOptions,
        ToolPermissionHandler handler,
        [EnumeratorCancellation] CancellationToken token)
    {
        var box = new SessionBox();
        var withCallback = claudeOptions with { CanUseTool = BuildCanUseTool(handler, box) };

        await using var session = await _agent.CreateSessionAsync(withCallback, token).ConfigureAwait(false);
        box.Session = session;

        await session.SendAsync(prompt, token).ConfigureAwait(false);

        await foreach (var msg in session.ReceiveMessages(token).ConfigureAwait(false))
        {
            yield return msg;
            if (msg is AB.ResultMessage)
                yield break;
        }
    }

    /// <summary>세션 핸들의 늦은 바인딩용 박스(옵션 생성 시점엔 세션이 아직 없으므로).</summary>
    private sealed class SessionBox
    {
        public AB.IAgentSession? Session;
    }

    /// <summary>
    /// Seoro 중립 <see cref="ToolPermissionHandler"/>를 AgentBridge <see cref="CanUseTool"/> 콜백으로 변환한다.
    /// 대화형 도구(AskUserQuestion/ExitPlanMode)만 핸들러로 라우팅하고, 일반 도구는 즉시 허용(bypass 동치)한다.
    /// </summary>
    private CanUseTool BuildCanUseTool(ToolPermissionHandler handler, SessionBox box)
    {
        return async (toolName, input, ctx, ct) =>
        {
            var qset = UserQuestionSet.TryParse(toolName, input);
            var plan = ClaudeTools.GetPlan(toolName, input);

            ToolPermissionRequest request;
            if (qset is not null)
                request = new ToolPermissionRequest
                {
                    Kind = ToolPermissionKind.AskUserQuestion,
                    ToolName = toolName,
                    ToolUseId = ctx.ToolUseId,
                    RawInputJson = input.GetRawText(),
                };
            else if (plan is not null)
                request = new ToolPermissionRequest
                {
                    Kind = ToolPermissionKind.ExitPlanMode,
                    ToolName = toolName,
                    ToolUseId = ctx.ToolUseId,
                    PlanText = plan,
                };
            else
                return new PermissionAllow(); // 일반 도구: 기존 bypass 동작 유지(자동 허용).

            ToolPermissionDecision decision;
            try
            {
                decision = await handler(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new PermissionDeny("권한 요청이 취소됨") { Interrupt = true };
            }

            switch (decision)
            {
                case AllowAnswerDecision answer when qset is not null:
                    return qset.Answer(answer.Selections, answer.FreeResponse);

                case AllowDecision allow:
                    if (allow.NextPermissionMode is { } modeStr
                        && box.Session is { } session
                        && CliOptionsMapper.MapPermissionMode(modeStr) is { } nextMode)
                        return await session.ApprovePlanAsync(nextMode, ct).ConfigureAwait(false);
                    return new PermissionAllow();

                case DenyDecision deny:
                    return new PermissionDeny(deny.Message) { Interrupt = deny.Interrupt };

                default:
                    return new PermissionAllow();
            }
        };
    }

    /// <summary>
    /// 메시지 소스를 감싸 취소는 그대로 전파하고(orchestrator가 wasCancelled 처리), CLI 미설치/기타 예외는
    /// <see cref="AB.ErrorMessage"/>로 변환해 스트림에 흘려보낸다(예외 throw 대신 깔끔한 UX).
    /// </summary>
    private async IAsyncEnumerable<AB.AgentMessage> SafeIterate(
        IAsyncEnumerable<AB.AgentMessage> source,
        [EnumeratorCancellation] CancellationToken token)
    {
        await using var enumerator = source.GetAsyncEnumerator(token);

        while (true)
        {
            AB.AgentMessage? current = null;
            AB.ErrorMessage? error = null;
            var done = false;

            try
            {
                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    current = enumerator.Current;
                else
                    done = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AB.AgentNotInstalledException ex)
            {
                error = new AB.ErrorMessage($"{_agent.DisplayName} CLI를 찾을 수 없습니다: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentBridge {Provider} 쿼리 실패", _agent.Id);
                error = new AB.ErrorMessage(ex.Message);
            }

            if (error != null)
            {
                yield return error;
                yield break;
            }

            if (done)
                yield break;

            yield return current!;
        }
    }

    public async Task<(bool found, string resolvedPath)> DetectCliAsync()
    {
        var installation = await _agent.DetectInstallationAsync().ConfigureAwait(false);
        return (installation.Found, installation.ExecutablePath ?? string.Empty);
    }

    public async Task<string?> GetDetectedVersionAsync()
    {
        try
        {
            var version = await _agent.ProbeVersionAsync().ConfigureAwait(false);
            return version.Raw;
        }
        catch (AB.AgentNotInstalledException)
        {
            return null;
        }
    }

    public void Cancel(string? sessionId = null)
    {
        var key = sessionId ?? DefaultKey;
        if (_ctsBySession.TryRemove(key, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var kv in _ctsBySession)
        {
            try
            {
                kv.Value.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 이미 정리됨 — 무시.
            }

            kv.Value.Dispose();
        }

        _ctsBySession.Clear();
    }

    /// <summary>
    /// UI 게이팅 회귀를 막기 위해 기존 ClaudeService/CodexService의 ProviderCapabilities 값과 정확히 일치시킨다.
    /// Claude는 전부 지원, Codex는 도구 필터링/예산/폴백 모델 미지원.
    /// </summary>
    private static ProviderCapabilities BuildCapabilities(bool isCodex) => new()
    {
        SupportsEffortLevel = true,
        SupportsForkSession = true,
        SupportsPlanMode = true,
        SupportsToolFiltering = !isCodex,
        SupportsMaxBudget = !isCodex,
        SupportsFallbackModel = !isCodex,
        SupportsImageAttachment = true,
        SupportsWebSearch = true,
        SupportsMcp = true,
    };
}
