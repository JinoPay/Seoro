using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner> logger)
    {
        _logger = logger;
    }

    public async Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default)
    {
        _logger.LogDebug("Running: {FileName} {Args}", options.FileName, string.Join(" ", options.Arguments));

        var psi = new ProcessStartInfo
        {
            FileName = options.FileName,
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = options.StandardInput != null,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in options.Arguments)
            psi.ArgumentList.Add(arg);

        if (options.EnvironmentVariables != null)
        {
            foreach (var (key, value) in options.EnvironmentVariables)
                psi.Environment[key] = value;
        }

        using var process = new Process { StartInfo = psi };

        var timeout = options.Timeout ?? DefaultTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            process.Start();

            // Write stdin if provided, then close the stream
            if (options.StandardInput != null)
            {
                await process.StandardInput.WriteAsync(options.StandardInput);
                process.StandardInput.Close();
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            await process.WaitForExitAsync(timeoutCts.Token);

            _logger.LogDebug("{FileName} exited with code {ExitCode}", options.FileName, process.ExitCode);
            return new ProcessResult(process.ExitCode == 0, stdout.Trim(), stderr.Trim(), process.ExitCode);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Timeout — not caller cancellation
            KillProcess(process, options.KillEntireProcessTree);
            _logger.LogWarning("{FileName} timed out after {Timeout}s", options.FileName, timeout.TotalSeconds);
            return new ProcessResult(false, "", $"Process timed out after {timeout.TotalSeconds}s", -1);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled
            KillProcess(process, options.KillEntireProcessTree);
            throw;
        }
    }

    private void KillProcess(Process process, bool entireTree)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireTree);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to kill process");
        }
    }
}
