namespace Seoro.Shared.Services.Infrastructure;

public interface ITerminalService : IAsyncDisposable
{
    /// <summary>Fired when the shell process exits. Args: (sessionKey, exitCode)</summary>
    event Action<string, int>? OnExited;

    /// <summary>Fired when the shell produces stdout/stderr output. Args: (sessionKey, data)</summary>
    event Action<string, string>? OnOutput;

    /// <summary>Fired when a PTY read/write fails. Args: (sessionKey, error message)</summary>
    event Action<string, string>? OnError;

    /// <summary>True when a shell process is alive for the given key.</summary>
    bool IsRunning(string sessionKey);

    /// <summary>
    ///     Start a new shell process. WorkingDirectory = session worktree.
    ///     When <paramref name="shell" /> is null, uses the user's preferred terminal shell from settings.
    /// </summary>
    Task StartAsync(string sessionKey, string workingDirectory, ShellInfo? shell = null, int? cols = null,
        int? rows = null);

    /// <summary>Kill the shell process for the given session. Scrollback is persisted unless disabled.</summary>
    Task StopAsync(string sessionKey, bool saveScrollback = true);

    /// <summary>Write raw text from xterm.js to the shell's stdin.</summary>
    Task WriteAsync(string sessionKey, string data);

    /// <summary>Resize the PTY for the given session.</summary>
    void Resize(string sessionKey, int cols, int rows);

    /// <summary>
    ///     Accumulated raw output for replay when attaching the panel to a session.
    ///     Falls back to the persisted scrollback file when no live session exists.
    /// </summary>
    string GetBufferedOutput(string sessionKey);

    /// <summary>Mark the session as recently used (panel attached) for LRU eviction.</summary>
    void NotifyAttached(string sessionKey);

    /// <summary>Delete the persisted scrollback file (e.g. when the session is deleted).</summary>
    Task DeleteScrollbackAsync(string sessionKey);
}
