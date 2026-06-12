using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Codex.AppServer;

/// <summary>
///     Codex app-server와의 저수준 JSON-RPC(NDJSON) 송수신 계층.
///     요청 id↔응답 매칭, 알림/서버요청 콜백 디스패치를 담당한다.
///     프로토콜 의미(thread/turn lifecycle)는 <see cref="CodexAppServerClient" />가 담당한다.
/// </summary>
internal sealed class CodexJsonRpcFraming(Process process, ILogger logger)
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _idCounter;
    private Task? _readLoop;

    /// <summary>서버 알림 수신 콜백 (method, params).</summary>
    public Action<string, JsonElement>? OnNotification { get; set; }

    /// <summary>서버→클라 요청 수신 콜백 (id, method, params). 핸들러가 응답까지 책임진다.</summary>
    public Func<long, string, JsonElement, Task>? OnServerRequest { get; set; }

    public void Start() => _readLoop = Task.Run(ReadLoopAsync);

    public async Task<JsonElement> SendRequestAsync(string method, object? @params, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _idCounter);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            await WriteAsync(new { id, method, @params }, ct);
            await using (ct.Register(() => tcs.TrySetCanceled(ct)))
                return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public Task SendNotificationAsync(string method, object? @params, CancellationToken ct)
        => WriteAsync(new { method, @params }, ct);

    public Task SendResponseAsync(long id, object result, CancellationToken ct)
        => WriteAsync(new { id, result }, ct);

    private async Task WriteAsync(object message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, WriteOptions);
        await _writeLock.WaitAsync(ct);
        try
        {
            await process.StandardInput.WriteAsync(json.AsMemory(), ct);
            await process.StandardInput.WriteAsync("\n".AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var reader = process.StandardOutput;
        try
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonElement root;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    root = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    logger.LogDebug("Codex app-server non-JSON 라인 건너뜀");
                    continue;
                }

                Dispatch(root);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Codex JSON-RPC 읽기 루프 종료");
        }
        finally
        {
            // 프로세스 종료 → 대기 중인 모든 요청 취소
            foreach (var kv in _pending)
                kv.Value.TrySetCanceled();
        }
    }

    private void Dispatch(JsonElement root)
    {
        var hasId = root.TryGetProperty("id", out var idEl);
        var hasMethod = root.TryGetProperty("method", out var methodEl);

        if (hasMethod)
        {
            var method = methodEl.GetString() ?? "";
            var prms = root.TryGetProperty("params", out var p) ? p : default;
            if (hasId)
            {
                // 서버 → 클라 요청
                var id = idEl.GetInt64();
                if (OnServerRequest != null)
                    _ = OnServerRequest(id, method, prms);
            }
            else
            {
                OnNotification?.Invoke(method, prms);
            }

            return;
        }

        if (hasId && _pending.TryRemove(idEl.GetInt64(), out var tcs))
        {
            if (root.TryGetProperty("result", out var result))
                tcs.TrySetResult(result.Clone());
            else if (root.TryGetProperty("error", out var error))
                tcs.TrySetException(new CodexRpcException(error.ToString()));
            else
                tcs.TrySetResult(default);
        }
    }
}

internal sealed class CodexRpcException(string message) : Exception(message);
