using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class GhService : IGhService
{
    private readonly ILogger<GhService> _logger;

    public GhService(ILogger<GhService> logger)
    {
        _logger = logger;
    }

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
            $"pr merge {prNumber} --{mergeMethod}",
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse PR info JSON for branch {Branch}", branchName);
            return null;
        }
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken ct = default)
    {
        var result = await RunGhAsync("auth status", ".", ct);
        return result.Success;
    }

    public async Task<GitResult> CreateIssueAsync(string repoDir, string title, string body, CancellationToken ct = default)
    {
        var escapedTitle = title.Replace("\"", "\\\"");
        var escapedBody = body.Replace("\"", "\\\"");
        return await RunGhAsync(
            $"issue create --title \"{escapedTitle}\" --body \"{escapedBody}\"",
            repoDir, ct);
    }

    public async Task<List<IssueInfo>> ListIssuesAsync(string repoDir, string state = "open", int limit = 30, CancellationToken ct = default)
    {
        var result = await RunGhAsync(
            $"issue list --state {state} --limit {limit} --json number,url,title,state",
            repoDir, ct);

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            var issues = new List<IssueInfo>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                issues.Add(new IssueInfo(
                    el.GetProperty("number").GetInt32(),
                    el.GetProperty("url").GetString() ?? "",
                    el.GetProperty("title").GetString() ?? "",
                    el.GetProperty("state").GetString() ?? ""));
            }
            return issues;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse issue list JSON");
            return [];
        }
    }

    public async Task<IssueInfo?> GetIssueAsync(string repoDir, int issueNumber, CancellationToken ct = default)
    {
        var result = await RunGhAsync(
            $"issue view {issueNumber} --json number,url,title,state",
            repoDir, ct);

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;
            return new IssueInfo(
                root.GetProperty("number").GetInt32(),
                root.GetProperty("url").GetString() ?? "",
                root.GetProperty("title").GetString() ?? "",
                root.GetProperty("state").GetString() ?? "");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse issue info JSON for #{Number}", issueNumber);
            return null;
        }
    }

    private async Task<GitResult> RunGhAsync(string arguments, string workingDir, CancellationToken ct = default)
    {
        _logger.LogDebug("gh {Arguments}", arguments);
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

        _logger.LogDebug("gh exited with code {ExitCode}", process.ExitCode);
        var result = new GitResult(process.ExitCode == 0, stdout.Trim(), stderr.Trim());
        process.Dispose();
        return result;
    }
}
