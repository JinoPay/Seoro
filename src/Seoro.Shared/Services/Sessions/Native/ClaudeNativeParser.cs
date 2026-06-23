using System.Text.Json;
using System.Text.Json.Nodes;
using Seoro.Shared.Models.Chat;

namespace Seoro.Shared.Services.Sessions.Native;

/// <summary>
///     Claude CLI 네이티브 세션 파일(~/.claude/projects/&lt;hash&gt;/&lt;conversationId&gt;.jsonl)을
///     <see cref="ChatMessage" /> 목록으로 파싱합니다.
///     이 파일은 CLI가 직접 기록하는 단일 진실 소스이며, Seoro의 잘린 복제본을 대체합니다.
/// </summary>
public static class ClaudeNativeParser
{
    private static readonly HashSet<string> NoiseTypes =
    [
        "file-history-snapshot", "progress", "last-prompt", "queue-operation",
        "summary", "mode", "ai-title", "agent-name", "permission-mode", "attachment", "system"
    ];

    /// <summary>
    ///     jsonl 파일을 읽어 메인 대화 타임라인을 ChatMessage 목록으로 반환합니다.
    ///     서브에이전트(isSidechain) 라인은 제외하며, tool_result는 직전 도구 호출의 출력으로 병합됩니다.
    /// </summary>
    public static List<ChatMessage> Parse(string filePath)
    {
        var messages = new List<ChatMessage>();
        // tool_use_id → 해당 ToolCall 인스턴스 (이후 user 메시지의 tool_result로 출력을 채움)
        var toolMap = new Dictionary<string, ToolCall>();

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
            if (NoiseTypes.Contains(type)) continue;
            if (node["isSidechain"]?.GetValue<bool>() == true) continue;

            switch (type)
            {
                case "user" or "human":
                    HandleUser(node, messages, toolMap);
                    break;
                case "assistant":
                    HandleAssistant(node, messages, toolMap);
                    break;
            }
        }

        return messages;
    }

    private static void HandleUser(JsonNode node, List<ChatMessage> messages, Dictionary<string, ToolCall> toolMap)
    {
        var content = node["message"]?["content"];

        // tool_result 병합 (배열 형태의 user content)
        if (content is JsonArray arr)
        {
            var userTexts = new List<string>();
            foreach (var item in arr)
            {
                var itemType = item?["type"]?.GetValue<string>() ?? "";
                if (itemType == "tool_result")
                {
                    var id = item?["tool_use_id"]?.GetValue<string>();
                    if (id != null && toolMap.TryGetValue(id, out var tc))
                    {
                        tc.Output = ExtractToolResultText(item?["content"]);
                        tc.IsError = item?["is_error"]?.GetValue<bool>() == true;
                        tc.IsComplete = true;
                    }
                }
                else if (itemType == "text")
                {
                    var text = item?["text"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(text))
                        userTexts.Add(text);
                }
            }

            var combined = string.Join("\n", userTexts);
            if (!string.IsNullOrWhiteSpace(combined))
                messages.Add(BuildText(node, MessageRole.User, combined));
            return;
        }

        // 문자열 형태의 일반 user 메시지
        var str = NativeParseHelpers.ExtractText(content);
        if (!string.IsNullOrWhiteSpace(str))
            messages.Add(BuildText(node, MessageRole.User, str));
    }

    private static void HandleAssistant(JsonNode node, List<ChatMessage> messages, Dictionary<string, ToolCall> toolMap)
    {
        var content = node["message"]?["content"];
        if (content is not JsonArray arr) return;

        var msg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Id = node["uuid"]?.GetValue<string>() ?? Guid.NewGuid().ToString(),
            Timestamp = NativeParseHelpers.ParseTimestamp(node["timestamp"]) ?? DateTime.UtcNow
        };

        var textParts = new List<string>();

        foreach (var item in arr)
        {
            var itemType = item?["type"]?.GetValue<string>() ?? "";
            switch (itemType)
            {
                case "text":
                    var text = item?["text"]?.GetValue<string>() ?? "";
                    if (!string.IsNullOrEmpty(text))
                    {
                        textParts.Add(text);
                        msg.Parts.Add(new ContentPart { Type = ContentPartType.Text, Text = text });
                    }

                    break;

                case "thinking":
                    var thinking = item?["thinking"]?.GetValue<string>() ?? "";
                    if (!string.IsNullOrEmpty(thinking))
                        msg.Parts.Add(new ContentPart { Type = ContentPartType.Thinking, Text = thinking });
                    break;

                case "tool_use":
                    var tc = new ToolCall
                    {
                        Id = item?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString(),
                        Name = item?["name"]?.GetValue<string>() ?? "tool",
                        Input = item?["input"]?.ToJsonString() ?? "{}"
                    };
                    toolMap[tc.Id] = tc;
                    msg.ToolCalls.Add(tc);
                    msg.Parts.Add(new ContentPart { Type = ContentPartType.ToolCall, ToolCall = tc });
                    break;
            }
        }

        if (msg.Parts.Count == 0) return; // 표시할 내용 없음

        msg.Text = string.Join("\n\n", textParts);
        messages.Add(msg);
    }

    private static ChatMessage BuildText(JsonNode node, MessageRole role, string text)
    {
        return new ChatMessage
        {
            Role = role,
            Id = node["uuid"]?.GetValue<string>() ?? Guid.NewGuid().ToString(),
            Timestamp = NativeParseHelpers.ParseTimestamp(node["timestamp"]) ?? DateTime.UtcNow,
            Text = text,
            Parts = { new ContentPart { Type = ContentPartType.Text, Text = text } }
        };
    }

    /// <summary>tool_result의 content(문자열 또는 콘텐츠 블록 배열)에서 텍스트를 추출합니다.</summary>
    private static string ExtractToolResultText(JsonNode? content)
    {
        if (content == null) return string.Empty;
        if (content.GetValueKind() == JsonValueKind.String)
            return content.GetValue<string>() ?? string.Empty;
        return NativeParseHelpers.ExtractText(content);
    }
}
