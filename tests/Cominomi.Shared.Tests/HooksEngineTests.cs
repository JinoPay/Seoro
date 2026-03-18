using Cominomi.Shared.Models;
using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cominomi.Shared.Tests;

public class HooksEngineTests
{
    private readonly StubProcessRunner _processRunner = new();
    private readonly StubShellService _shellService = new();

    private HooksEngine CreateEngine() => new(
        NullLogger<HooksEngine>.Instance,
        _shellService,
        _processRunner);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(500, 300)]
    public async Task ExecuteHook_ClampsTimeout(int inputTimeout, int expectedSeconds)
    {
        var engine = CreateEngine();
        var hooks = engine.GetType()
            .GetField("_hooks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        hooks.SetValue(engine, new List<HookDefinition>
        {
            new() { Event = HookEvent.OnMessageComplete, Command = "echo test", TimeoutSeconds = inputTimeout }
        });

        await engine.FireAsync(HookEvent.OnMessageComplete);

        Assert.Single(_processRunner.Invocations);
        var actual = _processRunner.Invocations[0].Timeout!.Value;
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), actual);
    }

    [Fact]
    public async Task FireAsync_ReturnsResults()
    {
        _processRunner.NextResult = new ProcessResult(true, "hello", "", 0);

        var engine = CreateEngine();
        var hooks = engine.GetType()
            .GetField("_hooks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        hooks.SetValue(engine, new List<HookDefinition>
        {
            new() { Event = HookEvent.OnSessionCreate, Command = "echo hello" }
        });

        var results = await engine.FireAsync(HookEvent.OnSessionCreate);

        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal("hello", results[0].Stdout);
        Assert.Equal("echo hello", results[0].Command);
    }

    [Fact]
    public async Task FireAsync_NoMatchingHooks_ReturnsEmpty()
    {
        var engine = CreateEngine();
        var results = await engine.FireAsync(HookEvent.OnBranchPush);
        Assert.Empty(results);
    }

    [Fact]
    public async Task FireAsync_DisabledHook_IsSkipped()
    {
        var engine = CreateEngine();
        var hooks = engine.GetType()
            .GetField("_hooks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        hooks.SetValue(engine, new List<HookDefinition>
        {
            new() { Event = HookEvent.OnMessageComplete, Command = "echo test", Enabled = false }
        });

        var results = await engine.FireAsync(HookEvent.OnMessageComplete);
        Assert.Empty(results);
        Assert.Empty(_processRunner.Invocations);
    }

    [Fact]
    public async Task FireAsync_TimedOutProcess_SetsTimedOutFlag()
    {
        _processRunner.NextResult = new ProcessResult(false, "", "Process timed out after 5s", -1);

        var engine = CreateEngine();
        var hooks = engine.GetType()
            .GetField("_hooks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        hooks.SetValue(engine, new List<HookDefinition>
        {
            new() { Event = HookEvent.Stop, Command = "sleep 999" }
        });

        var results = await engine.FireAsync(HookEvent.Stop);

        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.True(results[0].TimedOut);
    }

    [Fact]
    public async Task FireAsync_ProcessException_ReturnsFailed()
    {
        _processRunner.ThrowOnRun = new InvalidOperationException("process not found");

        var engine = CreateEngine();
        var hooks = engine.GetType()
            .GetField("_hooks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        hooks.SetValue(engine, new List<HookDefinition>
        {
            new() { Event = HookEvent.OnPrCreate, Command = "bad-command" }
        });

        var results = await engine.FireAsync(HookEvent.OnPrCreate);

        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Equal(-1, results[0].ExitCode);
        Assert.Contains("process not found", results[0].Stderr);
    }

    private class StubProcessRunner : IProcessRunner
    {
        public List<ProcessRunOptions> Invocations { get; } = [];
        public ProcessResult NextResult { get; set; } = new(true, "", "", 0);
        public Exception? ThrowOnRun { get; set; }

        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default)
        {
            Invocations.Add(options);
            if (ThrowOnRun != null) throw ThrowOnRun;
            return Task.FromResult(NextResult);
        }
    }

    private class StubShellService : IShellService
    {
        public Task<ShellInfo> GetShellAsync() =>
            Task.FromResult(new ShellInfo("/bin/bash", "-c", ShellType.Bash));
        public Task<string?> WhichAsync(string executableName) =>
            Task.FromResult<string?>(null);
    }
}
