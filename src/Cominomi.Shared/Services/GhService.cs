using System.Diagnostics;
using System.Text.Json;

namespace Cominomi.Shared.Services;

public class GhService : IGhService
{
    public async Task<GitResult> CreatePrAsync(string repoDir, string head, string baseBranch, string title, string body, CancellationToken ct = default)
    {
        var escapedTitle = title.Replace("\"", "\\\"");
        var escapedBody = body.Replace("\"", "\\\"");
        return await RunGhAsync(
            $"pr create --base \"{baseBranch}\" --head \"{head}\" --title \"{escapedTitle}\" --body \"{escapedBody}\"",
            repoDir, ct);
    }

    public async Task<GitResult> MergePrAsync(string repoDir, int prNumber, string mergeMethod = "squash", CancellationToken ct = default)
    {
        return await RunGhAsync(
            $"pr merge {prNumber} --{mergeMethod} --delete-branch",
            repoDir, ct);
    }

    public async Task<PrInfo?> GetPrForBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
    {
        var result = await RunGhAsync(
            $"pr view \"{branchName}\" --json number,url,state",
            repoDir, ct);

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;
            return new PrInfo(
                root.GetProperty("number").GetInt32(),
                root.GetProperty("url").GetString() ?? "",
                root.GetProperty("state").GetString() ?? "");
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken ct = default)
    {
        var result = await RunGhAsync("auth status", ".", ct);
        return result.Success;
    }

    private static async Task<GitResult> RunGhAsync(string arguments, string workingDir, CancellationToken ct = default)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Environment =
                {
                    ["GH_NO_UPDATE_NOTIFIER"] = "1",
                    ["NO_COLOR"] = "1"
                }
            }
        };

        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var result = new GitResult(process.ExitCode == 0, stdout.Trim(), stderr.Trim());
        process.Dispose();
        return result;
    }
}
