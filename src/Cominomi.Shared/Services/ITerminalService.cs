namespace Cominomi.Shared.Services;

public interface ITerminalService : IAsyncDisposable
{
    /// <summary>True when a shell process is alive for the given key.</summary>
    bool IsRunning(string sessionKey);

    /// <summary>Start a new shell process. WorkingDirectory = session worktree.</summary>
    Task StartAsync(string sessionKey, string workingDirectory);

    /// <summary>Write raw text from xterm.js to the shell's stdin.</summary>
    Task WriteAsync(string sessionKey, string data);

    /// <summary>Kill the shell process for the given session.</summary>
    Task StopAsync(string sessionKey);

    /// <summary>Fired when the shell produces stdout/stderr output. Args: (sessionKey, data)</summary>
    event Action<string, string>? OnOutput;

    /// <summary>Fired when the shell process exits. Args: (sessionKey, exitCode)</summary>
    event Action<string, int>? OnExited;
}
