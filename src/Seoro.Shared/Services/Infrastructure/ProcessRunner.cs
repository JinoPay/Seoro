using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Infrastructure;

public class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default)
    {
        logger.LogDebug("실행 중: {FileName} {Args}", options.FileName, string.Join(" ", options.Arguments));

        var psi = CreateProcessStartInfo(options);
        using var process = new Process { StartInfo = psi };

        var timeout = options.Timeout ?? DefaultTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            // macOS: Start() must run off the AppKit main thread.  When .NET's
            // Process.Start() falls back to fork(), the child inherits the main
            // thread's dispatch-queue state and crashes (SIGSEGV) before exec().
            // Task.Run() ensures fork() happens on a ThreadPool thread whose
            // Thread-0 copy in the child has no AppKit run-loop to drain.
            if (OperatingSystem.IsMacOS())
                await Task.Run(() => process.Start());
            else
                process.Start();

            // 표준 입력이 제공되면 작성한 후 스트림 닫기
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
                logger.LogDebug("{FileName} 표준 출력이 {MaxBytes} 바이트에서 잘림", options.FileName,
                    options.MaxOutputBytes);

            logger.LogDebug("{FileName} 종료 코드 {ExitCode}로 종료됨", options.FileName, process.ExitCode);
            return new ProcessResult(process.ExitCode == 0, stdout.Trim(), stderr.Trim(), process.ExitCode, truncated);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // 시간 초과 — 호출자 취소 아님
            KillProcess(process, options.KillEntireProcessTree);
            logger.LogWarning("{FileName}이(가) {Timeout}초 후 시간 초과됨", options.FileName, timeout.TotalSeconds);
            return new ProcessResult(false, "", $"프로세스가 {timeout.TotalSeconds}초 후 시간 초과됨", -1);
        }
        catch (OperationCanceledException)
        {
            // 호출자가 취소함
            KillProcess(process, options.KillEntireProcessTree);
            throw;
        }
    }

    public async Task<StreamingProcess> RunStreamingAsync(ProcessRunOptions options, CancellationToken ct = default)
    {
        logger.LogDebug("실행 중 (스트리밍): {FileName} {Args}", options.FileName,
            string.Join(" ", options.Arguments));

        try
        {
            var psi = CreateProcessStartInfo(options);
            var process = new Process { StartInfo = psi };

            // macOS: keep fork() off the AppKit main thread (see RunAsync comment).
            if (OperatingSystem.IsMacOS())
                await Task.Run(() => process.Start());
            else
                process.Start();

            // 호출자가 표준 출력만 읽으면 되도록 백그라운드에서 stderr 캡처
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            return new StreamingProcess(process, stderrTask, options.KillEntireProcessTree, logger);
        }
        catch (FileNotFoundException ex)
        {
            logger.LogError(ex, "실행 파일을 찾을 수 없음: {FileName}", options.FileName);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "프로세스 시작 실패: {FileName}", options.FileName);
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
    ///     스트림에서 <paramref name="maxBytes" />까지 읽은 후 나머지를 버립니다.
    ///     캡처된 텍스트와 출력이 잘렸는지 여부를 반환합니다.
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
                // 프로세스가 가득 찬 파이프에서 차단되지 않도록 남은 출력 비우기
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
            logger.LogDebug(ex, "프로세스 종료 실패");
        }
    }
}