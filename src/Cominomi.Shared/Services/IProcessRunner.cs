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
}

public record ProcessResult(bool Success, string Stdout, string Stderr, int ExitCode);

public interface IProcessRunner
{
    /// <summary>
    /// Run a process, collect stdout/stderr, and return the result.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default);
}
