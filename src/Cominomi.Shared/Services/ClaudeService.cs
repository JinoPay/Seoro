using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class ClaudeService : IClaudeService
{
    private readonly ISettingsService _settingsService;
    private volatile CancellationTokenSource? _internalCts;
    private volatile Process? _currentProcess;

    public ClaudeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async IAsyncEnumerable<StreamEvent> SendMessageAsync(
        string message,
        string workingDir,
        string model,
        string permissionMode = "default",
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var settings = await _settingsService.LoadAsync();
        var (fileName, baseArgs) = ResolveClaudeCommand(settings.ClaudePath);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var previous = Interlocked.Exchange(ref _internalCts, cts);
        previous?.Cancel();

        var arguments = $"{baseArgs}--print --output-format stream-json --model {model}";
        if (permissionMode == "plan")
            arguments += " --permission-mode plan";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                Environment = { ["NO_COLOR"] = "1" }
            }
        };

        var previousProcess = Interlocked.Exchange(ref _currentProcess, process);
        if (previousProcess is { HasExited: false })
        {
            try { previousProcess.Kill(entireProcessTree: true); } catch { }
            previousProcess.Dispose();
        }

        var token = cts.Token;

        process.Start();

        // Send message via stdin to avoid argument escaping issues
        await process.StandardInput.WriteAsync(message);
        process.StandardInput.Close();

        // Collect stderr asynchronously
        var stderrBuilder = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            try
            {
                var errLine = await process.StandardError.ReadToEndAsync(token);
                if (!string.IsNullOrWhiteSpace(errLine))
                    stderrBuilder.Append(errLine);
            }
            catch (OperationCanceledException) { }
        }, token);

        var reader = process.StandardOutput;

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

        if (token.IsCancellationRequested && process is { HasExited: false })
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }

        if (process is { HasExited: false })
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }

        // Wait for stderr collection to finish
        try { await stderrTask; } catch { }

        // If there was stderr output and process exited with error, yield an error event
        if (stderrBuilder.Length > 0 && process.ExitCode != 0)
        {
            yield return new StreamEvent
            {
                Type = "error",
                Error = stderrBuilder.ToString().Trim()
            };
        }

        process.Dispose();
        Interlocked.CompareExchange(ref _currentProcess, null, process);
    }

    public void Cancel()
    {
        _internalCts?.Cancel();
    }

    private static (string fileName, string argPrefix) ResolveClaudeCommand(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            // On Windows, .cmd files need cmd.exe wrapping when UseShellExecute=false
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && configuredPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                return ("cmd.exe", $"/c \"{configuredPath}\" ");
            }
            return (configuredPath, "");
        }

        // Auto-detect
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var resolved = TryWhich("where.exe", "claude");
            if (resolved != null)
            {
                if (resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                    return ("cmd.exe", $"/c \"{resolved}\" ");
                return (resolved, "");
            }
            return ("claude.exe", "");
        }

        // macOS / Linux
        var path = TryWhich("/usr/bin/which", "claude");
        if (path != null)
            return (path, "");

        // Common install locations
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm", "bin", "claude"),
            "/usr/local/bin/claude",
            "/opt/homebrew/bin/claude"
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return (candidate, "");
        }

        return ("claude", "");
    }

    private static string? TryWhich(string whichCommand, string target)
    {
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = whichCommand,
                    Arguments = target,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadLine()?.Trim();
            proc.WaitForExit(3000);
            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                return output;
        }
        catch { }

        return null;
    }
}
