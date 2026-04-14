using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seoro.Shared.Services.Git;

public partial class PullRequestService(
    IProcessRunner processRunner,
    IShellService shellService,
    IHooksEngine hooksEngine,
    IOptionsMonitor<AppSettings> appSettings,
    ILogger<PullRequestService> logger) : IPullRequestService
{
    private static readonly TimeSpan GhPathCacheTtl = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _ghPathLock = new(1, 1);
    private DateTime _ghPathResolvedAt;
    private string? _resolvedGhPath;

    public async Task<TrackedPullRequest?> TryCaptureCreatedPrAsync(Session session, ChatMessage assistantMessage,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(session.Git.WorktreePath))
            return null;

        foreach (var url in ExtractCandidateUrls(assistantMessage))
        {
            var pr = await QueryAsync(session.Git.WorktreePath, url, ct);
            if (pr == null)
                continue;

            if (!string.Equals(BranchRefNormalizer.Normalize(pr.HeadBranch),
                    BranchRefNormalizer.Normalize(session.Git.BranchName),
                    StringComparison.OrdinalIgnoreCase))
                continue;

            session.Git.TrackedPr = pr;
            FireHookSafe(HookEvent.OnPrCreate, pr);
            return pr;
        }

        return null;
    }

    public async Task<TrackedPullRequest?> GetPrForBranchAsync(Session session, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(session.Git.WorktreePath) ||
            string.IsNullOrWhiteSpace(session.Git.BranchName))
            return null;

        var pr = await QueryAsync(session.Git.WorktreePath, session.Git.BranchName, ct);
        if (pr == null)
            return null;

        session.Git.TrackedPr = pr;
        FireHookSafe(HookEvent.OnPrCreate, pr);
        return pr;
    }

    public async Task<TrackedPullRequest?> RefreshAsync(Session session, CancellationToken ct = default)
    {
        var current = session.Git.TrackedPr;
        if (current == null || string.IsNullOrWhiteSpace(current.Url) ||
            string.IsNullOrWhiteSpace(session.Git.WorktreePath))
            return null;

        var refreshed = await QueryAsync(session.Git.WorktreePath, current.Url, ct);
        if (refreshed == null)
            return null;

        session.Git.TrackedPr = refreshed;
        return refreshed;
    }

    public async Task<PullRequestMergeResult> MergeAsync(Session session, PullRequestMergeStrategy strategy,
        CancellationToken ct = default)
    {
        var trackedPr = session.Git.TrackedPr;
        if (trackedPr == null || string.IsNullOrWhiteSpace(trackedPr.Url))
            return new PullRequestMergeResult(false, null, "추적 중인 PR이 없습니다.");

        var ghPath = await ResolveGhPathAsync();
        var flag = strategy switch
        {
            PullRequestMergeStrategy.Merge => "--merge",
            PullRequestMergeStrategy.Squash => "--squash",
            PullRequestMergeStrategy.Rebase => "--rebase",
            _ => "--merge"
        };

        var result = await processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = ghPath,
            WorkingDirectory = session.Git.WorktreePath,
            Arguments = ["pr", "merge", trackedPr.Url, flag, "--delete-branch=false"],
            Timeout = TimeSpan.FromSeconds(30),
            EnvironmentVariables = await BuildGhEnvironmentAsync()
        }, ct);

        if (!result.Success)
        {
            if (result.Stderr.Contains("already merged", StringComparison.OrdinalIgnoreCase))
            {
                var refreshed = await RefreshAsync(session, ct);
                return new PullRequestMergeResult(true, refreshed, "");
            }

            return new PullRequestMergeResult(false, trackedPr,
                string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr);
        }

        var pr = await RefreshAsync(session, ct) ?? trackedPr;
        FireHookSafe(HookEvent.OnPrMerge, pr);
        return new PullRequestMergeResult(true, pr, "");
    }

    public async Task<bool> IsGhAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var ghPath = await ResolveGhPathAsync();
            var result = await processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = ghPath,
                Arguments = ["auth", "status"],
                Timeout = TimeSpan.FromSeconds(10),
                EnvironmentVariables = await BuildGhEnvironmentAsync()
            }, ct);
            return result.Success;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "gh auth status 확인 실패");
            return false;
        }
    }

    private async Task<TrackedPullRequest?> QueryAsync(string workingDir, string urlOrBranch, CancellationToken ct)
    {
        var ghPath = await ResolveGhPathAsync();
        var result = await processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = ghPath,
            WorkingDirectory = workingDir,
            Arguments =
            [
                "pr", "view", urlOrBranch,
                "--json",
                "number,url,state,title,isDraft,mergeable,mergeStateStatus,reviewDecision,headRefName,baseRefName,mergedAt,mergeCommit,statusCheckRollup"
            ],
            Timeout = TimeSpan.FromSeconds(20),
            EnvironmentVariables = await BuildGhEnvironmentAsync()
        }, ct);

        if (!result.Success)
        {
            logger.LogDebug("gh pr view 실패: {Err}",
                string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Stdout);
            var root = doc.RootElement;
            var stateRaw = root.TryGetProperty("state", out var stateEl) ? stateEl.GetString() ?? "" : "";
            var mergeableRaw = root.TryGetProperty("mergeable", out var mergeableEl)
                ? mergeableEl.GetString() ?? ""
                : "";
            var mergeStateStatus = root.TryGetProperty("mergeStateStatus", out var mss)
                ? mss.GetString() ?? ""
                : "";

            return new TrackedPullRequest
            {
                Url = root.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? urlOrBranch : urlOrBranch,
                Number = root.TryGetProperty("number", out var numEl) && numEl.ValueKind == JsonValueKind.Number
                    ? numEl.GetInt32()
                    : null,
                Title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "",
                BaseBranch = root.TryGetProperty("baseRefName", out var baseEl) ? baseEl.GetString() ?? "" : "",
                HeadBranch = root.TryGetProperty("headRefName", out var headEl) ? headEl.GetString() ?? "" : "",
                State = ParseLifecycleState(stateRaw),
                IsDraft = root.TryGetProperty("isDraft", out var draftEl) &&
                          draftEl.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                          draftEl.GetBoolean(),
                IsMergeable = ParseMergeable(mergeableRaw, mergeStateStatus),
                IsMerged = stateRaw.Equals("MERGED", StringComparison.OrdinalIgnoreCase),
                MergeStateStatus = mergeStateStatus,
                ReviewDecision = root.TryGetProperty("reviewDecision", out var rd) ? rd.GetString() ?? "" : "",
                ChecksSummary = SummarizeChecks(root),
                MergedAtUtc = root.TryGetProperty("mergedAt", out var mergedAtEl) &&
                              mergedAtEl.ValueKind == JsonValueKind.String &&
                              DateTime.TryParse(mergedAtEl.GetString(), out var mergedAt)
                    ? mergedAt
                    : null,
                LastMergeCommitSha = root.TryGetProperty("mergeCommit", out var mc) &&
                                     mc.ValueKind == JsonValueKind.Object &&
                                     mc.TryGetProperty("oid", out var oid)
                    ? oid.GetString() ?? ""
                    : "",
                LastCheckedAtUtc = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "gh pr view JSON 파싱 실패");
            return null;
        }
    }

    private void FireHookSafe(HookEvent hookEvent, TrackedPullRequest pr)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await hooksEngine.FireAsync(hookEvent, new Dictionary<string, string>
                {
                    ["PR_NUMBER"] = pr.Number?.ToString() ?? "",
                    ["PR_URL"] = pr.Url
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Hook} 훅 실행 실패: PR #{Number}", hookEvent, pr.Number);
            }
        });
    }

    private static IEnumerable<string> ExtractCandidateUrls(ChatMessage assistantMessage)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in GitHubPrUrlRegex().Matches(assistantMessage.Text ?? ""))
            if (seen.Add(match.Value))
                yield return match.Value;

        foreach (var tool in assistantMessage.ToolCalls)
            foreach (Match match in GitHubPrUrlRegex().Matches(tool.Output ?? ""))
                if (seen.Add(match.Value))
                    yield return match.Value;
    }

    private async Task<string> ResolveGhPathAsync()
    {
        var configuredPath = appSettings.CurrentValue.GhPath;
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        if (_resolvedGhPath != null && DateTime.UtcNow - _ghPathResolvedAt < GhPathCacheTtl)
            return _resolvedGhPath;

        await _ghPathLock.WaitAsync();
        try
        {
            if (_resolvedGhPath != null && DateTime.UtcNow - _ghPathResolvedAt < GhPathCacheTtl)
                return _resolvedGhPath;

            _resolvedGhPath = await shellService.WhichAsync("gh") ?? "gh";
            _ghPathResolvedAt = DateTime.UtcNow;
            return _resolvedGhPath;
        }
        finally
        {
            _ghPathLock.Release();
        }
    }

    private async Task<Dictionary<string, string>> BuildGhEnvironmentAsync()
    {
        var env = new Dictionary<string, string>
        {
            ["GH_PROMPT_DISABLED"] = "1",
            ["NO_COLOR"] = "1",
            ["CLICOLOR"] = "0"
        };

        var loginPath = await shellService.GetLoginShellPathAsync();
        if (!string.IsNullOrWhiteSpace(loginPath))
            env["PATH"] = loginPath;

        return env;
    }

    private static PullRequestLifecycleState ParseLifecycleState(string state) =>
        state.ToUpperInvariant() switch
        {
            "OPEN" => PullRequestLifecycleState.Open,
            "CLOSED" => PullRequestLifecycleState.Closed,
            "MERGED" => PullRequestLifecycleState.Merged,
            _ => PullRequestLifecycleState.Unknown
        };

    private static bool? ParseMergeable(string mergeableRaw, string mergeStateStatus)
    {
        if (mergeableRaw.Equals("MERGEABLE", StringComparison.OrdinalIgnoreCase))
            return true;
        if (mergeableRaw.Equals("CONFLICTING", StringComparison.OrdinalIgnoreCase))
            return false;
        if (mergeStateStatus.Equals("CLEAN", StringComparison.OrdinalIgnoreCase))
            return true;
        if (mergeStateStatus.Equals("DIRTY", StringComparison.OrdinalIgnoreCase) ||
            mergeStateStatus.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    private static string SummarizeChecks(JsonElement root)
    {
        if (!root.TryGetProperty("statusCheckRollup", out var rollup) || rollup.ValueKind != JsonValueKind.Array)
            return "";

        var total = 0;
        var failed = 0;
        var pending = 0;

        foreach (var item in rollup.EnumerateArray())
        {
            total++;
            var conclusion = item.TryGetProperty("conclusion", out var concEl) ? concEl.GetString() ?? "" : "";
            var status = item.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "" : "";

            if (!string.IsNullOrWhiteSpace(conclusion) &&
                !conclusion.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
                && !conclusion.Equals("NEUTRAL", StringComparison.OrdinalIgnoreCase)
                && !conclusion.Equals("SKIPPED", StringComparison.OrdinalIgnoreCase))
                failed++;
            else if (status.Equals("IN_PROGRESS", StringComparison.OrdinalIgnoreCase) ||
                     status.Equals("QUEUED", StringComparison.OrdinalIgnoreCase) ||
                     string.IsNullOrWhiteSpace(conclusion))
                pending++;
        }

        if (total == 0)
            return "";
        if (failed > 0)
            return $"checks 실패 {failed}/{total}";
        if (pending > 0)
            return $"checks 대기 {pending}/{total}";
        return $"checks 통과 {total}";
    }

    [GeneratedRegex(@"https://github\.com/[^/\s]+/[^/\s]+/pull/\d+", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubPrUrlRegex();
}
