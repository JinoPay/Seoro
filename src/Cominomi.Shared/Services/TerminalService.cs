using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

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
        => _sessions.TryGetValue(sessionKey, out var s) && !s.Process.HasExited;

    public async Task StartAsync(string sessionKey, string workingDirectory)
    {
        // Stop existing session if any
        await StopAsync(sessionKey);

        var shell = await _shellService.GetShellAsync();
        _logger.LogInformation("Starting terminal for session {Key} with {Shell} in {Dir}",
            sessionKey, shell.Type, workingDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = shell.FileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // For Git Bash on Windows, start in interactive login mode
        if (shell.Type == ShellType.Bash)
        {
            psi.ArgumentList.Add("--login");
            psi.ArgumentList.Add("-i");
        }
        else if (shell.Type == ShellType.Cmd)
        {
            // cmd.exe interactive mode (no /c)
        }

        // Set TERM to enable basic color support
        psi.Environment["TERM"] = "xterm-256color";

        var process = new Process { StartInfo = psi };
        process.Start();

        var cts = new CancellationTokenSource();
        var session = new TerminalSession(process, cts);

        if (!_sessions.TryAdd(sessionKey, session))
        {
            process.Kill(true);
            process.Dispose();
            return;
        }

        // Background tasks to read stdout and stderr in chunks
        _ = ReadOutputAsync(sessionKey, process.StandardOutput, cts.Token);
        _ = ReadOutputAsync(sessionKey, process.StandardError, cts.Token);

        // Monitor process exit
        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync(cts.Token);
                _logger.LogInformation("Terminal process exited for session {Key} with code {Code}",
                    sessionKey, process.ExitCode);
                OnExited?.Invoke(sessionKey, process.ExitCode);
            }
            catch (OperationCanceledException) { }
        }, cts.Token);
    }

    public async Task WriteAsync(string sessionKey, string data)
    {
        if (!_sessions.TryGetValue(sessionKey, out var session)) return;

        try
        {
            await session.Process.StandardInput.WriteAsync(data);
            await session.Process.StandardInput.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write to terminal stdin for session {Key}", sessionKey);
        }
    }

    public Task StopAsync(string sessionKey)
    {
        if (!_sessions.TryRemove(sessionKey, out var session)) return Task.CompletedTask;

        session.Cts.Cancel();
        try
        {
            if (!session.Process.HasExited)
                session.Process.Kill(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to kill terminal process for session {Key}", sessionKey);
        }
        session.Process.Dispose();
        session.Cts.Dispose();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        var keys = _sessions.Keys.ToList();
        foreach (var key in keys)
            await StopAsync(key);
    }

    private async Task ReadOutputAsync(string sessionKey, StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var bytesRead = await reader.ReadAsync(buffer.AsMemory(), ct);
                if (bytesRead == 0) break; // EOF

                var text = new string(buffer, 0, bytesRead);
                OnOutput?.Invoke(sessionKey, text);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Terminal output read error for session {Key}", sessionKey);
        }
    }

    private sealed record TerminalSession(Process Process, CancellationTokenSource Cts);
}
