using Seoro.Shared.Services.Chat;
using Seoro.Shared.Services.Cli;
using Seoro.Shared.Services.Events;

namespace Seoro.Shared.Tests;

public class ToolPermissionCoordinatorTests
{
    private static ToolPermissionRequest AskRequest(string id = "s1") => new()
    {
        Kind = ToolPermissionKind.AskUserQuestion,
        ToolName = "AskUserQuestion",
        RawInputJson = "{\"questions\":[]}",
    };

    [Fact]
    public async Task Resolve_CompletesPendingRequest()
    {
        var coordinator = new ToolPermissionCoordinator(new EventBus());
        var task = coordinator.RequestAsync("s1", AskRequest(), CancellationToken.None);

        Assert.False(task.IsCompleted);

        var decision = new AllowDecision();
        var resolved = coordinator.Resolve("s1", decision);

        Assert.True(resolved);
        Assert.Same(decision, await task);
    }

    [Fact]
    public void RequestAsync_PublishesEvent()
    {
        var bus = new EventBus();
        ToolPermissionRequestedEvent? received = null;
        bus.Subscribe<ToolPermissionRequestedEvent>(e => received = e);

        var coordinator = new ToolPermissionCoordinator(bus);
        _ = coordinator.RequestAsync("s1", AskRequest(), CancellationToken.None);

        Assert.NotNull(received);
        Assert.Equal("s1", received!.SessionId);
        Assert.Equal(ToolPermissionKind.AskUserQuestion, received.Request.Kind);
    }

    [Fact]
    public async Task Cancellation_CancelsPendingTask()
    {
        var coordinator = new ToolPermissionCoordinator(new EventBus());
        using var cts = new CancellationTokenSource();
        var task = coordinator.RequestAsync("s1", AskRequest(), cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Null(coordinator.PeekPending("s1"));
    }

    [Fact]
    public async Task SecondRequest_RejectsPreviousWithDeny()
    {
        var coordinator = new ToolPermissionCoordinator(new EventBus());
        var first = coordinator.RequestAsync("s1", AskRequest(), CancellationToken.None);
        var second = coordinator.RequestAsync("s1", AskRequest(), CancellationToken.None);

        var firstResult = await first;
        var deny = Assert.IsType<DenyDecision>(firstResult);
        Assert.True(deny.Interrupt);
        Assert.False(second.IsCompleted);
    }

    [Fact]
    public async Task CancelPending_ResolvesWithDeny()
    {
        var coordinator = new ToolPermissionCoordinator(new EventBus());
        var task = coordinator.RequestAsync("s1", AskRequest(), CancellationToken.None);

        coordinator.CancelPending("s1", "세션 종료");

        var deny = Assert.IsType<DenyDecision>(await task);
        Assert.True(deny.Interrupt);
        Assert.Null(coordinator.PeekPending("s1"));
    }

    [Fact]
    public void PeekPending_ReturnsRequestUntilResolved()
    {
        var coordinator = new ToolPermissionCoordinator(new EventBus());
        Assert.Null(coordinator.PeekPending("s1"));

        _ = coordinator.RequestAsync("s1", AskRequest(), CancellationToken.None);
        Assert.NotNull(coordinator.PeekPending("s1"));

        coordinator.Resolve("s1", new AllowDecision());
        Assert.Null(coordinator.PeekPending("s1"));
    }

    [Fact]
    public void Resolve_NoPending_ReturnsFalse()
    {
        var coordinator = new ToolPermissionCoordinator(new EventBus());
        Assert.False(coordinator.Resolve("missing", new AllowDecision()));
    }
}
