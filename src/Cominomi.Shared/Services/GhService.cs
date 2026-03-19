using System.Text.Json;
using Cominomi.Shared;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class GhService : IGhService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<GhService> _logger;

    private static readonly Dictionary<string, string> GhEnv = CominomiConstants.Env.GhEnv;

    public GhService(IProcessRunner processRunner, ILogger<GhService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<GitResult> CreatePrAsync(string repoDir, string head, string baseBranch, string title, string body, CancellationToken ct = default)
    {
        return await RunGhWithRetryAsync(repoDir, ct,
            "pr", "create",
            "--base", baseBranch,
            "--head", head,
            "--title", title,
            "--body", body);
    }

    public async Task<GitResult> MergePrAsync(string repoDir, int prNumber, string mergeMethod = CominomiConstants.DefaultMergeStrategy, CancellationToken ct = default)
    {
        return await RunGhWithRetryAsync(repoDir, ct,
            "pr", "merge", prNumber.ToString(), $"--{mergeMethod}");
    }

    public async Task<GitResult> ClosePrAsync(string repoDir, int prNumber, CancellationToken ct = default)
    {
        return await RunGhWithRetryAsync(repoDir, ct,
            "pr", "close", prNumber.ToString());
    }

    public async Task<PrInfo?> GetPrForBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
    {
        var result = await RunGhWithRetryAsync(repoDir, ct,
            "pr", "view", branchName, "--json", "number,url,state");

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
        var safeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = await RunGhAsync(safeDir, ct, "auth", "status");
        return result.Success;
    }

    public async Task<GitResult> CreateIssueAsync(string repoDir, string title, string body, CancellationToken ct = default)
    {
        return await RunGhWithRetryAsync(repoDir, ct,
            "issue", "create",
            "--title", title,
            "--body", body);
    }

    public async Task<List<IssueInfo>> ListIssuesAsync(string repoDir, string state = "open", int limit = CominomiConstants.GhDefaultIssueLimit, CancellationToken ct = default)
    {
        var result = await RunGhWithRetryAsync(repoDir, ct,
            "issue", "list",
            "--state", state,
            "--limit", limit.ToString(),
            "--json", "number,url,title,state");

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
        var result = await RunGhWithRetryAsync(repoDir, ct,
            "issue", "view", issueNumber.ToString(),
            "--json", "number,url,title,state");

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

    public async Task<PrCheckResult> WaitForChecksAsync(string repoDir, int prNumber, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var token = cts.Token;

        var pollInterval = TimeSpan.FromSeconds(10);

        while (!token.IsCancellationRequested)
        {
            var result = await RunGhAsync(repoDir, token,
                "pr", "checks", prNumber.ToString(),
                "--json", "name,state,conclusion");

            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
            {
                try
                {
                    using var doc = JsonDocument.Parse(result.Output);
                    var checks = doc.RootElement;

                    if (checks.GetArrayLength() == 0)
                        return new PrCheckResult(true, false, "No checks configured");

                    bool anyPending = false;
                    bool anyFailed = false;
                    var failedNames = new List<string>();

                    foreach (var check in checks.EnumerateArray())
                    {
                        var state = check.GetProperty("state").GetString() ?? "";
                        var conclusion = check.TryGetProperty("conclusion", out var c) ? c.GetString() ?? "" : "";
                        var name = check.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

                        if (state is "PENDING" or "QUEUED" or "IN_PROGRESS" or "WAITING" or "REQUESTED")
                        {
                            anyPending = true;
                        }
                        else if (conclusion is not ("SUCCESS" or "NEUTRAL" or "SKIPPED"))
                        {
                            anyFailed = true;
                            failedNames.Add(name);
                        }
                    }

                    if (anyFailed)
                        return new PrCheckResult(false, false, $"Failed checks: {string.Join(", ", failedNames)}");

                    if (!anyPending)
                        return new PrCheckResult(true, false, "All checks passed");
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(ex, "Failed to parse PR checks JSON");
                }
            }

            try { await Task.Delay(pollInterval, token); }
            catch (OperationCanceledException) { break; }
        }

        return new PrCheckResult(false, true, "Timed out waiting for checks");
    }

    private async Task<GitResult> RunGhAsync(string workingDir, CancellationToken ct, params string[] args)
    {
        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "gh",
            Arguments = args,
            WorkingDirectory = workingDir,
            EnvironmentVariables = GhEnv,
            Timeout = TimeSpan.FromSeconds(30)
        }, ct);

        return new GitResult(result.Success, result.Stdout, result.Stderr);
    }

    private async Task<GitResult> RunGhWithRetryAsync(string workingDir, CancellationToken ct, params string[] args)
    {
        for (int attempt = 0; attempt <= CominomiConstants.GhMaxRetries; attempt++)
        {
            var result = await RunGhAsync(workingDir, ct, args);

            if (result.Success || !IsRateLimitError(result.Error))
                return result;

            if (attempt < CominomiConstants.GhMaxRetries)
            {
                var delay = TimeSpan.FromSeconds(CominomiConstants.GhRetryBaseDelaySeconds * Math.Pow(3, attempt));
                _logger.LogWarning("GitHub API rate limit hit, retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds, attempt + 1, CominomiConstants.GhMaxRetries);

                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { return result; }
            }
            else
            {
                return result;
            }
        }

        // Unreachable, but satisfies compiler
        return await RunGhAsync(workingDir, ct, args);
    }

    private static bool IsRateLimitError(string stderr)
    {
        if (string.IsNullOrEmpty(stderr)) return false;
        return stderr.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("secondary rate", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("API rate limit exceeded", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("abuse detection", StringComparison.OrdinalIgnoreCase);
    }
}
