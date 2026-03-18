using System.Collections.Concurrent;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ActivityService : IActivityService
{
    private readonly ISessionService _sessionService;
    private readonly IGitService _gitService;
    private readonly ILogger<ActivityService> _logger;

    // Cache: avoid re-running git log on every tab switch → 10 sec TTL
    private readonly ConcurrentDictionary<string, (List<ActivityDateGroup> Groups, DateTime LoadedAt)> _activityCache = new();
    private static readonly TimeSpan ActivityCacheTtl = TimeSpan.FromSeconds(10);

    public ActivityService(ISessionService sessionService, IGitService gitService, ILogger<ActivityService> logger)
    {
        _sessionService = sessionService;
        _gitService = gitService;
        _logger = logger;
    }

    public async Task<List<ActivityDateGroup>> GetWorkspaceActivityAsync(string workspaceId, CancellationToken ct = default)
    {
        if (_activityCache.TryGetValue(workspaceId, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < ActivityCacheTtl)
        {
            return cached.Groups;
        }

        var sessions = await _sessionService.GetSessionsByWorkspaceAsync(workspaceId);
        var activeSessions = sessions.Where(s =>
            s.Status != SessionStatus.Pending &&
            !string.IsNullOrEmpty(s.Git.WorktreePath) &&
            !string.IsNullOrEmpty(s.Git.BaseBranch)).ToList();

        var allEntries = new List<ActivityEntry>();
        var tasks = activeSessions.Select(async session =>
        {
            try
            {
                var result = await _gitService.GetFormattedCommitLogAsync(
                    session.Git.WorktreePath, session.Git.BaseBranch, 50, ct);

                if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
                    return new List<ActivityEntry>();

                return result.Output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => ParseCommitLine(line, session))
                    .Where(e => e != null)
                    .Cast<ActivityEntry>()
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get commit log for session {SessionId}", session.Id);
                return new List<ActivityEntry>();
            }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var entries in results)
            allEntries.AddRange(entries);

        allEntries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

        var groups = GroupByDate(allEntries);
        _activityCache[workspaceId] = (groups, DateTime.UtcNow);
        return groups;
    }

    internal static ActivityEntry? ParseCommitLine(string line, Session session)
    {
        var parts = line.Split('\0', 5);
        if (parts.Length < 5) return null;

        if (!DateTime.TryParse(parts[3], out var timestamp))
            return null;

        return new ActivityEntry
        {
            CommitHash = parts[0],
            ShortHash = parts[1],
            Author = parts[2],
            Timestamp = timestamp.ToUniversalTime(),
            Message = parts[4],
            SessionId = session.Id,
            SessionTitle = session.Title,
            BranchName = session.Git.BranchName,
            SessionStatus = session.Status
        };
    }

    private static List<ActivityDateGroup> GroupByDate(List<ActivityEntry> entries)
    {
        var today = DateTime.UtcNow.Date;
        var yesterday = today.AddDays(-1);

        return entries
            .GroupBy(e => e.Timestamp.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new ActivityDateGroup
            {
                Label = g.Key == today ? "Today"
                    : g.Key == yesterday ? "Yesterday"
                    : g.Key.ToString("yyyy년 M월 d일"),
                Entries = g.ToList()
            })
            .ToList();
    }
}
