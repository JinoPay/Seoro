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
    private CliCapabilities? _capabilities;
    private readonly SemaphoreSlim _capLock = new(1, 1);

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
        var caps = await DetectCapabilitiesAsync(fileName, baseArgs);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var previous = Interlocked.Exchange(ref _internalCts, cts);
        previous?.Cancel();

        var arguments = BuildArguments(baseArgs, model, permissionMode, caps);
        var token = cts.Token;

        var process = StartClaudeProcess(fileName, arguments, workingDir);
        await process.StandardInput.WriteAsync(message);
        process.StandardInput.Close();

        var stderrBuilder = new StringBuilder();
        var stderrTask = CollectStderrAsync(process, stderrBuilder, token);

        var reader = process.StandardOutput;
        bool anyEvents = false;

        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(token); }
            catch (OperationCanceledException) { break; }

            if (string.IsNullOrWhiteSpace(line)) continue;

            StreamEvent? evt = null;
            try { evt = JsonSerializer.Deserialize<StreamEvent>(line); }
            catch (JsonException) { }

            if (evt != null)
            {
                anyEvents = true;
                yield return evt;
            }
        }

        await FinishProcess(process, token);
        try { await stderrTask; } catch { }

        var stderr = stderrBuilder.ToString().Trim();

        // Retry with --verbose if process failed before producing events
        if (!anyEvents && process.ExitCode != 0
            && stderr.Contains("requires --verbose", StringComparison.OrdinalIgnoreCase)
            && !caps.RequiresVerboseForStreamJson)
        {
            caps.RequiresVerboseForStreamJson = true;
            caps.SupportsVerbose = true;
            process.Dispose();
            Interlocked.CompareExchange(ref _currentProcess, null, process);

            arguments = BuildArguments(baseArgs, model, permissionMode, caps);
            process = StartClaudeProcess(fileName, arguments, workingDir);
            await process.StandardInput.WriteAsync(message);
            process.StandardInput.Close();

            stderrBuilder.Clear();
            stderrTask = CollectStderrAsync(process, stderrBuilder, token);
            reader = process.StandardOutput;

            while (!reader.EndOfStream && !token.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync(token); }
                catch (OperationCanceledException) { break; }

                if (string.IsNullOrWhiteSpace(line)) continue;

                StreamEvent? evt = null;
                try { evt = JsonSerializer.Deserialize<StreamEvent>(line); }
                catch (JsonException) { }

                if (evt != null)
                    yield return evt;
            }

            await FinishProcess(process, token);
            try { await stderrTask; } catch { }
            stderr = stderrBuilder.ToString().Trim();
        }

        if (!string.IsNullOrEmpty(stderr) && process.ExitCode != 0)
        {
            yield return new StreamEvent
            {
                Type = "error",
                Error = stderr
            };
        }

        process.Dispose();
        Interlocked.CompareExchange(ref _currentProcess, null, process);
    }

    private Process StartClaudeProcess(string fileName, string arguments, string workingDir)
    {
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

        process.Start();
        return process;
    }

    private static Task CollectStderrAsync(Process process, StringBuilder sb, CancellationToken token)
    {
        return Task.Run(async () =>
        {
            try
            {
                var errLine = await process.StandardError.ReadToEndAsync(token);
                if (!string.IsNullOrWhiteSpace(errLine))
                    sb.Append(errLine);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private static async Task FinishProcess(Process process, CancellationToken token)
    {
        if (token.IsCancellationRequested && process is { HasExited: false })
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }

        if (process is { HasExited: false })
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static string BuildArguments(string baseArgs, string model, string permissionMode, CliCapabilities caps)
    {
        var sb = new StringBuilder(baseArgs);
        sb.Append("--print --output-format stream-json ");
        if (caps.RequiresVerboseForStreamJson || caps.SupportsVerbose)
            sb.Append("--verbose ");
        sb.Append($"--model {model}");
        if (permissionMode == "plan")
            sb.Append(" --permission-mode plan");
        return sb.ToString();
    }

    private async Task<CliCapabilities> DetectCapabilitiesAsync(string fileName, string baseArgs)
    {
        if (_capabilities != null)
            return _capabilities;

        await _capLock.WaitAsync();
        try
        {
            if (_capabilities != null)
                return _capabilities;

            var caps = new CliCapabilities();

            var version = await RunSimpleCommand(fileName, $"{baseArgs}--version");
            caps.Version = version?.Trim() ?? "";

            var help = await RunSimpleCommand(fileName, $"{baseArgs}--help");
            if (help != null)
            {
                caps.SupportsVerbose = help.Contains("--verbose", StringComparison.OrdinalIgnoreCase);
            }

            _capabilities = caps;
            return caps;
        }
        finally
        {
            _capLock.Release();
        }
    }

    private static async Task<string?> RunSimpleCommand(string fileName, string arguments)
    {
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Environment = { ["NO_COLOR"] = "1" }
                }
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            proc.Dispose();
            return output;
        }
        catch
        {
            return null;
        }
    }

    public void Cancel()
    {
        _internalCts?.Cancel();
    }

    private static (string fileName, string argPrefix) ResolveClaudeCommand(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && configuredPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                return ("cmd.exe", $"/c \"{configuredPath}\" ");
            }
            return (configuredPath, "");
        }

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

        var path = TryWhich("/usr/bin/which", "claude");
        if (path != null)
            return (path, "");

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
