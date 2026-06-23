using System.Text.Json;
using System.Text.Json.Nodes;
using Seoro.Shared.Models.Chat;

namespace Seoro.Shared.Services.Sessions.Native;

/// <summary>
///     Codex CLI 네이티브 rollout 파일(~/.codex/sessions/YYYY/MM/DD/rollout-&lt;ts&gt;-&lt;thread_id&gt;.jsonl)을
///     <see cref="ChatMessage" /> 목록으로 파싱합니다.
///     rollout 포맷은 라이브 <c>exec --json</c> 이벤트와 다르며, <c>{timestamp,type,payload}</c> 구조입니다.
///     표시에 충분한 <c>response_item</c> 라인만 사용하고 중복되는 <c>event_msg</c>는 무시합니다.
/// </summary>
public static class CodexRolloutParser
{
    public static List<ChatMessage> Parse(string filePath)
    {
        var messages = new List<ChatMessage>();
        // call_id → ToolCall (이후 *_output 항목으로 출력을 채움)
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
            if (node["type"]?.GetValue<string>() != "response_item") continue;

            var payload = node["payload"];
            if (payload == null) continue;

            var ts = NativeParseHelpers.ParseTimestamp(node["timestamp"]) ?? DateTime.UtcNow;
            var pType = payload["type"]?.GetValue<string>() ?? "";

            switch (pType)
            {
                case "message":
                    HandleMessage(payload, ts, messages);
                    break;
                case "reasoning":
                    HandleReasoning(payload, ts, messages);
                    break;
                case "function_call":
                case "custom_tool_call":
                case "local_shell_call":
                    HandleToolCall(payload, pType, ts, messages, toolMap);
                    break;
                case "function_call_output":
                case "custom_tool_call_output":
                    HandleToolOutput(payload, toolMap);
                    break;
            }
        }

        return messages;
    }

    private static void HandleMessage(JsonNode payload, DateTime ts, List<ChatMessage> messages)
    {
        var role = payload["role"]?.GetValue<string>() ?? "";
        if (role == "developer") return; // 합성 시스템 지시 — 표시 안 함

        var text = NativeParseHelpers.ExtractText(payload["content"]);
        if (string.IsNullOrWhiteSpace(text)) return;

        if (role == "assistant")
        {
            messages.Add(BuildText(MessageRole.Assistant, text, ts));
            return;
        }

        // role == "user" (또는 기타): Seoro가 주입한 환경 컨텍스트/시스템 지시 필터링
        if (text.StartsWith("<environment_context>", StringComparison.Ordinal)) return;

        var cleaned = StripSyntheticPrefix(text);
        if (!string.IsNullOrWhiteSpace(cleaned))
            messages.Add(BuildText(MessageRole.User, cleaned, ts));
    }

    private static void HandleReasoning(JsonNode payload, DateTime ts, List<ChatMessage> messages)
    {
        // encrypted_content는 표시 불가 — summary 텍스트만 추출
        if (payload["summary"] is not JsonArray summary || summary.Count == 0) return;

        var parts = new List<string>();
        foreach (var item in summary)
        {
            var text = item?["text"]?.GetValue<string>()
                       ?? (item?.GetValueKind() == JsonValueKind.String ? item.GetValue<string>() : null);
            if (!string.IsNullOrEmpty(text))
                parts.Add(text);
        }

        var combined = string.Join("\n", parts);
        if (string.IsNullOrWhiteSpace(combined)) return;

        var msg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Id = Guid.NewGuid().ToString(),
            Timestamp = ts,
            Parts = { new ContentPart { Type = ContentPartType.Thinking, Text = combined } }
        };
        messages.Add(msg);
    }

    private static void HandleToolCall(JsonNode payload, string pType, DateTime ts,
        List<ChatMessage> messages, Dictionary<string, ToolCall> toolMap)
    {
        var callId = payload["call_id"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
        // function_call: "arguments" (JSON 문자열) / custom_tool_call: "input" (원시 문자열)
        var input = payload["arguments"]?.GetValue<string>()
                    ?? payload["input"]?.GetValue<string>()
                    ?? "{}";

        var tc = new ToolCall
        {
            Id = callId,
            Name = payload["name"]?.GetValue<string>() ?? pType,
            Input = input
        };
        toolMap[callId] = tc;

        var msg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Id = Guid.NewGuid().ToString(),
            Timestamp = ts,
            Parts = { new ContentPart { Type = ContentPartType.ToolCall, ToolCall = tc } }
        };
        msg.ToolCalls.Add(tc);
        messages.Add(msg);
    }

    private static void HandleToolOutput(JsonNode payload, Dictionary<string, ToolCall> toolMap)
    {
        var callId = payload["call_id"]?.GetValue<string>();
        if (callId == null || !toolMap.TryGetValue(callId, out var tc)) return;

        var output = payload["output"];
        tc.Output = output?.GetValueKind() == JsonValueKind.String
            ? output.GetValue<string>() ?? string.Empty
            : output?.ToJsonString() ?? string.Empty;
        tc.IsComplete = true;
    }

    private static ChatMessage BuildText(MessageRole role, string text, DateTime ts)
    {
        return new ChatMessage
        {
            Role = role,
            Id = Guid.NewGuid().ToString(),
            Timestamp = ts,
            Text = text,
            Parts = { new ContentPart { Type = ContentPartType.Text, Text = text } }
        };
    }

    /// <summary>
    ///     Seoro가 메시지 앞에 붙이는 "[System Instructions]…\n\n[User]\n&lt;실제&gt;" 프리픽스를 제거하고
    ///     실제 사용자 입력만 반환합니다. 마커가 없으면 원문을 그대로 반환합니다.
    /// </summary>
    private static string StripSyntheticPrefix(string text)
    {
        const string marker = "[User]\n";
        var idx = text.LastIndexOf(marker, StringComparison.Ordinal);
        return idx >= 0 ? text[(idx + marker.Length)..].Trim() : text;
    }
}
