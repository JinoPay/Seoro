using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

public class StreamEvent
{
    [JsonPropertyName("content_block")] public ContentBlock? ContentBlock { get; set; }

    [JsonPropertyName("delta")] public ContentDelta? Delta { get; set; }

    [JsonPropertyName("cost_usd")] public decimal? CostUsd { get; set; }

    [JsonPropertyName("total_cost_usd")] public decimal? TotalCostUsd { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonPropertyName("index")] public int? Index { get; set; }

    [JsonPropertyName("error")] public JsonElement? Error { get; set; }

    [JsonPropertyName("message")] public StreamMessage? Message { get; set; }

    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;

    [JsonPropertyName("model")] public string? Model { get; set; }

    [JsonPropertyName("parent_tool_use_id")]
    public string? ParentToolUseId { get; set; }

    [JsonPropertyName("result")] public string? Result { get; set; }

    [JsonPropertyName("session_id")] public string? SessionId { get; set; }

    [JsonPropertyName("subtype")] public string? Subtype { get; set; }

    [JsonPropertyName("usage")] public UsageInfo? Usage { get; set; }

    /// <summary>
    ///     Extract error message from either a string or structured error object.
    /// </summary>
    public string? GetErrorMessage()
    {
        if (Error == null) return null;
        var el = Error.Value;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Object => el.TryGetProperty("message", out var msg)
                ? msg.GetString()
                : el.ToString(),
            _ => el.ToString()
        };
    }
}

public class StreamMessage
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonPropertyName("content")] public List<ContentBlock>? Content { get; set; }

    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;

    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;

    [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }

    [JsonPropertyName("usage")] public UsageInfo? Usage { get; set; }
}

public class ContentBlock
{
    [JsonPropertyName("is_error")] public bool? IsError { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonPropertyName("content")] public JsonElement? Content { get; set; }

    [JsonPropertyName("input")] public JsonElement? Input { get; set; }

    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;

    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("text")] public string? Text { get; set; }

    [JsonPropertyName("thinking")] public string? Thinking { get; set; }

    [JsonPropertyName("tool_use_id")] public string? ToolUseId { get; set; }
}

public class ContentDelta
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;

    [JsonPropertyName("partial_json")] public string? PartialJson { get; set; }

    [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }

    [JsonPropertyName("text")] public string? Text { get; set; }

    [JsonPropertyName("thinking")] public string? Thinking { get; set; }
}

public class UsageInfo
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }

    [JsonPropertyName("cache_creation_input_tokens")]
    public int? CacheCreationInputTokens { get; set; }

    [JsonPropertyName("cache_read_input_tokens")]
    public int? CacheReadInputTokens { get; set; }

    [JsonPropertyName("server_tool_use_input_tokens")]
    public int? ServerToolUseInputTokens { get; set; }
}