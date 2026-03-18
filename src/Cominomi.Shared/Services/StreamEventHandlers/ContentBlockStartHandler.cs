using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class ContentBlockStartHandler : IStreamEventHandler
{
    private readonly IChatState _chatState;

    public ContentBlockStartHandler(IChatState chatState) => _chatState = chatState;

    public string EventType => "content_block_start";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        switch (evt.ContentBlock?.Type)
        {
            case "thinking":
                _chatState.SetPhase(StreamingPhase.Thinking, sessionId: ctx.Session.Id);
                break;

            case "redacted_thinking":
                _chatState.AppendThinking(ctx.AssistantMessage, "[사고 내용 생략됨]");
                break;

            case "server_tool_use":
            case "tool_use":
                ctx.CurrentToolCall = new ToolCall
                {
                    Id = evt.ContentBlock.Id ?? "",
                    Name = evt.ContentBlock.Name ?? ""
                };
                _chatState.AddToolCall(ctx.AssistantMessage, ctx.CurrentToolCall);
                _chatState.SetPhase(StreamingPhase.UsingTool, evt.ContentBlock.Name, ctx.Session.Id);
                if (evt.ContentBlock.Name == "ExitPlanMode")
                    ctx.ExitPlanModeDetected = true;
                break;

            case "server_tool_result":
            case "tool_result":
                if (evt.Index.HasValue && !string.IsNullOrEmpty(evt.ContentBlock.ToolUseId))
                {
                    ctx.ToolResultBlockMap[evt.Index.Value] = evt.ContentBlock.ToolUseId;
                    var matchingTool = ctx.AssistantMessage.ToolCalls.FirstOrDefault(t => t.Id == evt.ContentBlock.ToolUseId);
                    if (matchingTool != null)
                    {
                        matchingTool.IsError = evt.ContentBlock.IsError ?? false;
                        if (evt.ContentBlock.Content != null)
                            matchingTool.Output = StreamEventUtils.ExtractToolResultContent(evt.ContentBlock.Content.Value);
                        _chatState.NotifyStateChanged();
                    }
                }
                break;
        }

        return Task.CompletedTask;
    }
}
