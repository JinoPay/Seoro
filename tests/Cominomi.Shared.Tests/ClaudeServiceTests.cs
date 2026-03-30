using Cominomi.Shared.Models;
using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Tests;

public class ClaudeServiceTests : IDisposable
{
    private readonly StubShellService _shellService = new();
    private readonly StubProcessRunner _processRunner = new();
    private readonly FakeOptionsMonitor _optionsMonitor = new();
    private readonly ClaudeService _sut;

    public ClaudeServiceTests()
    {
        _sut = new ClaudeService(
            _optionsMonitor,
            _shellService,
            _processRunner,
            NullLogger<ClaudeService>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    // --- Cancel ---

    [Fact]
    public void Cancel_NoActiveProcess_DoesNotThrow()
    {
        _sut.Cancel("nonexistent-session");
    }

    [Fact]
    public void Cancel_DefaultSession_DoesNotThrow()
    {
        _sut.Cancel(); // null sessionId → uses default key
    }

    // --- DetectCliAsync ---

    [Fact]
    public async Task DetectCliAsync_ShellResolvesPath_ReturnsFound()
    {
        _shellService.WhichResult = "/usr/local/bin/claude";
        var (found, path) = await _sut.DetectCliAsync();
        Assert.True(found);
        Assert.Equal("/usr/local/bin/claude", path);
    }

    [Fact]
    public async Task DetectCliAsync_ConfiguredPath_UsesIt()
    {
        _optionsMonitor.Settings.ClaudePath = "/custom/claude";
        var (found, path) = await _sut.DetectCliAsync();
        Assert.True(found);
        Assert.Equal("/custom/claude", path);
    }

    // --- Dispose ---

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        _sut.Dispose();
        _sut.Dispose(); // second dispose should be safe
    }

    // --- Stubs ---

    private class StubShellService : IShellService
    {
        public string? WhichResult { get; set; }

        public Task<ShellInfo> GetShellAsync()
            => Task.FromResult(new ShellInfo("/bin/bash", "-c", ShellType.Bash));
        public Task<ShellInfo> GetTerminalShellAsync()
            => GetShellAsync();
        public Task<List<ShellInfo>> GetAvailableShellsAsync()
            => Task.FromResult(new List<ShellInfo> { new("/bin/bash", "-c", ShellType.Bash) });

        public Task<string?> WhichAsync(string executableName)
            => Task.FromResult(WhichResult);

        public Task<string?> GetLoginShellPathAsync()
            => Task.FromResult<string?>(null);

        public void InvalidateCache() { }
    }

    private class StubProcessRunner : IProcessRunner
    {
        public ProcessResult NextResult { get; set; } = new(true, "", "", 0);

        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default)
            => Task.FromResult(NextResult);

        public Task<StreamingProcess> RunStreamingAsync(ProcessRunOptions options, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private class FakeOptionsMonitor : IOptionsMonitor<AppSettings>
    {
        public AppSettings Settings { get; } = new();
        public AppSettings CurrentValue => Settings;
        public AppSettings Get(string? name) => Settings;
        public IDisposable? OnChange(Action<AppSettings, string?> listener) => null;
    }
}
