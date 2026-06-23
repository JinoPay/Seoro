using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seoro.Shared.Services.Sessions.History;

/// <summary>
///     CLI 네이티브 jsonl(`~/.claude/projects/*.jsonl`) 한 줄(이벤트)을 해석하는
///     순수 헬퍼. 인덱싱·검색·트랜스크립트 읽기가 공유한다.
/// </summary>
internal static class ReplayJsonHelpers
{
    public static readonly HashSet<string> NoiseEventTypes =
        ["file-history-snapshot", "progress", "last-prompt", "queue-operation"];

    /// <summary>
    ///     "timestamp" 필드가 ISO 8601 문자열 또는 Unix 밀리초 숫자일 수 있어
    ///     두 형식을 모두 안전하게 DateTime으로 변환한다.
    /// </summary>
    public static DateTime? ParseTimestampNode(JsonNode? tsNode)
    {
        if (tsNode == null) return null;
        try
        {
            var kind = tsNode.GetValueKind();
            if (kind == JsonValueKind.String)
            {
                var str = tsNode.GetValue<string>();
                if (str != null && DateTimeOffset.TryParse(str, out var dto))
                    return dto.UtcDateTime;
            }
            else if (kind == JsonValueKind.Number)
            {
                var ms = (long)tsNode.GetValue<double>();
                if (ms > 0)
                    return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            }
        }
        catch
        {
            // ignore — return null for unexpected node state
        }

        return null;
    }

    public static bool IsToolResultOnlyUser(JsonNode node)
    {
        var msg = node["message"];
        var content = msg?["content"];
        if (content is not JsonArray arr) return false;
        return !arr.Any(item => item?["type"]?.GetValue<string>() == "text");
    }

    public static string? ExtractTextContent(JsonNode? content)
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

    public static string ExtractTextSnippet(JsonNode node, string query)
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
}
