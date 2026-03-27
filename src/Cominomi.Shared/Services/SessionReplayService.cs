using System.Text.Json;
using System.Text.Json.Nodes;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SessionReplayService : ISessionReplayService
{
    private static readonly string ClaudeProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    private readonly ILogger<SessionReplayService> _logger;

    public SessionReplayService(ILogger<SessionReplayService> logger)
    {
        _logger = logger;
    }

    public Task<List<SessionReplaySummary>> ListSessionsAsync()
    {
        var sessions = new List<SessionReplaySummary>();

        if (!Directory.Exists(ClaudeProjectsDir))
            return Task.FromResult(sessions);

        foreach (var projectDir in Directory.GetDirectories(ClaudeProjectsDir))
        {
            var sessionsDir = Path.Combine(projectDir, "sessions");
            if (!Directory.Exists(sessionsDir)) continue;

            foreach (var file in Directory.GetFiles(sessionsDir, "*.jsonl"))
            {
                try
                {
                    var summary = ScanSessionFile(file, projectDir);
                    if (summary != null)
                        sessions.Add(summary);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error scanning session: {Path}", file);
                }
            }
        }

        return Task.FromResult(sessions.OrderByDescending(s => s.LastTimestamp).ToList());
    }

    public Task<List<SessionReplayEvent>> LoadEventsAsync(string filePath, int skip = 0, int take = 100)
    {
        var events = new List<SessionReplayEvent>();
        if (!File.Exists(filePath))
            return Task.FromResult(events);

        try
        {
            var lines = File.ReadLines(filePath).Skip(skip).Take(take);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var evt = ParseEvent(line);
                if (evt != null)
                    events.Add(evt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading session events: {Path}", filePath);
        }

        return Task.FromResult(events);
    }

    private static SessionReplaySummary? ScanSessionFile(string filePath, string projectDir)
    {
        var lines = File.ReadLines(filePath).Take(500).ToList();
        if (lines.Count == 0) return null;

        int messages = 0, tools = 0;
        string? firstMessage = null;
        DateTime? first = null, last = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var node = JsonNode.Parse(line);
                if (node == null) continue;

                var type = node["type"]?.GetValue<string>();
                var ts = node["timestamp"]?.GetValue<string>();
                if (ts != null && DateTime.TryParse(ts, out var dt))
                {
                    first ??= dt;
                    last = dt;
                }

                if (type is "human" or "user")
                {
                    messages++;
                    firstMessage ??= node["message"]?["content"]?.GetValue<string>()
                                     ?? node["content"]?.ToString();
                }
                else if (type is "assistant") messages++;
                else if (type is "tool_use") tools++;
            }
            catch { /* skip malformed lines */ }
        }

        return new SessionReplaySummary
        {
            Id = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            ProjectPath = Path.GetFileName(projectDir),
            EntryCount = lines.Count,
            MessageCount = messages,
            ToolCallCount = tools,
            FirstTimestamp = first,
            LastTimestamp = last,
            FirstMessage = firstMessage?.Length > 100 ? firstMessage[..100] + "..." : firstMessage
        };
    }

    private static SessionReplayEvent? ParseEvent(string line)
    {
        try
        {
            var node = JsonNode.Parse(line);
            if (node == null) return null;

            var type = node["type"]?.GetValue<string>() ?? "unknown";
            DateTime? ts = null;
            var tsStr = node["timestamp"]?.GetValue<string>();
            if (tsStr != null && DateTime.TryParse(tsStr, out var dt)) ts = dt;

            var content = type switch
            {
                "human" or "user" => node["message"]?["content"]?.GetValue<string>()
                                     ?? node["content"]?.ToString() ?? "",
                "assistant" => node["message"]?["content"]?.ToString()
                               ?? node["content"]?.ToString() ?? "",
                "tool_use" => node["name"]?.GetValue<string>() ?? "tool",
                "tool_result" => node["content"]?.ToString() ?? "",
                _ => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
            };

            return new SessionReplayEvent
            {
                Type = type,
                Timestamp = ts,
                Content = content.Length > 500 ? content[..500] + "..." : content,
                ToolName = type == "tool_use" ? node["name"]?.GetValue<string>() : null
            };
        }
        catch
        {
            return null;
        }
    }
}
