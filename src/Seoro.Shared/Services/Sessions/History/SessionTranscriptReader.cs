using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Sessions.History;

/// <summary>
///     단일 CLI 네이티브 세션 파일을 읽어 리플레이용 이벤트 목록으로 파싱하거나
///     마크다운으로 내보낸다. (인덱스가 아니라 개별 파일 전체를 해석)
/// </summary>
public interface ISessionTranscriptReader
{
    Task<SessionLoadResult> LoadSessionAsync(string filePath, int limit = 50, int offset = 0);
    Task<string> ExportToMarkdownAsync(string filePath);
}

public class SessionTranscriptReader(ILogger<SessionTranscriptReader> logger) : ISessionTranscriptReader
{
    public Task<SessionLoadResult> LoadSessionAsync(string filePath, int limit = 50, int offset = 0)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(filePath))
                return new SessionLoadResult();

            var events = new List<SessionReplayEvent>();
            var total = 0;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    JsonNode? node;
                    try
                    {
                        node = JsonNode.Parse(line);
                    }
                    catch
                    {
                        continue;
                    }

                    if (node == null) continue;

                    var type = node["type"]?.GetValue<string>() ?? "unknown";
                    if (ReplayJsonHelpers.NoiseEventTypes.Contains(type)) continue;

                    // Skip user messages that are just tool_result wrappers
                    if (type == "user" && ReplayJsonHelpers.IsToolResultOnlyUser(node)) continue;

                    if (total >= offset && events.Count < limit) events.Add(ParseEvent(node, type));
                    total++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "세션 이벤트 로드 오류: {Path}", filePath);
            }

            return new SessionLoadResult
            {
                Events = events,
                Total = total,
                HasMore = offset + limit < total
            };
        });
    }

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
                    try
                    {
                        node = JsonNode.Parse(line);
                    }
                    catch
                    {
                        continue;
                    }

                    if (node == null) continue;

                    var type = node["type"]?.GetValue<string>() ?? "";
                    if (ReplayJsonHelpers.NoiseEventTypes.Contains(type)) continue;

                    var msg = node["message"] ?? node;
                    var content = msg["content"];

                    if (type == "user")
                    {
                        var text = ReplayJsonHelpers.ExtractTextContent(content);
                        if (string.IsNullOrEmpty(text)) continue;
                        userCount++;
                        md += $"## User\n\n{text}\n\n";
                    }
                    else if (type == "assistant")
                    {
                        if (content is JsonArray arr)
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
            catch (Exception ex)
            {
                logger.LogWarning(ex, "세션 내보내기 오류: {Path}", filePath);
            }

            md += $"\n---\n*{userCount} messages, {toolCount} tool calls*\n";
            return md;
        });
    }

    private static SessionReplayEvent ParseEvent(JsonNode node, string type)
    {
        var ts = ReplayJsonHelpers.ParseTimestampNode(node["timestamp"]);

        var msg = node["message"] ?? node;
        var content = msg["content"];

        string displayContent;
        List<ToolCallInfo>? toolCalls = null;
        string? toolName = null;
        var isError = false;

        switch (type)
        {
            case "human" or "user":
                displayContent = ReplayJsonHelpers.ExtractTextContent(content) ?? "";
                break;

            case "assistant":
                var textParts = new List<string>();
                toolCalls = [];

                if (content is JsonArray arr)
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

                displayContent = string.Join("\n\n", textParts);
                if (toolCalls.Count == 0) toolCalls = null;
                break;

            case "tool_result":
                displayContent = ReplayJsonHelpers.ExtractTextContent(content) ?? "";
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
}
