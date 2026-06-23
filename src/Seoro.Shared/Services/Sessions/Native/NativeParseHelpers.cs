using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seoro.Shared.Services.Sessions.Native;

/// <summary>
///     CLI 네이티브 세션 jsonl 파싱용 공유 헬퍼.
///     Claude(<see cref="ClaudeNativeParser" />)와 Codex(<see cref="CodexRolloutParser" />) 파서가 공통으로 사용합니다.
/// </summary>
internal static class NativeParseHelpers
{
    /// <summary>
    ///     "timestamp" 노드를 DateTime으로 안전하게 변환합니다.
    ///     ISO 8601 문자열 또는 Unix 밀리초 숫자 모두 지원합니다.
    ///     (SessionReplayService / StatsCacheService와 동일한 처리.)
    /// </summary>
    public static DateTime? ParseTimestamp(JsonNode? tsNode)
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

    /// <summary>
    ///     content 노드에서 사람이 읽을 텍스트를 추출합니다.
    ///     문자열이면 그대로, 배열이면 type=="text"(또는 input/output_text) 항목을 이어붙입니다.
    /// </summary>
    public static string ExtractText(JsonNode? content)
    {
        if (content == null) return string.Empty;

        if (content.GetValueKind() == JsonValueKind.String)
            return content.GetValue<string>() ?? string.Empty;

        if (content is JsonArray arr)
        {
            var texts = new List<string>();
            foreach (var item in arr)
            {
                var itemType = item?["type"]?.GetValue<string>() ?? "";
                if (itemType is "text" or "input_text" or "output_text")
                {
                    var text = item?["text"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(text))
                        texts.Add(text);
                }
            }

            return string.Join("\n", texts);
        }

        return string.Empty;
    }
}
