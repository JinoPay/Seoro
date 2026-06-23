using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using AB = AgentBridge;
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

        try
        {
            await foreach (var msg in SafeQuery(prompt, agentOptions, cts.Token).ConfigureAwait(false))
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
    /// QueryAsync를 감싸 취소는 그대로 전파하고(orchestrator가 wasCancelled 처리), CLI 미설치/기타 예외는
    /// <see cref="AB.ErrorMessage"/>로 변환해 스트림에 흘려보낸다(예외 throw 대신 깔끔한 UX).
    /// </summary>
    private async IAsyncEnumerable<AB.AgentMessage> SafeQuery(
        AB.AgentPrompt prompt,
        AB.AgentOptions options,
        [EnumeratorCancellation] CancellationToken token)
    {
        await using var enumerator = _agent.QueryAsync(prompt, options, token).GetAsyncEnumerator(token);

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
