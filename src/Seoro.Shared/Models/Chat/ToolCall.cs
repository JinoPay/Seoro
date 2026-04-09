namespace Seoro.Shared.Models.Chat;

public class ToolCall
{
    public bool IsComplete { get; set; }
    public bool IsError { get; set; }
    public string Id { get; init; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Output { get; set; } = string.Empty;

    /// <summary>
    ///     설정된 경우, 이 도구 호출은 부모 Agent 도구에 의해 호출되었습니다.
    ///     값은 부모 Agent의 tool_use_id입니다.
    /// </summary>
    public string? ParentToolUseId { get; init; }
}