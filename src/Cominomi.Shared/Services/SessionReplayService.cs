using System.Text.Json;
using System.Text.Json.Nodes;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SessionReplayService(ILogger<SessionReplayService> logger) : ISessionReplayService
{
    private static readonly string ClaudeProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    private static readonly string TagsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "cominomi-session-tags.json");

    private static readonly string IndexFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "cominomi-session-index.json");

    private static readonly HashSet<string> NoiseEventTypes =
        ["file-history-snapshot", "progress", "last-prompt", "queue-operation"];

    private static readonly JsonSerializerOptions IndexJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private volatile SessionIndex? _index;
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    // ===== Session Index Management =====

    public async Task RefreshSessionIndexAsync(bool force = false)
    {
        await _indexLock.WaitAsync();
        try
        {
            await EnsureIndexLoadedAsync();
            var index = _index!;

            if (!Directory.Exists(ClaudeProjectsDir))
                return;

            var existingPaths = new HashSet<string>(index.Entries.Keys, StringComparer.OrdinalIgnoreCase);
            var currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var projectDir in Directory.GetDirectories(ClaudeProjectsDir))
            {
                string[] files;
                try { files = Directory.GetFiles(projectDir, "*.jsonl"); }
                catch { continue; }

                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length < 200) continue;
                        currentPaths.Add(file);

                        // Skip unchanged files unless force
                        if (!force && index.Entries.TryGetValue(file, out var existing)
                            && existing.FileSizeBytes == fi.Length
                            && existing.FileLastWriteUtc == fi.LastWriteTimeUtc)
                            continue;

                        var entry = QuickScan(file, fi);
                        if (entry != null)
                            index.Entries[file] = entry;
                    }
                    catch { /* skip inaccessible files */ }
                }
            }

            // Remove entries for deleted files
            foreach (var path in existingPaths)
            {
                if (!currentPaths.Contains(path))
                    index.Entries.Remove(path);
            }

            index.LastComputedDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            await SaveIndexAsync(index);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    public async Task<List<SessionReplaySummary>> SearchIndexAsync(string query, int maxResults = 20)
    {
        await EnsureIndexLoadedAsync();
        var index = _index!;

        if (string.IsNullOrWhiteSpace(query))
            return [];

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var scored = new List<(SessionIndexEntry entry, int score)>();

        foreach (var entry in index.Entries.Values)
        {
            int score = 0;
            foreach (var token in tokens)
            {
                if (entry.FirstMessage?.Contains(token, StringComparison.OrdinalIgnoreCase) == true)
                    score += 3;
                if (entry.ProjectPath.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 2;
                if (entry.Id.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 1;
            }
            if (score > 0)
                scored.Add((entry, score));
        }

        return scored
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.entry.FirstTimestamp ?? DateTime.MinValue)
            .Take(maxResults)
            .Select(x => IndexEntryToSummary(x.entry))
            .ToList();
    }

    // ===== List Sessions (paginated, sorted by session timestamp) =====

    public async Task<SessionListResult> ListSessionsAsync(int limit = 10, int offset = 0)
    {
        await EnsureIndexLoadedAsync();
        var index = _index!;

        var sorted = index.Entries.Values
            .OrderByDescending(e => e.FirstTimestamp ?? DateTime.MinValue)
            .ToList();

        var total = sorted.Count;
        var sessions = sorted
            .Skip(offset)
            .Take(limit)
            .Select(IndexEntryToSummary)
            .ToList();

        return new SessionListResult
        {
            Sessions = sessions,
            Total = total,
            HasMore = offset + limit < total
        };
    }

    // ===== Load Session Events (paginated, noise-filtered) =====

    public Task<SessionLoadResult> LoadSessionAsync(string filePath, int limit = 50, int offset = 0)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(filePath))
                return new SessionLoadResult();

            var events = new List<SessionReplayEvent>();
            int total = 0;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    JsonNode? node;
                    try { node = JsonNode.Parse(line); }
                    catch { continue; }
                    if (node == null) continue;

                    var type = node["type"]?.GetValue<string>() ?? "unknown";
                    if (NoiseEventTypes.Contains(type)) continue;

                    // Skip user messages that are just tool_result wrappers
                    if (type == "user" && IsToolResultOnlyUser(node)) continue;

                    if (total >= offset && events.Count < limit)
                    {
                        events.Add(ParseEvent(node, type));
                    }
                    total++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error loading session events: {Path}", filePath);
            }

            return new SessionLoadResult
            {
                Events = events,
                Total = total,
                HasMore = offset + limit < total
            };
        });
    }

    // ===== Search =====

    public Task<List<SessionSearchResult>> SearchAsync(string query, int maxResults = 20)
    {
        return Task.Run(() =>
        {
            var results = new List<SessionSearchResult>();
            if (!Directory.Exists(ClaudeProjectsDir) || string.IsNullOrWhiteSpace(query))
                return results;

            var q = query.ToLowerInvariant();

            foreach (var projectDir in Directory.GetDirectories(ClaudeProjectsDir))
            {
                if (results.Count >= maxResults) break;

                var projectHash = Path.GetFileName(projectDir);
                var projectPath = ProjectHashToPath(projectHash);

                string[] files;
                try { files = Directory.GetFiles(projectDir, "*.jsonl"); }
                catch { continue; }

                foreach (var file in files)
                {
                    if (results.Count >= maxResults) break;

                    var sessionId = Path.GetFileNameWithoutExtension(file);

                    try
                    {
                        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(fs);

                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            if (!line.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

                            JsonNode? node;
                            try { node = JsonNode.Parse(line); }
                            catch { continue; }
                            if (node == null) continue;

                            var type = node["type"]?.GetValue<string>() ?? "";
                            if (NoiseEventTypes.Contains(type)) continue;

                            var snippet = ExtractTextSnippet(node, q);
                            if (string.IsNullOrEmpty(snippet)) continue;

                            DateTime? ts = null;
                            var tsStr = node["timestamp"]?.GetValue<string>();
                            if (tsStr != null && DateTime.TryParse(tsStr, out var dt)) ts = dt;

                            results.Add(new SessionSearchResult
                            {
                                SessionId = sessionId,
                                ProjectPath = projectPath,
                                FilePath = file,
                                Snippet = snippet,
                                Timestamp = ts,
                                EventType = type
                            });

                            if (results.Count >= maxResults) break;
                        }
                    }
                    catch { /* skip unreadable files */ }
                }
            }

            return results;
        });
    }

    // ===== Live Detection =====

    public Task<List<LiveSessionInfo>> DetectLiveSessionsAsync()
    {
        return Task.Run(() =>
        {
            var live = new List<LiveSessionInfo>();
            if (!Directory.Exists(ClaudeProjectsDir))
                return live;

            var now = DateTimeOffset.UtcNow;

            foreach (var projectDir in Directory.GetDirectories(ClaudeProjectsDir))
            {
                var projectHash = Path.GetFileName(projectDir);
                var projectPath = ProjectHashToPath(projectHash);

                string[] files;
                try { files = Directory.GetFiles(projectDir, "*.jsonl"); }
                catch { continue; }

                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        var ago = (long)(now - fi.LastWriteTimeUtc).TotalSeconds;
                        if (ago < 300) // 5 minutes
                        {
                            live.Add(new LiveSessionInfo
                            {
                                FilePath = file,
                                ProjectPath = projectPath,
                                ModifiedSecondsAgo = ago
                            });
                        }
                    }
                    catch { /* skip */ }
                }
            }

            live.Sort((a, b) => a.ModifiedSecondsAgo.CompareTo(b.ModifiedSecondsAgo));
            return live;
        });
    }

    // ===== Tags =====

    public Task<SessionTagsData> GetTagsAsync()
    {
        return Task.Run(() =>
        {
            if (!File.Exists(TagsFilePath))
                return new SessionTagsData();

            try
            {
                var json = File.ReadAllText(TagsFilePath);
                return JsonSerializer.Deserialize<SessionTagsData>(json) ?? new SessionTagsData();
            }
            catch
            {
                return new SessionTagsData();
            }
        });
    }

    public async Task SetTagAsync(string sessionId, List<string> tags, string? note = null)
    {
        var data = await GetTagsAsync();

        if (tags.Count > 0)
            data.Tags[sessionId] = tags;
        else
            data.Tags.Remove(sessionId);

        if (note != null)
        {
            if (!string.IsNullOrEmpty(note))
                data.Notes[sessionId] = note;
            else
                data.Notes.Remove(sessionId);
        }

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(TagsFilePath, json);
    }

    // ===== Export to Markdown =====

    public Task<string> ExportToMarkdownAsync(string filePath)
    {
        return Task.Run(() =>
        {
            var md = "# Session Replay\n\n";
            int userCount = 0, toolCount = 0;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    JsonNode? node;
                    try { node = JsonNode.Parse(line); }
                    catch { continue; }
                    if (node == null) continue;

                    var type = node["type"]?.GetValue<string>() ?? "";
                    if (NoiseEventTypes.Contains(type)) continue;

                    var msg = node["message"] ?? node;
                    var content = msg["content"];

                    if (type == "user")
                    {
                        var text = ExtractTextContent(content);
                        if (string.IsNullOrEmpty(text)) continue;
                        userCount++;
                        md += $"## User\n\n{text}\n\n";
                    }
                    else if (type == "assistant")
                    {
                        if (content is JsonArray arr)
                        {
                            foreach (var item in arr)
                            {
                                var itemType = item?["type"]?.GetValue<string>() ?? "";
                                if (itemType == "text")
                                {
                                    var text = item?["text"]?.GetValue<string>() ?? "";
                                    if (!string.IsNullOrEmpty(text))
                                        md += $"## Claude\n\n{text}\n\n";
                                }
                                else if (itemType == "tool_use")
                                {
                                    var name = item?["name"]?.GetValue<string>() ?? "tool";
                                    toolCount++;
                                    md += $"**Tool: {name}**\n\n";
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error exporting session: {Path}", filePath);
            }

            md += $"\n---\n*{userCount} messages, {toolCount} tool calls*\n";
            return md;
        });
    }

    // ===== Quick Scan (reads first N lines, estimates the rest from file size) =====

    private const int SampleLineCount = 30;

    private static SessionIndexEntry? QuickScan(string filePath, FileInfo fi)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);

            string? firstTimestamp = null;
            string? lastTimestamp = null;
            string? firstMessage = null;
            int parsedCount = 0, userCount = 0, toolCount = 0;
            long totalBytesRead = 0;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                totalBytesRead += System.Text.Encoding.UTF8.GetByteCount(line) + 1; // +1 for newline

                JsonNode? node;
                try { node = JsonNode.Parse(line); }
                catch { continue; }
                if (node == null) continue;

                var type = node["type"]?.GetValue<string>() ?? "";
                if (NoiseEventTypes.Contains(type)) continue;

                parsedCount++;

                var ts = node["timestamp"]?.GetValue<string>();
                if (ts != null)
                {
                    firstTimestamp ??= ts;
                    lastTimestamp = ts;
                }

                if (type == "user")
                {
                    if (IsToolResultOnlyUser(node)) continue;
                    userCount++;
                    if (firstMessage == null)
                    {
                        var msg = node["message"];
                        var content = msg?["content"];
                        var text = ExtractTextContent(content);
                        if (!string.IsNullOrEmpty(text))
                            firstMessage = text.Length > 100 ? text[..100] : text;
                    }
                }
                else if (type == "assistant")
                {
                    var msg = node["message"];
                    var content = msg?["content"];
                    if (content is JsonArray arr)
                    {
                        foreach (var item in arr)
                        {
                            if (item?["type"]?.GetValue<string>() == "tool_use")
                                toolCount++;
                        }
                    }
                }

                // After SampleLineCount parsed entries, estimate the rest from file size
                if (parsedCount >= SampleLineCount)
                    break;
            }

            if (parsedCount < 3) return null;

            // Estimate total entries from file size and average line bytes
            int estimatedEntries;
            int estimatedUsers;
            int estimatedTools;

            if (reader.EndOfStream || parsedCount < SampleLineCount)
            {
                // File fully read within sample — exact counts
                estimatedEntries = parsedCount;
                estimatedUsers = userCount;
                estimatedTools = toolCount;
            }
            else
            {
                var avgLineBytes = totalBytesRead / Math.Max(parsedCount, 1);
                estimatedEntries = (int)(fi.Length / Math.Max(avgLineBytes, 1));
                var scale = (float)estimatedEntries / parsedCount;
                estimatedUsers = (int)(userCount * scale);
                estimatedTools = (int)(toolCount * scale);
            }

            var projectDir = Path.GetDirectoryName(filePath) ?? "";
            var projectHash = Path.GetFileName(projectDir);

            DateTime? firstTs = null, lastTs = null;
            if (firstTimestamp != null && DateTime.TryParse(firstTimestamp, out var dt1)) firstTs = dt1;
            if (lastTimestamp != null && DateTime.TryParse(lastTimestamp, out var dt2)) lastTs = dt2;

            return new SessionIndexEntry
            {
                Id = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                ProjectHash = projectHash,
                ProjectPath = ProjectHashToPath(projectHash),
                EntryCount = estimatedEntries,
                UserMessageCount = estimatedUsers,
                ToolCallCount = estimatedTools,
                FirstTimestamp = firstTs,
                LastTimestamp = lastTs,
                FirstMessage = firstMessage,
                FileSizeBytes = fi.Length,
                FileLastWriteUtc = fi.LastWriteTimeUtc,
                IsEstimated = parsedCount >= SampleLineCount && !reader.EndOfStream,
            };
        }
        catch
        {
            return null;
        }
    }

    // ===== Aggregated Index Stats (lightweight, no list allocation) =====

    public async Task<SessionIndexStats> GetIndexStatsAsync()
    {
        await EnsureIndexLoadedAsync();
        var index = _index!;

        var dailyMap = new Dictionary<string, DailyActivityEntry>();
        var projectPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hourCounts = new int[24];
        int nightSessions = 0, morningSessions = 0;
        long longestMs = 0;

        foreach (var e in index.Entries.Values)
        {
            var dateKey = e.FirstTimestamp?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";
            if (string.IsNullOrEmpty(dateKey)) continue;

            if (!dailyMap.TryGetValue(dateKey, out var entry))
            {
                entry = new DailyActivityEntry { Date = dateKey };
                dailyMap[dateKey] = entry;
            }
            entry.MessageCount += e.UserMessageCount;
            entry.SessionCount++;
            entry.ToolCallCount += e.ToolCallCount;

            if (!string.IsNullOrEmpty(e.ProjectPath))
                projectPaths.Add(e.ProjectPath);

            if (e.FirstTimestamp.HasValue)
            {
                var hour = e.FirstTimestamp.Value.ToLocalTime().Hour;
                hourCounts[hour]++;
                if (hour >= 22 || hour < 4) nightSessions++;
                if (hour >= 5 && hour < 9) morningSessions++;
            }

            if (e.FirstTimestamp.HasValue && e.LastTimestamp.HasValue)
            {
                var ms = (long)(e.LastTimestamp.Value - e.FirstTimestamp.Value).TotalMilliseconds;
                if (ms > longestMs) longestMs = ms;
            }
        }

        var dailyActivity = dailyMap.Values.OrderBy(d => d.Date).ToList();

        return new SessionIndexStats(
            TotalSessions: index.Entries.Count,
            TotalMessages: index.Entries.Values.Sum(e => e.UserMessageCount),
            TotalToolCalls: index.Entries.Values.Sum(e => e.ToolCallCount),
            DaysActive: dailyMap.Count,
            TotalProjects: projectPaths.Count,
            DailyActivity: dailyActivity,
            HourCounts: hourCounts,
            NightSessions: nightSessions,
            MorningSessions: morningSessions,
            LongestSessionMs: longestMs);
    }

    // ===== Index Persistence =====

    private async Task EnsureIndexLoadedAsync()
    {
        if (_index != null) return;

        try
        {
            if (File.Exists(IndexFilePath))
            {
                var json = await File.ReadAllTextAsync(IndexFilePath);
                _index = JsonSerializer.Deserialize<SessionIndex>(json, IndexJsonOptions);
            }
        }
        catch
        {
            // Corrupted index — start fresh
        }

        _index ??= new SessionIndex();
    }

    private static async Task SaveIndexAsync(SessionIndex index)
    {
        try
        {
            var json = JsonSerializer.Serialize(index, IndexJsonOptions);
            await File.WriteAllTextAsync(IndexFilePath, json);
        }
        catch { /* write failure is non-fatal */ }
    }

    private static SessionReplaySummary IndexEntryToSummary(SessionIndexEntry e)
    {
        return new SessionReplaySummary
        {
            Id = e.Id,
            FilePath = e.FilePath,
            ProjectHash = e.ProjectHash,
            ProjectPath = e.ProjectPath,
            EntryCount = e.EntryCount,
            MessageCount = e.UserMessageCount,
            ToolCallCount = e.ToolCallCount,
            FirstTimestamp = e.FirstTimestamp,
            LastTimestamp = e.LastTimestamp,
            FirstMessage = e.FirstMessage,
            ModifiedAtUnix = e.FileLastWriteUtc.Subtract(DateTime.UnixEpoch).TotalSeconds,
        };
    }

    // ===== Event Parsing =====

    private static SessionReplayEvent ParseEvent(JsonNode node, string type)
    {
        DateTime? ts = null;
        var tsStr = node["timestamp"]?.GetValue<string>();
        if (tsStr != null && DateTime.TryParse(tsStr, out var dt)) ts = dt;

        var msg = node["message"] ?? node;
        var content = msg["content"];

        string displayContent;
        List<ToolCallInfo>? toolCalls = null;
        string? toolName = null;
        bool isError = false;

        switch (type)
        {
            case "human" or "user":
                displayContent = ExtractTextContent(content) ?? "";
                break;

            case "assistant":
                var textParts = new List<string>();
                toolCalls = [];

                if (content is JsonArray arr)
                {
                    foreach (var item in arr)
                    {
                        var itemType = item?["type"]?.GetValue<string>() ?? "";
                        if (itemType == "text")
                        {
                            var text = item?["text"]?.GetValue<string>() ?? "";
                            if (!string.IsNullOrEmpty(text))
                                textParts.Add(text);
                        }
                        else if (itemType == "tool_use")
                        {
                            var name = item?["name"]?.GetValue<string>() ?? "tool";
                            var input = item?["input"]?.ToJsonString() ?? "{}";
                            toolCalls.Add(new ToolCallInfo
                            {
                                Name = name,
                                InputPreview = input.Length > 200 ? input[..200] : input
                            });
                        }
                    }
                }

                displayContent = string.Join("\n\n", textParts);
                if (toolCalls.Count == 0) toolCalls = null;
                break;

            case "tool_result":
                displayContent = ExtractTextContent(content) ?? "";
                if (displayContent.Length > 500)
                    displayContent = displayContent[..500] + "...";
                isError = node["is_error"]?.GetValue<bool>() == true ||
                          msg["is_error"]?.GetValue<bool>() == true;
                break;

            default:
                displayContent = "";
                break;
        }

        return new SessionReplayEvent
        {
            Type = type,
            Timestamp = ts,
            Content = displayContent,
            ToolName = toolName,
            ToolCalls = toolCalls,
            IsError = isError
        };
    }

    // ===== Helpers =====

    private static bool IsToolResultOnlyUser(JsonNode node)
    {
        var msg = node["message"];
        var content = msg?["content"];
        if (content is not JsonArray arr) return false;
        return !arr.Any(item => item?["type"]?.GetValue<string>() == "text");
    }

    private static string? ExtractTextContent(JsonNode? content)
    {
        if (content == null) return null;

        // String content
        if (content.GetValueKind() == JsonValueKind.String)
            return content.GetValue<string>();

        // Array content — extract text items
        if (content is JsonArray arr)
        {
            var texts = arr
                .Where(item => item?["type"]?.GetValue<string>() == "text")
                .Select(item => item?["text"]?.GetValue<string>())
                .Where(t => t != null)
                .ToList();
            return texts.Count > 0 ? string.Join("\n", texts) : null;
        }

        return content.ToString();
    }

    private static string ExtractTextSnippet(JsonNode node, string query)
    {
        var msg = node["message"] ?? node;
        var content = msg["content"];
        var text = ExtractTextContent(content);

        if (string.IsNullOrEmpty(text)) return "";

        var lower = text.ToLowerInvariant();
        var pos = lower.IndexOf(query, StringComparison.Ordinal);
        if (pos >= 0)
        {
            var start = Math.Max(0, pos - 40);
            var end = Math.Min(text.Length, pos + query.Length + 40);
            var snippet = text[start..end];
            if (start > 0) snippet = "..." + snippet;
            if (end < text.Length) snippet += "...";
            return snippet;
        }

        return text.Length > 80 ? text[..80] : text;
    }

    // ===== Project Hash → Path Decoding =====

    internal static string ProjectHashToPath(string hash)
    {
        // Naive: replace all '-' with '/'
        var naive = hash.Replace('-', '/');
        if (Directory.Exists(naive))
            return naive;

        // Smart: try to find a real path by grouping segments
        var segments = hash.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var resolved = ResolveSegments(segments, 0, "/");
        return resolved ?? naive;
    }

    private static string? ResolveSegments(string[] segments, int idx, string current)
    {
        if (idx >= segments.Length)
            return Directory.Exists(current) ? current : null;

        // Try joining segments with '-' (longer matches first to prefer real dir names)
        for (int end = segments.Length; end > idx; end--)
        {
            var joined = string.Join("-", segments[idx..end]);
            var candidate = current == "/"
                ? $"/{joined}"
                : $"{current}/{joined}";

            if (Directory.Exists(candidate))
            {
                var result = ResolveSegments(segments, end, candidate);
                if (result != null) return result;
            }
        }

        return null;
    }
}
