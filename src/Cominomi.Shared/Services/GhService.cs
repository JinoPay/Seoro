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
        return await RunGhAsync(repoDir, ct,
            "pr", "create",
            "--base", baseBranch,
            "--head", head,
            "--title", title,
            "--body", body);
    }

    public async Task<GitResult> MergePrAsync(string repoDir, int prNumber, string mergeMethod = CominomiConstants.DefaultMergeStrategy, CancellationToken ct = default)
    {
        return await RunGhAsync(repoDir, ct,
            "pr", "merge", prNumber.ToString(), $"--{mergeMethod}");
    }

    public async Task<GitResult> ClosePrAsync(string repoDir, int prNumber, CancellationToken ct = default)
    {
        return await RunGhAsync(repoDir, ct,
            "pr", "close", prNumber.ToString());
    }

    public async Task<PrInfo?> GetPrForBranchAsync(string repoDir, string branchName, CancellationToken ct = default)
    {
        var result = await RunGhAsync(repoDir, ct,
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
        var result = await RunGhAsync(".", ct, "auth", "status");
        return result.Success;
    }

    public async Task<GitResult> CreateIssueAsync(string repoDir, string title, string body, CancellationToken ct = default)
    {
        return await RunGhAsync(repoDir, ct,
            "issue", "create",
            "--title", title,
            "--body", body);
    }

    public async Task<List<IssueInfo>> ListIssuesAsync(string repoDir, string state = "open", int limit = 30, CancellationToken ct = default)
    {
        var result = await RunGhAsync(repoDir, ct,
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
        var result = await RunGhAsync(repoDir, ct,
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
}
