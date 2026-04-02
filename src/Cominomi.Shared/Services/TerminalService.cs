using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Pty.Net;

namespace Cominomi.Shared.Services;

public class TerminalService : ITerminalService
{
    private readonly IShellService _shellService;
    private readonly ILogger<TerminalService> _logger;
    private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();

    public event Action<string, string>? OnOutput;
    public event Action<string, int>? OnExited;

    public TerminalService(IShellService shellService, ILogger<TerminalService> logger)
    {
        _shellService = shellService;
        _logger = logger;
    }

    public bool IsRunning(string sessionKey)
        => _sessions.TryGetValue(sessionKey, out var s) && s.IsAlive;

    public async Task StartAsync(string sessionKey, string workingDirectory, ShellInfo? shell = null)
    {
        // Stop existing session if any
        await StopAsync(sessionKey);

        // Validate working directory exists — fall back to current directory
        if (!Directory.Exists(workingDirectory))
        {
            _logger.LogWarning("Terminal CWD does not exist: {Dir}, falling back to current directory", workingDirectory);
            workingDirectory = Environment.CurrentDirectory;
        }

        shell ??= await _shellService.GetTerminalShellAsync();
        _logger.LogInformation("Starting PTY terminal for session {Key} with {Shell} in {Dir}",
            sessionKey, shell.Type, workingDirectory);

        var args = new List<string>();
        if (shell.Type is ShellType.Bash or ShellType.Zsh)
        {
            args.Add("--login");
            args.Add("-i");
        }

        var env = new Dictionary<string, string>
        {
            ["TERM"] = "xterm-256color",
        };

        var options = new PtyOptions
        {
            Name = "Cominomi Terminal",
            App = shell.FileName,
            CommandLine = args.ToArray(),
            Cwd = workingDirectory,
            Cols = 120,
            Rows = 30,
            Environment = env,
        };

        var ptyConnection = await PtyProvider.SpawnAsync(options, CancellationToken.None);

        var cts = new CancellationTokenSource();
        var session = new TerminalSession(ptyConnection, cts);

        if (!_sessions.TryAdd(sessionKey, session))
        {
            ptyConnection.Kill();
            ptyConnection.Dispose();
            return;
        }

        ptyConnection.ProcessExited += (_, e) =>
        {
            _logger.LogInformation("PTY process exited for session {Key} with code {Code}",
                sessionKey, e.ExitCode);
            session.MarkExited();
            OnExited?.Invoke(sessionKey, e.ExitCode);
        };

        // Background task to read PTY output
        _ = ReadPtyOutputAsync(sessionKey, ptyConnection, cts.Token);
    }

    public async Task WriteAsync(string sessionKey, string data)
    {
        if (!_sessions.TryGetValue(sessionKey, out var session)) return;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            await session.Pty.WriterStream.WriteAsync(bytes);
            await session.Pty.WriterStream.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write to PTY for session {Key}", sessionKey);
        }
    }

    public Task StopAsync(string sessionKey)
    {
        if (!_sessions.TryRemove(sessionKey, out var session)) return Task.CompletedTask;

        session.Cts.Cancel();
        try
        {
            if (session.IsAlive)
                session.Pty.Kill();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to kill PTY process for session {Key}", sessionKey);
        }
        try
        {
            session.Pty.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to dispose PTY for session {Key}", sessionKey);
        }
        session.Cts.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Resize the PTY for the given session.</summary>
    public void Resize(string sessionKey, int cols, int rows)
    {
        if (!_sessions.TryGetValue(sessionKey, out var session) || !session.IsAlive) return;

        try
        {
            session.Pty.Resize(cols, rows);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resize PTY for session {Key}", sessionKey);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var keys = _sessions.Keys.ToList();
        foreach (var key in keys)
            await StopAsync(key);
        if (keys.Count > 0)
            _logger.LogDebug("All terminal sessions disposed ({Count} sessions)", keys.Count);
    }

    private async Task ReadPtyOutputAsync(string sessionKey, IPtyConnection pty, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var isFirstChunk = true;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var bytesRead = await pty.ReaderStream.ReadAsync(buffer, ct);
                if (bytesRead == 0) break;

                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                // Strip leading blank lines from the first output (Git Bash PS1 starts with \n)
                if (isFirstChunk)
                {
                    text = text.TrimStart('\r', '\n');
                    isFirstChunk = false;
                    if (text.Length == 0) continue;
                }

                OnOutput?.Invoke(sessionKey, text);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PTY output read error for session {Key}", sessionKey);
        }
    }

    private sealed class TerminalSession
    {
        public IPtyConnection Pty { get; }
        public CancellationTokenSource Cts { get; }
        private bool _exited;

        public bool IsAlive => !_exited;

        public TerminalSession(IPtyConnection pty, CancellationTokenSource cts)
        {
            Pty = pty;
            Cts = cts;
        }

        public void MarkExited() => _exited = true;
    }
}
