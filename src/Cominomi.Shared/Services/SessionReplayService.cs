using System.Text.Json;
using System.Text.Json.Nodes;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SessionReplayService : ISessionReplayService
{
    private static readonly string ClaudeProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    private static readonly string TagsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "cominomi-session-tags.json");

    private static readonly HashSet<string> NoiseEventTypes =
        ["file-history-snapshot", "progress", "last-prompt", "queue-operation"];

    private readonly ILogger<SessionReplayService> _logger;

    public SessionReplayService(ILogger<SessionReplayService> logger)
    {
        _logger = logger;
    }

    // ===== List Sessions (paginated, sorted by modification time) =====

    public Task<SessionListResult> ListSessionsAsync(int limit = 10, int offset = 0)
    {
        return Task.Run(() =>
        {
            if (!Directory.Exists(ClaudeProjectsDir))
                return new SessionListResult();

            // Collect all .jsonl file paths (fast — just readdir + metadata)
            var jsonlPaths = new List<(string Path, double ModifiedAt)>();

            foreach (var projectDir in Directory.GetDirectories(ClaudeProjectsDir))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(projectDir, "*.jsonl"))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Length < 200) continue; // skip tiny files
                            jsonlPaths.Add((file, fi.LastWriteTimeUtc
                                .Subtract(DateTime.UnixEpoch).TotalSeconds));
                        }
                        catch { /* skip inaccessible files */ }
                    }
                }
                catch { /* skip inaccessible directories */ }
            }

            // Sort by modified time (newest first)
            jsonlPaths.Sort((a, b) => b.ModifiedAt.CompareTo(a.ModifiedAt));

            var total = jsonlPaths.Count;

            // Only scan the paginated subset
            var sessions = jsonlPaths
                .Skip(offset)
                .Take(limit)
                .Select(p => QuickScan(p.Path, p.ModifiedAt))
                .Where(s => s != null)
                .Select(s => s!)
                .ToList();

            return new SessionListResult
            {
                Sessions = sessions,
                Total = total,
                HasMore = offset + limit < total
            };
        });
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
                _logger.LogWarning(ex, "Error loading session events: {Path}", filePath);
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
                _logger.LogWarning(ex, "Error exporting session: {Path}", filePath);
            }

            md += $"\n---\n*{userCount} messages, {toolCount} tool calls*\n";
            return md;
        });
    }

    // ===== Quick Scan (first 30 lines + file size estimation) =====

    private static SessionReplaySummary? QuickScan(string filePath, double modifiedAt)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Length < 200) return null;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);

            string? firstTimestamp = null;
            string? lastTimestamp = null;
            string? firstMessage = null;
            int userCount = 0, toolCount = 0, lineCount = 0;

            for (int i = 0; i < 30; i++)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                lineCount++;

                JsonNode? node;
                try { node = JsonNode.Parse(line); }
                catch { continue; }
                if (node == null) continue;

                var type = node["type"]?.GetValue<string>() ?? "";

                var ts = node["timestamp"]?.GetValue<string>();
                if (ts != null)
                {
                    firstTimestamp ??= ts;
                    lastTimestamp = ts;
                }

                if (type == "user")
                {
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
            }

            if (lineCount < 3) return null;

            // Estimate total entries from file size
            var avgLineBytes = fi.Length / Math.Max(lineCount, 1);
            var estimatedEntries = (int)(fi.Length / Math.Max(avgLineBytes, 1));
            var scale = (float)estimatedEntries / lineCount;

            var projectDir = Path.GetDirectoryName(filePath) ?? "";
            var projectHash = Path.GetFileName(projectDir);

            DateTime? firstTs = null, lastTs = null;
            if (firstTimestamp != null && DateTime.TryParse(firstTimestamp, out var dt1)) firstTs = dt1;
            if (lastTimestamp != null && DateTime.TryParse(lastTimestamp, out var dt2)) lastTs = dt2;

            return new SessionReplaySummary
            {
                Id = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                ProjectHash = projectHash,
                ProjectPath = ProjectHashToPath(projectHash),
                EntryCount = estimatedEntries,
                MessageCount = (int)(userCount * scale),
                ToolCallCount = (int)(toolCount * scale),
                FirstTimestamp = firstTs,
                LastTimestamp = lastTs,
                FirstMessage = firstMessage,
                ModifiedAtUnix = modifiedAt
            };
        }
        catch
        {
            return null;
        }
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
