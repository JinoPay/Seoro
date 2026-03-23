using System.Collections.Concurrent;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ActivityService : IActivityService
{
    private readonly ISessionService _sessionService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ILogger<ActivityService> _logger;

    private readonly ConcurrentDictionary<string, (ActionTimelineResult Result, DateTime LoadedAt)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

    public ActivityService(ISessionService sessionService, IWorkspaceService workspaceService, ILogger<ActivityService> logger)
    {
        _sessionService = sessionService;
        _workspaceService = workspaceService;
        _logger = logger;
    }

    public async Task<ActionTimelineResult> GetActionTimelineAsync(ActionTimelineFilter filter, CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(filter);
        if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.LoadedAt < CacheTtl)
            return cached.Result;

        var cutoff = filter.DaysBack > 0 ? DateTime.UtcNow.AddDays(-filter.DaysBack) : DateTime.MinValue;

        // Phase 1: Session metadata scan (cheap, in-memory cache)
        var sessions = await _sessionService.GetSessionsAsync();
        var candidates = sessions
            .Where(s => s.UpdatedAt >= cutoff)
            .Where(s => filter.WorkspaceId == null || s.WorkspaceId == filter.WorkspaceId)
            .Where(s => filter.SessionId == null || s.Id == filter.SessionId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToList();

        // Build workspace name lookup
        var workspaces = await _workspaceService.GetWorkspacesAsync();
        var wsNameMap = workspaces.ToDictionary(w => w.Id, w => w.Name);

        // Phase 2: Parallel message extraction with concurrency limit
        var allEntries = new ConcurrentBag<ActionTimelineEntry>();
        var semaphore = new SemaphoreSlim(4);

        await Task.WhenAll(candidates.Select(async session =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var loaded = await _sessionService.LoadSessionAsync(session.Id);
                if (loaded == null) return;

                var wsName = wsNameMap.GetValueOrDefault(session.WorkspaceId, "");

                foreach (var msg in loaded.Messages)
                {
                    if (msg.Role != MessageRole.Assistant) continue;

                    foreach (var part in msg.Parts)
                    {
                        if (part.Type != ContentPartType.ToolCall || part.ToolCall == null) continue;

                        var tc = part.ToolCall;
                        var timestamp = msg.Timestamp;

                        if (timestamp < cutoff) continue;
                        if (filter.Before.HasValue && timestamp >= filter.Before.Value) continue;

                        allEntries.Add(new ActionTimelineEntry
                        {
                            ToolCallId = tc.Id,
                            ToolName = ToolDisplayHelper.NormalizeToolName(tc.Name),
                            HeaderLabel = ToolDisplayHelper.GetHeaderLabel(tc),
                            ResultHint = ToolDisplayHelper.GetCompactResult(tc),
                            IsError = tc.IsError,
                            Timestamp = timestamp,
                            SessionId = session.Id,
                            SessionTitle = session.Title,
                            WorkspaceId = session.WorkspaceId,
                            WorkspaceName = wsName
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract actions for session {SessionId}", session.Id);
            }
            finally
            {
                semaphore.Release();
            }
        }));

        // Phase 3: Sort, limit, group
        var sorted = allEntries.OrderByDescending(e => e.Timestamp).ToList();
        var hasMore = sorted.Count > filter.MaxEntries;
        var limited = sorted.Take(filter.MaxEntries).ToList();

        var result = new ActionTimelineResult
        {
            Groups = GroupByDate(limited),
            TotalEntriesLoaded = limited.Count,
            OldestTimestamp = limited.Count > 0 ? limited[^1].Timestamp : null,
            HasMore = hasMore
        };

        _cache[cacheKey] = (result, DateTime.UtcNow);
        return result;
    }

    private static List<ActionDateGroup> GroupByDate(List<ActionTimelineEntry> entries)
    {
        var today = DateTime.UtcNow.Date;
        var yesterday = today.AddDays(-1);

        return entries
            .GroupBy(e => e.Timestamp.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new ActionDateGroup
            {
                Label = g.Key == today ? "Today"
                    : g.Key == yesterday ? "Yesterday"
                    : g.Key.ToString("yyyy년 M월 d일"),
                Entries = g.ToList()
            })
            .ToList();
    }

    private static string BuildCacheKey(ActionTimelineFilter filter) =>
        $"{filter.WorkspaceId ?? "all"}:{filter.SessionId ?? "all"}:{filter.DaysBack}:{filter.MaxEntries}:{filter.Before?.Ticks}";
}
