using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default)
    {
        logger.LogDebug("Running: {FileName} {Args}", options.FileName, string.Join(" ", options.Arguments));

        var psi = CreateProcessStartInfo(options);
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

            var stdoutTask = options.MaxOutputBytes is { } maxBytes
                ? ReadBoundedAsync(process.StandardOutput, maxBytes, timeoutCts.Token)
                : ReadUnboundedAsync(process.StandardOutput, timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            var (stdout, truncated) = await stdoutTask;
            var stderr = await stderrTask;
            await process.WaitForExitAsync(timeoutCts.Token);

            if (truncated)
                logger.LogDebug("{FileName} stdout truncated at {MaxBytes} bytes", options.FileName,
                    options.MaxOutputBytes);

            logger.LogDebug("{FileName} exited with code {ExitCode}", options.FileName, process.ExitCode);
            return new ProcessResult(process.ExitCode == 0, stdout.Trim(), stderr.Trim(), process.ExitCode, truncated);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Timeout — not caller cancellation
            KillProcess(process, options.KillEntireProcessTree);
            logger.LogWarning("{FileName} timed out after {Timeout}s", options.FileName, timeout.TotalSeconds);
            return new ProcessResult(false, "", $"Process timed out after {timeout.TotalSeconds}s", -1);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled
            KillProcess(process, options.KillEntireProcessTree);
            throw;
        }
    }

    public Task<StreamingProcess> RunStreamingAsync(ProcessRunOptions options, CancellationToken ct = default)
    {
        logger.LogDebug("Running (streaming): {FileName} {Args}", options.FileName,
            string.Join(" ", options.Arguments));

        try
        {
            var psi = CreateProcessStartInfo(options);
            var process = new Process { StartInfo = psi };
            process.Start();

            // Capture stderr in background so the caller only needs to read stdout
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            return Task.FromResult(new StreamingProcess(process, stderrTask, options.KillEntireProcessTree, logger));
        }
        catch (FileNotFoundException ex)
        {
            logger.LogError(ex, "Executable not found: {FileName}", options.FileName);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start process: {FileName}", options.FileName);
            throw;
        }
    }

    private static ProcessStartInfo CreateProcessStartInfo(ProcessRunOptions options)
    {
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
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in options.Arguments)
            psi.ArgumentList.Add(arg);

        if (options.EnvironmentVariables != null)
            foreach (var (key, value) in options.EnvironmentVariables)
                psi.Environment[key] = value;

        return psi;
    }

    /// <summary>
    ///     Reads up to <paramref name="maxBytes" /> from the stream, then discards the rest.
    ///     Returns the captured text and whether the output was truncated.
    /// </summary>
    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(
        StreamReader reader, int maxBytes, CancellationToken ct)
    {
        var buffer = new char[8192];
        var sb = new StringBuilder(Math.Min(maxBytes, 65536));
        var totalBytes = 0;
        var truncated = false;

        while (true)
        {
            var remaining = maxBytes - totalBytes;
            if (remaining <= 0)
            {
                truncated = true;
                // Drain remaining output so the process doesn't block on a full pipe
                while (await reader.ReadAsync(buffer.AsMemory(), ct) > 0)
                {
                }

                break;
            }

            var toRead = Math.Min(buffer.Length, remaining);
            var read = await reader.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0) break;

            sb.Append(buffer, 0, read);
            totalBytes += Encoding.UTF8.GetByteCount(buffer, 0, read);
        }

        return (sb.ToString(), truncated);
    }

    private static async Task<(string Text, bool Truncated)> ReadUnboundedAsync(
        StreamReader reader, CancellationToken ct)
    {
        var text = await reader.ReadToEndAsync(ct);
        return (text, false);
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
            logger.LogDebug(ex, "Failed to kill process");
        }
    }
}