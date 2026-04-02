namespace Cominomi.Shared.Services;

public interface ITerminalService : IAsyncDisposable
{
    /// <summary>Fired when the shell process exits. Args: (sessionKey, exitCode)</summary>
    event Action<string, int>? OnExited;

    /// <summary>Fired when the shell produces stdout/stderr output. Args: (sessionKey, data)</summary>
    event Action<string, string>? OnOutput;

    /// <summary>True when a shell process is alive for the given key.</summary>
    bool IsRunning(string sessionKey);

    /// <summary>
    ///     Start a new shell process. WorkingDirectory = session worktree.
    ///     When <paramref name="shell" /> is null, uses the user's preferred terminal shell from settings.
    /// </summary>
    Task StartAsync(string sessionKey, string workingDirectory, ShellInfo? shell = null);

    /// <summary>Kill the shell process for the given session.</summary>
    Task StopAsync(string sessionKey);

    /// <summary>Write raw text from xterm.js to the shell's stdin.</summary>
    Task WriteAsync(string sessionKey, string data);

    /// <summary>Resize the PTY for the given session.</summary>
    void Resize(string sessionKey, int cols, int rows);
}