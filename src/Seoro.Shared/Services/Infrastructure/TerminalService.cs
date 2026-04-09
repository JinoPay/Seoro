using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Pty.Net;

namespace Seoro.Shared.Services.Infrastructure;

public class TerminalService(IShellService shellService, ILogger<TerminalService> logger)
    : ITerminalService
{
    private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();

    public async ValueTask DisposeAsync()
    {
        var keys = _sessions.Keys.ToList();
        foreach (var key in keys)
            await StopAsync(key);
        if (keys.Count > 0)
            logger.LogDebug("모든 터미널 세션 해제됨 ({Count} 세션)", keys.Count);
    }

    public event Action<string, int>? OnExited;

    public event Action<string, string>? OnOutput;

    public bool IsRunning(string sessionKey)
    {
        return _sessions.TryGetValue(sessionKey, out var s) && s.IsAlive;
    }

    public async Task StartAsync(string sessionKey, string workingDirectory, ShellInfo? shell = null)
    {
        // Stop existing session if any
        await StopAsync(sessionKey);

        // Validate working directory exists — fall back to current directory
        if (!Directory.Exists(workingDirectory))
        {
            logger.LogWarning("터미널 CWD가 존재하지 않음: {Dir}, 현재 디렉토리로 대체",
                workingDirectory);
            workingDirectory = Environment.CurrentDirectory;
        }

        shell ??= await shellService.GetTerminalShellAsync();
        logger.LogInformation("세션 {Key}에 대한 PTY 터미널 시작 (셸: {Shell}, 디렉토리: {Dir})",
            sessionKey, shell.Type, workingDirectory);

        var args = new List<string>();
        if (shell.Type is ShellType.Bash or ShellType.Zsh)
        {
            args.Add("--login");
            args.Add("-i");
        }

        var env = new Dictionary<string, string>
        {
            ["TERM"] = "xterm-256color"
        };

        var options = new PtyOptions
        {
            Name = "Seoro Terminal",
            App = shell.FileName,
            CommandLine = args.ToArray(),
            Cwd = workingDirectory,
            Cols = 120,
            Rows = 30,
            Environment = env
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
            logger.LogInformation("세션 {Key}의 PTY 프로세스 종료 (코드: {Code})",
                sessionKey, e.ExitCode);
            session.MarkExited();
            OnExited?.Invoke(sessionKey, e.ExitCode);
        };

        // Background task to read PTY output
        _ = ReadPtyOutputAsync(sessionKey, ptyConnection, cts.Token);
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
            logger.LogDebug(ex, "세션 {Key}의 PTY 프로세스 종료 실패", sessionKey);
        }

        try
        {
            session.Pty.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "세션 {Key}의 PTY 해제 실패", sessionKey);
        }

        session.Cts.Dispose();
        return Task.CompletedTask;
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
            logger.LogDebug(ex, "세션 {Key}의 PTY에 쓰기 실패", sessionKey);
        }
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
            logger.LogDebug(ex, "세션 {Key}의 PTY 크기 조정 실패", sessionKey);
        }
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
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "세션 {Key}의 PTY 출력 읽기 오류", sessionKey);
        }
    }

    private sealed class TerminalSession
    {
        private bool _exited;

        public TerminalSession(IPtyConnection pty, CancellationTokenSource cts)
        {
            Pty = pty;
            Cts = cts;
        }

        public bool IsAlive => !_exited;
        public CancellationTokenSource Cts { get; }
        public IPtyConnection Pty { get; }

        public void MarkExited()
        {
            _exited = true;
        }
    }
}