using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Seoro.Shared.Services.Cli;
using Seoro.Shared.Services.Events;

namespace Seoro.Shared.Services.Chat;

/// <summary>
///     백그라운드 스트림에서 실행되는 권한 콜백과 Blazor UI 사이를 잇는 조정자.
///     콜백은 <see cref="RequestAsync"/>로 결정을 기다리고, UI는 <see cref="Resolve"/>로 답을 회신한다.
///     세션당 보류 요청은 1개로 직렬화된다.
/// </summary>
public interface IToolPermissionCoordinator
{
    /// <summary>권한 콜백이 호출. 요청을 UI에 알리고 사용자 응답까지 대기한다.</summary>
    Task<ToolPermissionDecision> RequestAsync(string sessionId, ToolPermissionRequest request, CancellationToken ct);

    /// <summary>UI가 호출. 보류 중인 요청을 결정으로 완료한다. 보류 요청이 없으면 false.</summary>
    bool Resolve(string sessionId, ToolPermissionDecision decision);

    /// <summary>세션 종료/스트림 정리 시 보류 요청을 거부로 정리한다.</summary>
    void CancelPending(string sessionId, string reason);

    /// <summary>세션 전환 후 UI 복원용. 보류 중인 요청을 조회한다.</summary>
    ToolPermissionRequest? PeekPending(string sessionId);
}

/// <inheritdoc cref="IToolPermissionCoordinator"/>
public sealed class ToolPermissionCoordinator(IEventBus eventBus) : IToolPermissionCoordinator
{
    private sealed class PendingEntry
    {
        public required ToolPermissionRequest Request { get; init; }
        public required TaskCompletionSource<ToolPermissionDecision> Tcs { get; init; }
        public CancellationTokenRegistration CtRegistration { get; set; }
    }

    private readonly ConcurrentDictionary<string, PendingEntry> _pending = new();

    public Task<ToolPermissionDecision> RequestAsync(
        string sessionId, ToolPermissionRequest request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ToolPermissionDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new PendingEntry { Request = request, Tcs = tcs };

        // 같은 세션에 보류 중이던 이전 요청은 거부로 정리하고 교체한다.
        if (_pending.TryRemove(sessionId, out var prev))
        {
            prev.CtRegistration.Dispose();
            prev.Tcs.TrySetResult(new DenyDecision("이전 권한 요청이 새 요청으로 대체됨", Interrupt: true));
        }

        _pending[sessionId] = entry;

        // 스트림이 취소되면(사용자 Stop 등) 보류 요청도 취소해 콜백의 await를 푼다.
        entry.CtRegistration = ct.Register(() =>
        {
            if (_pending.TryRemove(sessionId, out var e) && ReferenceEquals(e, entry))
                e.Tcs.TrySetCanceled(ct);
        });

        eventBus.Publish(new ToolPermissionRequestedEvent(sessionId, request));
        return tcs.Task;
    }

    public bool Resolve(string sessionId, ToolPermissionDecision decision)
    {
        if (_pending.TryRemove(sessionId, out var entry))
        {
            entry.CtRegistration.Dispose();
            return entry.Tcs.TrySetResult(decision);
        }

        return false;
    }

    public void CancelPending(string sessionId, string reason)
    {
        if (_pending.TryRemove(sessionId, out var entry))
        {
            entry.CtRegistration.Dispose();
            entry.Tcs.TrySetResult(new DenyDecision(reason, Interrupt: true));
        }
    }

    public ToolPermissionRequest? PeekPending(string sessionId)
        => _pending.TryGetValue(sessionId, out var entry) ? entry.Request : null;
}
