using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Seoro.Shared.Models.Chat;
using Seoro.Shared.Services.Cli.Approval;

namespace Seoro.Shared.Services.Codex.AppServer;

/// <summary>턴 전송에 필요한 옵션(CliSendOptions에서 매핑).</summary>
internal sealed record CodexTurnRequest
{
    public required string Message { get; init; }
    public required string WorkingDir { get; init; }
    public string? Model { get; init; }
    public string? ConversationId { get; init; }
}

/// <summary>
///     하나의 Codex app-server 영속 프로세스를 소유하고 JSON-RPC로 통신한다.
///     initialize 핸드셰이크 → thread 확보 → turn/start → 알림을 <see cref="StreamEvent" />로 스트리밍.
///     승인 요청(서버→클라)은 <see cref="IToolApprovalHandler" />로 라우팅, interrupt는 turn/interrupt.
/// </summary>
internal sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly IToolApprovalHandler _approvalHandler;
    private readonly CodexJsonRpcFraming _framing;
    private readonly ILogger _logger;
    private readonly Process _process;
    private readonly string _sessionId;
    private readonly SemaphoreSlim _turnLock = new(1, 1);

    private volatile Channel<StreamEvent>? _activeTurn;
    private CodexAppServerEventAdapter? _adapter;
    private string? _currentTurnId;
    private bool _initialized;
    private string? _threadId;

    public CodexAppServerClient(Process process, string sessionId, IToolApprovalHandler approvalHandler, ILogger logger)
    {
        _process = process;
        _sessionId = sessionId;
        _approvalHandler = approvalHandler;
        _logger = logger;
        _framing = new CodexJsonRpcFraming(process, logger)
        {
            OnNotification = OnNotification,
            OnServerRequest = OnServerRequestAsync
        };
    }

    public void Start() => _framing.Start();

    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            catch { return true; }
        }
    }

    public async IAsyncEnumerable<StreamEvent> StartTurnAsync(
        CodexTurnRequest req, [EnumeratorCancellation] CancellationToken ct)
    {
        await _turnLock.WaitAsync(ct);
        var channel = Channel.CreateUnbounded<StreamEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        _adapter = new CodexAppServerEventAdapter(_logger);
        _activeTurn = channel;
        try
        {
            await EnsureInitializedAsync(ct);
            await EnsureThreadAsync(req, ct);

            var turnParams = new
            {
                threadId = _threadId,
                input = new[] { new { type = "text", text = req.Message } },
                model = string.IsNullOrEmpty(req.Model) ? null : req.Model,
                cwd = req.WorkingDir
            };
            var result = await _framing.SendRequestAsync(CodexRpcMethods.TurnStart, turnParams, ct);
            _currentTurnId = result.TryGetProperty("turn", out var t) && t.TryGetProperty("id", out var tid)
                ? tid.GetString()
                : null;

            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                yield return evt;
        }
        finally
        {
            _activeTurn = null;
            _currentTurnId = null;
            _turnLock.Release();
        }
    }

    public async Task InterruptAsync()
    {
        if (HasExited || _threadId == null || _currentTurnId == null) return;
        try
        {
            await _framing.SendRequestAsync(CodexRpcMethods.TurnInterrupt,
                new { threadId = _threadId, turnId = _currentTurnId }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "turn/interrupt 전송 실패");
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _framing.SendRequestAsync(CodexRpcMethods.Initialize,
            new { clientInfo = new { name = "Seoro", version = "1.0" } }, ct);
        await _framing.SendNotificationAsync(CodexRpcMethods.Initialized, null, ct);
        _initialized = true;
    }

    private async Task EnsureThreadAsync(CodexTurnRequest req, CancellationToken ct)
    {
        if (_threadId != null) return;

        JsonElement result;
        if (!string.IsNullOrEmpty(req.ConversationId))
            result = await _framing.SendRequestAsync(CodexRpcMethods.ThreadResume,
                new { threadId = req.ConversationId, cwd = req.WorkingDir }, ct);
        else
            result = await _framing.SendRequestAsync(CodexRpcMethods.ThreadStart,
                new { cwd = req.WorkingDir }, ct);

        _threadId = result.TryGetProperty("thread", out var th) && th.TryGetProperty("id", out var ti)
            ? ti.GetString()
            : null;
    }

    private void OnNotification(string method, JsonElement prms)
    {
        var turn = _activeTurn;
        var adapter = _adapter;
        if (turn == null || adapter == null) return;

        foreach (var evt in adapter.Adapt(method, prms))
        {
            turn.Writer.TryWrite(evt);
            if (evt.Type is "result" or "error")
                turn.Writer.TryComplete();
        }
    }

    private async Task OnServerRequestAsync(long id, string method, JsonElement prms)
    {
        try
        {
            object result = method switch
            {
                CodexRpcMethods.CommandExecutionRequestApproval or CodexRpcMethods.FileChangeRequestApproval
                    => new { decision = await ResolveApprovalDecisionAsync(method, prms) },
                CodexRpcMethods.PermissionsRequestApproval
                    => prms.TryGetProperty("permissions", out var perms)
                        ? new { permissions = JsonSerializer.Deserialize<JsonElement>(perms.GetRawText()), scope = "turn" }
                        : new { scope = "turn" } as object,
                CodexRpcMethods.ToolRequestUserInput => new { answers = new { } },
                _ => new { }
            };
            await _framing.SendResponseAsync(id, result, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Codex 서버 요청 처리 실패: {Method}", method);
            try { await _framing.SendResponseAsync(id, new { decision = "decline" }, CancellationToken.None); }
            catch { /* best-effort */ }
        }
    }

    private async Task<string> ResolveApprovalDecisionAsync(string method, JsonElement prms)
    {
        var kind = method == CodexRpcMethods.CommandExecutionRequestApproval
            ? ToolApprovalKind.Command
            : ToolApprovalKind.FileChange;
        var request = new ToolApprovalRequest
        {
            SessionId = _sessionId,
            Kind = kind,
            ToolName = kind == ToolApprovalKind.Command ? "Bash" : "Edit",
            Command = prms.TryGetProperty("command", out var c) ? c.GetString() : null,
            Reason = prms.TryGetProperty("reason", out var r) ? r.GetString() : null,
            RawInput = prms.Clone()
        };
        var decision = await _approvalHandler.RequestApprovalAsync(request, CancellationToken.None);
        return decision.Outcome switch
        {
            ApprovalOutcome.Allow => "accept",
            ApprovalOutcome.AllowForSession => "acceptForSession",
            ApprovalOutcome.Cancel => "cancel",
            _ => "decline"
        };
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                try { _process.StandardInput.Close(); } catch { /* best-effort */ }
                if (!_process.WaitForExit(2000))
                    try { _process.Kill(true); } catch { /* best-effort */ }
            }
        }
        catch { /* already gone */ }

        try { _process.Dispose(); } catch { /* best-effort */ }
        _turnLock.Dispose();
        await Task.CompletedTask;
    }
}
