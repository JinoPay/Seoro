using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Pty.Net;

namespace Seoro.Shared.Services.Infrastructure;

public class TerminalService(
    IShellService shellService,
    IPtySpawner ptySpawner,
    ILogger<TerminalService> logger)
    : ITerminalService
{
    private static readonly TimeSpan ReaderDrainTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ScrollbackSaveInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();
    private readonly SemaphoreSlim _startLock = new(1, 1);

    /// <summary>스크롤백 영속화 디렉터리 — 테스트에서 임시 경로로 대체 가능.</summary>
    protected virtual string ScrollbackDirectory => AppPaths.Sessions;

    public async ValueTask DisposeAsync()
    {
        var keys = _sessions.Keys.ToList();
        if (keys.Count == 0) return;

        var stopAll = Task.WhenAll(keys.Select(k => StopAsync(k)));
        await Task.WhenAny(stopAll, Task.Delay(ShutdownTimeout));
        logger.LogDebug("모든 터미널 세션 해제됨 ({Count} 세션)", keys.Count);
    }

    public event Action<string, int>? OnExited;

    public event Action<string, string>? OnOutput;

    public event Action<string, string>? OnError;

    public bool IsRunning(string sessionKey)
    {
        return _sessions.TryGetValue(sessionKey, out var s) && s.IsAlive;
    }

    public async Task StartAsync(string sessionKey, string workingDirectory, ShellInfo? shell = null, int? cols = null,
        int? rows = null)
    {
        await _startLock.WaitAsync();
        try
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

            var loginPath = await shellService.GetLoginShellPathAsync();
            if (!string.IsNullOrWhiteSpace(loginPath))
                env["PATH"] = loginPath;

            var initialCols = Math.Max(cols ?? 120, 20);
            var initialRows = Math.Max(rows ?? 30, 5);

            var options = new PtyOptions
            {
                Name = "Seoro Terminal",
                App = shell.FileName,
                CommandLine = args.ToArray(),
                Cwd = workingDirectory,
                Cols = initialCols,
                Rows = initialRows,
                Environment = env
            };

            var ptyConnection = await ptySpawner.SpawnAsync(options, CancellationToken.None);

            var cts = new CancellationTokenSource();
            var session = new TerminalSession(ptyConnection, cts);

            // 앱 재시작 후 같은 세션으로 돌아온 경우 디스크 스크롤백 복원
            var saved = await ReadScrollbackFileAsync(sessionKey);
            if (!string.IsNullOrEmpty(saved))
            {
                session.Buffer.Append(saved);
                session.Buffer.MarkSaved();
            }

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
            session.ReaderTask = ReadPtyOutputAsync(sessionKey, session, cts.Token);

            EvictLeastRecentlyUsed(sessionKey);
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task StopAsync(string sessionKey, bool saveScrollback = true)
    {
        if (!_sessions.TryRemove(sessionKey, out var session)) return;

        // 순서 중요: cancel → 리더 종료 대기 → flush → kill → dispose → cts dispose.
        // 리더가 같은 토큰으로 ReadAsync 중일 때 CTS를 dispose하면 ObjectDisposedException이 난다.
        await session.Cts.CancelAsync();

        if (session.ReaderTask != null)
            await Task.WhenAny(session.ReaderTask, Task.Delay(ReaderDrainTimeout));

        if (saveScrollback)
            await SaveScrollbackAsync(sessionKey, session);

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

        // Kill 후에야 블로킹이 풀리는 플랫폼(Windows ConPTY) 방어 — 한 번 더 대기
        if (session.ReaderTask is { IsCompleted: false })
            await Task.WhenAny(session.ReaderTask, Task.Delay(ReaderDrainTimeout));

        session.Cts.Dispose();
    }

    public async Task WriteAsync(string sessionKey, string data)
    {
        if (!_sessions.TryGetValue(sessionKey, out var session)) return;

        await session.WriteLock.WaitAsync();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            await session.Pty.WriterStream.WriteAsync(bytes);
            await session.Pty.WriterStream.FlushAsync();
            session.Touch();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "세션 {Key}의 PTY에 쓰기 실패", sessionKey);
            OnError?.Invoke(sessionKey, ex.Message);
            if (ex is IOException or ObjectDisposedException && session.IsAlive)
            {
                session.MarkExited();
                OnExited?.Invoke(sessionKey, -1);
            }
        }
        finally
        {
            session.WriteLock.Release();
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

    public string GetBufferedOutput(string sessionKey)
    {
        if (_sessions.TryGetValue(sessionKey, out var session))
            return session.Buffer.Snapshot();

        try
        {
            var path = GetScrollbackPath(sessionKey);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "세션 {Key}의 스크롤백 파일 읽기 실패", sessionKey);
            return "";
        }
    }

    public void NotifyAttached(string sessionKey)
    {
        if (_sessions.TryGetValue(sessionKey, out var session))
            session.Touch();
    }

    public Task DeleteScrollbackAsync(string sessionKey)
    {
        try
        {
            var path = GetScrollbackPath(sessionKey);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "세션 {Key}의 스크롤백 파일 삭제 실패", sessionKey);
        }

        return Task.CompletedTask;
    }

    private async Task ReadPtyOutputAsync(string sessionKey, TerminalSession session, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var lastSaveUtc = DateTime.UtcNow;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var bytesRead = await session.Pty.ReaderStream.ReadAsync(buffer, ct);
                if (bytesRead == 0)
                {
                    // EOF — 셸이 끝났는데 ProcessExited가 아직 안 왔을 수 있음
                    if (session.IsAlive)
                    {
                        logger.LogWarning("세션 {Key}의 PTY 출력 스트림 EOF", sessionKey);
                        session.MarkExited();
                        OnExited?.Invoke(sessionKey, -1);
                    }

                    break;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                session.Buffer.Append(text);
                OnOutput?.Invoke(sessionKey, text);

                if (session.Buffer.IsDirty && DateTime.UtcNow - lastSaveUtc >= ScrollbackSaveInterval)
                {
                    await SaveScrollbackAsync(sessionKey, session);
                    lastSaveUtc = DateTime.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "세션 {Key}의 PTY 출력 읽기 오류", sessionKey);
                OnError?.Invoke(sessionKey, ex.Message);
                if (session.IsAlive)
                {
                    session.MarkExited();
                    OnExited?.Invoke(sessionKey, -1);
                }
            }
        }
    }

    /// <summary>라이브 PTY가 상한을 넘으면 가장 오래 사용되지 않은 세션부터 정리 (스크롤백은 디스크에 보존).</summary>
    private void EvictLeastRecentlyUsed(string justStartedKey)
    {
        var excess = _sessions.Count - SeoroConstants.MaxLiveTerminals;
        if (excess <= 0) return;

        var victims = _sessions
            .Where(kv => kv.Key != justStartedKey)
            .OrderBy(kv => kv.Value.LastActivityUtc)
            .Take(excess)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in victims)
        {
            logger.LogInformation("터미널 상한({Max}) 초과 — 가장 오래된 세션 {Key} 정리",
                SeoroConstants.MaxLiveTerminals, key);
            _ = StopAsync(key);
        }
    }

    private string GetScrollbackPath(string sessionKey)
    {
        return Path.Combine(ScrollbackDirectory, $"{sessionKey}.terminal.txt");
    }

    private async Task SaveScrollbackAsync(string sessionKey, TerminalSession session)
    {
        if (!session.Buffer.IsDirty) return;

        try
        {
            await AtomicFileWriter.WriteAsync(GetScrollbackPath(sessionKey), session.Buffer.Snapshot());
            session.Buffer.MarkSaved();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "세션 {Key}의 스크롤백 저장 실패", sessionKey);
        }
    }

    private async Task<string> ReadScrollbackFileAsync(string sessionKey)
    {
        try
        {
            var path = GetScrollbackPath(sessionKey);
            return File.Exists(path) ? await File.ReadAllTextAsync(path) : "";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "세션 {Key}의 스크롤백 파일 읽기 실패", sessionKey);
            return "";
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
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public Task? ReaderTask { get; set; }
        public DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;
        public TerminalScrollbackBuffer Buffer { get; } = new();

        public void MarkExited()
        {
            _exited = true;
        }

        public void Touch()
        {
            LastActivityUtc = DateTime.UtcNow;
        }
    }
}
