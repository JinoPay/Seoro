using System.Text.Json;
using AB = AgentBridge;
using Seoro.Shared.Models.Chat;

namespace Seoro.Shared.Services.Cli;

/// <summary>
/// AgentBridge의 통합 <see cref="AB.AgentMessage"/> 스트림을 Seoro 파이프라인이 소비하는
/// <see cref="StreamEvent"/>(Anthropic stream-json 형태)로 변환한다.
/// 메시지 전송(SendMessageAsync 호출) 당 인스턴스 1개를 사용한다.
///
/// 핵심 전략:
/// - Claude는 IncludePartialMessages=ON → <see cref="AB.PartialMessage"/>(Raw=원본 stream_event)를 그대로
///   StreamEvent로 패스스루하여 토큰 단위 스트리밍을 재현한다. 콘텐츠가 partial로 이미 흘렀으므로
///   최종 <see cref="AB.AssistantMessage"/>는 스킵(텍스트/도구 중복 append 방지).
/// - Codex는 partial이 없으므로 message-level <see cref="AB.AssistantMessage"/>/<see cref="AB.UserMessage"/>를
///   "assistant"/"user" StreamEvent로 그대로 내보낸다. Codex 도구명/입력을 Seoro가 기대하는 형태로 정규화한다.
/// </summary>
public sealed class StreamEventTranslator
{
    private static readonly JsonSerializerOptions PartialJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly bool _isCodex;
    private readonly bool _streamsPartials;

    public StreamEventTranslator(string providerId)
    {
        _isCodex = string.Equals(providerId, "codex", StringComparison.OrdinalIgnoreCase);
        // Claude는 partial 스트리밍(IncludePartialMessages) 활성. 그 외 프로바이더는 message-level.
        _streamsPartials = string.Equals(providerId, "claude", StringComparison.OrdinalIgnoreCase);
    }

    public IEnumerable<StreamEvent> Translate(AB.AgentMessage message)
    {
        switch (message)
        {
            case AB.SystemMessage sm:
                // Claude의 init, Codex의 thread.started를 동일한 system/init으로 정규화 → ConversationId 캡처.
                if (sm.Subtype is "init" or "thread.started")
                    yield return new StreamEvent
                    {
                        Type = "system",
                        Subtype = "init",
                        SessionId = sm.SessionId,
                    };
                yield break;

            case AB.PartialMessage pm:
                var passthrough = TranslatePartial(pm);
                if (passthrough != null)
                    yield return passthrough;
                yield break;

            case AB.AssistantMessage am:
                // partial 스트리밍 모드(Claude)에서는 content_block_* 경로로 이미 전달됨 → 중복 방지 위해 스킵.
                if (_streamsPartials)
                    yield break;
                var content = MapBlocks(am.Content);
                if (content.Count == 0)
                    yield break; // 예: Codex의 ToolProgress 전용 메시지
                yield return new StreamEvent
                {
                    Type = "assistant",
                    SessionId = am.SessionId,
                    ParentToolUseId = am.ParentToolUseId,
                    Model = am.Model,
                    Message = new StreamMessage
                    {
                        Role = "assistant",
                        Model = am.Model ?? "",
                        Content = content,
                        Usage = MapUsage(am.Usage),
                    },
                };
                yield break;

            case AB.UserMessage um:
                var userContent = MapBlocks(um.Content);
                if (userContent.Count == 0)
                    yield break;
                yield return new StreamEvent
                {
                    Type = "user",
                    SessionId = um.SessionId,
                    ParentToolUseId = um.ParentToolUseId,
                    Message = new StreamMessage
                    {
                        Role = "user",
                        Content = userContent,
                    },
                };
                yield break;

            case AB.ResultMessage rm:
                yield return new StreamEvent
                {
                    Type = "result",
                    Subtype = rm.Subtype,
                    SessionId = rm.SessionId,
                    Result = rm.Result,
                    TotalCostUsd = rm.TotalCostUsd.HasValue ? (decimal)rm.TotalCostUsd.Value : null,
                    Usage = MapUsage(rm.Usage),
                };
                if (rm.IsError)
                    yield return new StreamEvent { Type = "error", Error = ToError(rm.Result ?? rm.Subtype) };
                yield break;

            case AB.ErrorMessage em:
                yield return new StreamEvent { Type = "error", Error = ToError(em.Message) };
                yield break;
        }
    }

    /// <summary>
    /// PartialMessage.Raw(원본 <c>{"type":"stream_event","event":{...}}</c>)의 내부 <c>event</c>를
    /// 그대로 StreamEvent로 역직렬화한다. 내부 이벤트는 Anthropic의 content_block_start/delta/stop,
    /// message_start, message_delta 형태이며 Seoro의 핸들러가 직접 소비한다.
    /// </summary>
    private static StreamEvent? TranslatePartial(AB.PartialMessage pm)
    {
        if (pm.Raw is not { } raw || raw.ValueKind != JsonValueKind.Object)
            return null;
        if (!raw.TryGetProperty("event", out var inner) || inner.ValueKind != JsonValueKind.Object)
            return null;

        StreamEvent? evt;
        try
        {
            evt = inner.Deserialize<StreamEvent>(PartialJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (evt == null || string.IsNullOrEmpty(evt.Type))
            return null;

        // 외부 래퍼의 귀속 정보를 내부 이벤트로 전파(서브에이전트 중첩/세션 추적용).
        if (string.IsNullOrEmpty(evt.ParentToolUseId)
            && raw.TryGetProperty("parent_tool_use_id", out var pid)
            && pid.ValueKind == JsonValueKind.String)
            evt.ParentToolUseId = pid.GetString();
        if (string.IsNullOrEmpty(evt.SessionId))
            evt.SessionId = pm.SessionId;

        return evt;
    }

    private List<ContentBlock> MapBlocks(IReadOnlyList<AB.ContentBlock> blocks)
    {
        var result = new List<ContentBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            switch (block)
            {
                case AB.TextBlock t:
                    result.Add(new ContentBlock { Type = "text", Text = t.Text });
                    break;

                case AB.ThinkingBlock th:
                    result.Add(new ContentBlock { Type = "thinking", Thinking = th.Thinking });
                    break;

                case AB.ToolUseBlock tu:
                    result.Add(MapToolUse(tu));
                    break;

                case AB.ToolResultBlock tr:
                    result.Add(new ContentBlock
                    {
                        Type = "tool_result",
                        ToolUseId = tr.ToolUseId,
                        Content = JsonSerializer.SerializeToElement(tr.Content ?? string.Empty),
                        IsError = tr.IsError,
                    });
                    break;

                // ToolProgressBlock(진행 중 명령 출력)은 message-level 최종 결과로 대체되므로 스킵.
                case AB.ToolProgressBlock:
                    break;
            }
        }

        return result;
    }

    private ContentBlock MapToolUse(AB.ToolUseBlock tu)
    {
        var name = tu.Name;
        JsonElement? input = tu.Input.ValueKind == JsonValueKind.Undefined ? null : tu.Input;

        if (_isCodex)
        {
            switch (tu.Name)
            {
                case "command_execution":
                    name = "Bash";
                    input = ShapeCommandInput(tu.Input);
                    break;
                case "file_change":
                    name = "Edit";
                    input = ShapeFileChangeInput(tu.Input);
                    break;
                case "web_search":
                    name = "WebSearch";
                    break;
                case "file_search":
                    name = "FileSearch";
                    break;
                case "collab_tool_call":
                    name = "Agent";
                    break;
                case "mcp_elicitation":
                    name = "McpElicitation";
                    break;
                // mcp_tool_call은 AgentBridge가 이미 실제 도구명으로 해석해 전달한다.
            }
        }

        return new ContentBlock
        {
            Type = "tool_use",
            Id = tu.Id,
            Name = name,
            Input = input,
        };
    }

    /// <summary>Codex command_execution 아이템에서 command만 추출 → <c>{ "command": ... }</c>.</summary>
    private static JsonElement ShapeCommandInput(JsonElement item)
    {
        var command = item.ValueKind == JsonValueKind.Object
                      && item.TryGetProperty("command", out var cmd)
                      && cmd.ValueKind == JsonValueKind.String
            ? cmd.GetString() ?? string.Empty
            : string.Empty;
        return JsonSerializer.SerializeToElement(new { command });
    }

    /// <summary>Codex file_change 아이템에서 첫 변경 파일 경로 추출 → <c>{ "path": ... }</c>.</summary>
    private static JsonElement ShapeFileChangeInput(JsonElement item)
    {
        var path = string.Empty;
        if (item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("changes", out var changes)
            && changes.ValueKind == JsonValueKind.Array
            && changes.GetArrayLength() > 0)
        {
            var first = changes[0];
            if (first.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                path = p.GetString() ?? string.Empty;
        }

        return JsonSerializer.SerializeToElement(new { path });
    }

    private static UsageInfo? MapUsage(AB.TokenUsage? usage)
    {
        if (usage == null)
            return null;

        return new UsageInfo
        {
            InputTokens = ToInt(usage.InputTokens),
            OutputTokens = ToInt(usage.OutputTokens),
            CacheCreationInputTokens = usage.CacheCreationInputTokens.HasValue ? ToInt(usage.CacheCreationInputTokens) : null,
            CacheReadInputTokens = usage.CacheReadInputTokens.HasValue ? ToInt(usage.CacheReadInputTokens) : null,
            // ReasoningOutputTokens는 Seoro에 대응 필드가 없어 드롭(이중 계산 방지, 현행 동일).
        };
    }

    private static int ToInt(long? value)
    {
        if (!value.HasValue || value.Value <= 0)
            return 0;
        return value.Value > int.MaxValue ? int.MaxValue : (int)value.Value;
    }

    private static JsonElement ToError(string message)
        => JsonSerializer.SerializeToElement(message);
}
