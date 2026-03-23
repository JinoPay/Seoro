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

            if (result.Success || !ProcessErrorClassifier.IsGhRateLimitError(result.Error))
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

}
