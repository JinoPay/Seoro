using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Seoro.Shared.Models.Chat;
using Seoro.Shared.Services.Cli.Approval;

namespace Seoro.Shared.Services.Claude.Bidirectional;

/// <summary>
///     하나의 Claude CLI 영속 프로세스를 소유하고 양방향 stream-json 프로토콜로 통신한다.
///     - 단일 stdout 읽기 루프가 control_request를 가로채(승인/무시) 나머지 이벤트만 활성 턴 채널로 전달.
///     - stdin 쓰기는 <see cref="_stdinLock" />로 직렬화(user 메시지·control_response·interrupt가 한 스트림 공유).
///     - 한 세션은 한 번에 한 턴만 진행한다(<see cref="_turnLock" />).
///     control 와이어 포맷은 전적으로 <see cref="ControlProtocolCodec" />에 격리된다.
/// </summary>
public sealed class ClaudeSession : IAsyncDisposable
{
    private readonly IToolApprovalHandler _approvalHandler;
    private readonly ILogger _logger;
    private readonly Process _process;
    private readonly string _sessionId;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SemaphoreSlim _stdinLock = new(1, 1);
    private readonly StringBuilder _stderr = new();
    private readonly SemaphoreSlim _turnLock = new(1, 1);

    private volatile Channel<StreamEvent>? _activeTurn;
    private bool _initialized;
    private Task? _readLoopTask;
    private Task? _stderrTask;

    public ClaudeSession(Process process, string sessionId, IToolApprovalHandler approvalHandler, ILogger logger)
    {
        _process = process;
        _sessionId = sessionId;
        _approvalHandler = approvalHandler;
        _logger = logger;
    }

    /// <summary>읽기/오류 수집 루프를 시작한다. 생성 직후 1회 호출.</summary>
    public void Start()
    {
        _readLoopTask = Task.Run(ReadLoopAsync);
        _stderrTask = Task.Run(CollectStderrAsync);
    }

    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            catch { return true; }
        }
    }

    /// <summary>
    ///     user 메시지를 전송하고 이번 턴의 이벤트(result 포함)를 스트리밍한다.
    ///     result 이벤트 또는 프로세스 종료 시 종료된다.
    /// </summary>
    public async IAsyncEnumerable<StreamEvent> SendTurnAsync(
        string content, [EnumeratorCancellation] CancellationToken ct)
    {
        await _turnLock.WaitAsync(ct);
        var channel = Channel.CreateUnbounded<StreamEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        _activeTurn = channel;
        try
        {
            // 첫 턴 직전에 권한/제어 control protocol을 활성화하는 initialize를 1회 전송한다.
            if (!_initialized)
            {
                await WriteLineAsync(ControlProtocolCodec.BuildInitializeRequest("init_1"), ct);
                _initialized = true;
            }

            await WriteLineAsync(ControlProtocolCodec.BuildUserMessage(content), ct);
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                yield return evt;
        }
        finally
        {
            _activeTurn = null;
            _turnLock.Release();
        }
    }

    /// <summary>진행 중인 턴을 interrupt control_request로 중단시킨다(프로세스는 유지).</summary>
    public async Task InterruptAsync()
    {
        if (HasExited) return;
        try
        {
            var reqId = $"req_{Guid.NewGuid():N}";
            await WriteLineAsync(ControlProtocolCodec.BuildInterruptRequest(reqId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "interrupt control_request 전송 실패");
        }
    }

    private async Task WriteLineAsync(string json, CancellationToken ct)
    {
        await _stdinLock.WaitAsync(ct);
        try
        {
            await _process.StandardInput.WriteAsync(json.AsMemory(), ct);
            await _process.StandardInput.WriteAsync("\n".AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);
        }
        finally
        {
            _stdinLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var reader = _process.StandardOutput;
        try
        {
            while (!reader.EndOfStream && !_shutdownCts.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync(_shutdownCts.Token); }
                catch (OperationCanceledException) { break; }

                if (string.IsNullOrWhiteSpace(line)) continue;
                _logger.LogDebug("ClaudeSession 원본 라인: {Line}", line);

                JsonElement root;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    root = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    _logger.LogDebug("ClaudeSession non-JSON 라인 건너뜀");
                    continue;
                }

                if (ControlProtocolCodec.TryParseControlRequest(root, out var control) && control != null)
                {
                    await HandleControlRequestAsync(control);
                    continue;
                }

                // control_response(initialize 등)는 채팅 이벤트가 아니므로 무시한다.
                if (root.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "control_response")
                {
                    _logger.LogDebug("control_response 수신(무시)");
                    continue;
                }

                StreamEvent? evt;
                try { evt = root.Deserialize<StreamEvent>(); }
                catch (JsonException) { continue; }
                if (evt == null) continue;

                var turn = _activeTurn;
                if (turn != null)
                {
                    turn.Writer.TryWrite(evt);
                    if (evt.Type == "result")
                        turn.Writer.TryComplete();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ClaudeSession 읽기 루프 오류");
        }
        finally
        {
            // 프로세스 종료/스트림 끝 → 활성 턴을 깨워 호출자가 빠져나오게 한다.
            _activeTurn?.Writer.TryComplete();
        }
    }

    private async Task HandleControlRequestAsync(ClaudeControlRequest control)
    {
        if (control.Subtype is "permission" or "can_use_tool")
        {
            var approvalReq = ControlProtocolCodec.ToApprovalRequest(control, _sessionId);
            if (approvalReq == null)
            {
                _logger.LogDebug("permission control_request 파싱 실패, 무시");
                return;
            }

            ToolApprovalDecision decision;
            try
            {
                decision = await _approvalHandler.RequestApprovalAsync(approvalReq, _shutdownCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "승인 핸들러 오류, 거부로 응답");
                decision = new ToolApprovalDecision
                    { Outcome = ApprovalOutcome.Deny, DenyReason = "Approval handler error" };
            }

            try
            {
                await WriteLineAsync(
                    ControlProtocolCodec.BuildPermissionResponse(control.RequestId, decision),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "control_response 전송 실패");
            }
        }
        else
        {
            // initialize 등 그 외 control은 Phase 1에서 처리하지 않는다(로그만).
            _logger.LogDebug("미처리 control_request subtype: {Subtype}", control.Subtype);
        }
    }

    private async Task CollectStderrAsync()
    {
        try
        {
            var text = await _process.StandardError.ReadToEndAsync(_shutdownCts.Token);
            if (!string.IsNullOrWhiteSpace(text))
            {
                _stderr.Append(text);
                _logger.LogDebug("ClaudeSession stderr: {Stderr}", text);
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        catch (Exception ex) { _logger.LogDebug(ex, "ClaudeSession stderr 수집 오류"); }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdownCts.Cancel();
        try
        {
            if (!_process.HasExited)
            {
                try { _process.StandardInput.Close(); } catch { /* best-effort */ }
                if (!_process.WaitForExit(2000))
                    try { _process.Kill(true); } catch { /* best-effort */ }
            }
        }
        catch { /* process already gone */ }

        if (_readLoopTask != null)
            try { await _readLoopTask; } catch { /* ignore */ }
        if (_stderrTask != null)
            try { await _stderrTask; } catch { /* ignore */ }

        try { _process.Dispose(); } catch { /* best-effort */ }
        _shutdownCts.Dispose();
        _stdinLock.Dispose();
        _turnLock.Dispose();
    }
}
