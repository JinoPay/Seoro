using System.Text.Json;
using Microsoft.Extensions.Logging;
using Seoro.Shared.Models.Chat;

namespace Seoro.Shared.Services.Codex.AppServer;

/// <summary>
///     Codex app-server 스트리밍 알림(카멜케이스 item.type, 증분 delta)을 Claude 형식의
///     <see cref="StreamEvent" />로 직접 변환한다. exec 경로의 <c>CodexEventConverter</c>와 달리
///     app-server의 증분 delta 모델에 맞춰 자체 index/누적 상태를 관리한다(turn마다 새 인스턴스).
/// </summary>
internal sealed class CodexAppServerEventAdapter(ILogger logger)
{
    private static readonly Dictionary<string, string> ToolNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["commandExecution"] = "Bash",
        ["webSearch"] = "WebSearch",
        ["mcpToolCall"] = "McpTool",
        ["collabToolCall"] = "Agent",
        ["fileSearch"] = "FileSearch",
        ["fileChange"] = "Edit",
        ["mcpElicitation"] = "McpElicitation"
    };

    private readonly Dictionary<string, string> _accum = new();
    private readonly Dictionary<string, int> _itemIndex = new();
    private int _blockCounter;
    private UsageInfo? _lastUsage;

    public IEnumerable<StreamEvent> Adapt(string method, JsonElement prms)
    {
        switch (method)
        {
            case CodexRpcMethods.ThreadStarted:
                var threadId = prms.TryGetProperty("thread", out var th) && th.TryGetProperty("id", out var ti)
                    ? ti.GetString()
                    : null;
                yield return new StreamEvent { Type = "system", Subtype = "init", SessionId = threadId };
                break;

            case CodexRpcMethods.ItemStarted:
                foreach (var e in OnItemStarted(prms)) yield return e;
                break;

            case CodexRpcMethods.ItemAgentMessageDelta:
                foreach (var e in OnAgentMessageDelta(prms)) yield return e;
                break;

            case CodexRpcMethods.ItemCompleted:
                foreach (var e in OnItemCompleted(prms)) yield return e;
                break;

            case CodexRpcMethods.ThreadTokenUsageUpdated:
                _lastUsage = ExtractUsage(prms);
                break;

            case CodexRpcMethods.TurnCompleted:
                foreach (var e in OnTurnCompleted(prms)) yield return e;
                break;
        }
    }

    private IEnumerable<StreamEvent> OnItemStarted(JsonElement prms)
    {
        if (!TryGetItem(prms, out var item, out var itemId, out var itemType)) yield break;

        switch (itemType)
        {
            case "agentMessage":
                _accum[itemId] = "";
                yield return new StreamEvent
                {
                    Type = "content_block_start",
                    Index = GetOrCreateIndex(itemId),
                    ContentBlock = new ContentBlock { Type = "text", Text = "" }
                };
                break;

            case "userMessage":
            case "reasoning":
                // userMessage는 우리가 보낸 입력, reasoning은 표시 생략(요약/내용 비는 경우 다수).
                break;

            default:
                // 도구류 → tool_use 블록 시작
                yield return new StreamEvent
                {
                    Type = "content_block_start",
                    Index = GetOrCreateIndex(itemId),
                    ContentBlock = new ContentBlock
                    {
                        Type = "tool_use",
                        Id = itemId,
                        Name = ToolNameMap.GetValueOrDefault(itemType, itemType),
                        Input = item.Clone()
                    }
                };
                break;
        }
    }

    private IEnumerable<StreamEvent> OnAgentMessageDelta(JsonElement prms)
    {
        var itemId = prms.TryGetProperty("itemId", out var idEl) ? idEl.GetString() : null;
        var delta = prms.TryGetProperty("delta", out var dEl) ? dEl.GetString() : null;
        if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(delta)) yield break;

        _accum[itemId] = _accum.GetValueOrDefault(itemId, "") + delta;
        yield return new StreamEvent
        {
            Type = "content_block_delta",
            Index = GetOrCreateIndex(itemId),
            Delta = new ContentDelta { Type = "text_delta", Text = delta }
        };
    }

    private IEnumerable<StreamEvent> OnItemCompleted(JsonElement prms)
    {
        if (!TryGetItem(prms, out var item, out var itemId, out var itemType)) yield break;
        if (itemType is "userMessage" or "reasoning") yield break;
        if (!_itemIndex.ContainsKey(itemId)) yield break; // 시작을 못 본 항목은 무시

        var index = GetOrCreateIndex(itemId);

        // agentMessage: 누락된 마지막 차분 보충
        if (itemType == "agentMessage")
        {
            var text = item.TryGetProperty("text", out var tEl) ? tEl.GetString() ?? "" : "";
            var prev = _accum.GetValueOrDefault(itemId, "");
            if (text.Length > prev.Length)
                yield return new StreamEvent
                {
                    Type = "content_block_delta",
                    Index = index,
                    Delta = new ContentDelta { Type = "text_delta", Text = text[prev.Length..] }
                };
            _accum.Remove(itemId);
        }

        yield return new StreamEvent { Type = "content_block_stop", Index = index };
    }

    private IEnumerable<StreamEvent> OnTurnCompleted(JsonElement prms)
    {
        var status = prms.TryGetProperty("turn", out var turn) && turn.TryGetProperty("status", out var st)
            ? st.GetString()
            : null;

        if (status == "failed")
        {
            var msg = turn.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object
                ? err.TryGetProperty("message", out var m) ? m.GetString() : err.ToString()
                : "Codex turn failed";
            yield return new StreamEvent { Type = "error", Error = JsonSerializer.SerializeToElement(msg) };
            yield break;
        }

        yield return new StreamEvent { Type = "result", Usage = _lastUsage };
    }

    private static UsageInfo? ExtractUsage(JsonElement prms)
    {
        if (!prms.TryGetProperty("tokenUsage", out var tu)) return null;
        // 턴 단위 사용량은 "last"(누적은 "total"). ResultHandler가 세션에 += 하므로 last를 쓴다.
        var src = tu.TryGetProperty("last", out var last) ? last : tu;
        return new UsageInfo
        {
            InputTokens = src.TryGetProperty("inputTokens", out var i) ? i.GetInt32() : 0,
            OutputTokens = src.TryGetProperty("outputTokens", out var o) ? o.GetInt32() : 0,
            CacheReadInputTokens = src.TryGetProperty("cachedInputTokens", out var c) ? c.GetInt32() : null
        };
    }

    private static bool TryGetItem(JsonElement prms, out JsonElement item, out string itemId, out string itemType)
    {
        item = default;
        itemId = "";
        itemType = "";
        if (!prms.TryGetProperty("item", out item)) return false;
        if (item.TryGetProperty("id", out var idEl)) itemId = idEl.GetString() ?? "";
        if (item.TryGetProperty("type", out var tEl)) itemType = tEl.GetString() ?? "";
        return !string.IsNullOrEmpty(itemId) && !string.IsNullOrEmpty(itemType);
    }

    private int GetOrCreateIndex(string itemId)
    {
        if (_itemIndex.TryGetValue(itemId, out var idx)) return idx;
        idx = _blockCounter++;
        _itemIndex[itemId] = idx;
        return idx;
    }
}
