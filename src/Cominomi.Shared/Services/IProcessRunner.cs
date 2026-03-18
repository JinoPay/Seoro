using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public record ProcessRunOptions
{
    public required string FileName { get; init; }
    public string[] Arguments { get; init; } = [];
    public string WorkingDirectory { get; init; } = ".";
    public TimeSpan? Timeout { get; init; }
    public Dictionary<string, string>? EnvironmentVariables { get; init; }
    public bool KillEntireProcessTree { get; init; } = true;

    /// <summary>
    /// Optional string to write to the process's standard input. When set, stdin is redirected
    /// and the content is written then the stream is closed.
    /// </summary>
    public string? StandardInput { get; init; }

    /// <summary>
    /// Maximum bytes to read from stdout. When exceeded, output is truncated and
    /// <see cref="ProcessResult.Truncated"/> is set to <c>true</c>.
    /// Null (default) means unlimited — the entire stream is buffered.
    /// </summary>
    public int? MaxOutputBytes { get; init; }
}

public record ProcessResult(bool Success, string Stdout, string Stderr, int ExitCode, bool Truncated = false);

/// <summary>
/// Wraps a running process for streaming stdout line-by-line.
/// Stderr is captured in-memory for the final result.
/// Dispose to kill / clean up the process.
/// </summary>
public sealed class StreamingProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stderrTask;
    private readonly bool _killEntireTree;
    private readonly ILogger? _logger;
    private bool _disposed;

    internal StreamingProcess(Process process, Task<string> stderrTask, bool killEntireTree, ILogger? logger = null)
    {
        _process = process;
        _stderrTask = stderrTask;
        _killEntireTree = killEntireTree;
        _logger = logger;
    }

    /// <summary>Read a single line from stdout. Returns null at EOF.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken ct = default)
        => await _process.StandardOutput.ReadLineAsync(ct);

    /// <summary>Wait for the process to exit and return exit-code + captured stderr.</summary>
    public async Task<(int ExitCode, string Stderr)> WaitForExitAsync(CancellationToken ct = default)
    {
        await _process.WaitForExitAsync(ct);
        var stderr = await _stderrTask;
        return (_process.ExitCode, stderr.Trim());
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
                _process.Kill(_killEntireTree);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "StreamingProcess: failed to kill process (likely already exited)");
        }

        // Drain stderr to avoid deadlock
        try { await _stderrTask; }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "StreamingProcess: stderr reading failed — error details may have been lost");
        }

        _process.Dispose();
    }
}

public interface IProcessRunner
{
    /// <summary>
    /// Run a process, collect stdout/stderr, and return the result.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default);

    /// <summary>
    /// Start a process and return a handle for streaming stdout.
    /// The caller is responsible for reading stdout and disposing the handle.
    /// </summary>
    Task<StreamingProcess> RunStreamingAsync(ProcessRunOptions options, CancellationToken ct = default);
}
