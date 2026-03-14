using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class ClaudeService : IClaudeService
{
    private Process? _currentProcess;
    private CancellationTokenSource? _internalCts;

    public async IAsyncEnumerable<StreamEvent> SendMessageAsync(
        string message,
        string workingDir,
        string model,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _internalCts.Token;

        var escapedMessage = message.Replace("\"", "\\\"");

        _currentProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = $"--print --output-format stream-json --model {model} \"{escapedMessage}\"",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Environment = { ["NO_COLOR"] = "1" }
            }
        };

        _currentProcess.Start();

        var reader = _currentProcess.StandardOutput;

        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            StreamEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<StreamEvent>(line);
            }
            catch (JsonException)
            {
                // skip malformed lines
            }

            if (evt != null)
                yield return evt;
        }

        if (token.IsCancellationRequested && _currentProcess is { HasExited: false })
        {
            _currentProcess.Kill(entireProcessTree: true);
        }

        if (_currentProcess is { HasExited: false })
        {
            await _currentProcess.WaitForExitAsync(CancellationToken.None);
        }

        _currentProcess?.Dispose();
        _currentProcess = null;
        _internalCts = null;
    }

    public void Cancel()
    {
        _internalCts?.Cancel();
    }
}
