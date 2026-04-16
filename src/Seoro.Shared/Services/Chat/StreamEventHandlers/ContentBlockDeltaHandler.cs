using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Chat.StreamEventHandlers;

public class ContentBlockDeltaHandler(IChatState chatState, ILogger<ContentBlockDeltaHandler> logger) : IStreamEventHandler
{
    public string EventType => "content_block_delta";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Index.HasValue && ctx.ToolResultBlockMap.TryGetValue(evt.Index.Value, out var resultToolId))
        {
            var tool = ctx.AssistantMessage.ToolCalls.FirstOrDefault(t => t.Id == resultToolId);
            if (tool != null && evt.Delta?.Text != null)
            {
                tool.Output += evt.Delta.Text;
                chatState.NotifyStateChanged();
            }
        }
        else if (evt.Delta?.Type == "text_delta" && evt.Delta.Text != null)
        {
            if (ctx.CurrentToolCall != null)
            {
                // Codex 패턴: item.updated 시 도구 출력을 text_delta로 전달
                // Claude는 text_delta 중 CurrentToolCall이 null이므로 충돌 없음
                ctx.CurrentToolCall.Output += evt.Delta.Text;
                chatState.NotifyStateChanged();
            }
            else
            {
                chatState.AppendText(ctx.AssistantMessage, evt.Delta.Text);
                chatState.SetPhase(StreamingPhase.WritingText, sessionId: ctx.Session.Id);
            }
        }
        else if (evt.Delta?.Type == "thinking_delta" && evt.Delta.Thinking != null)
        {
            chatState.AppendThinking(ctx.AssistantMessage, evt.Delta.Thinking);
            chatState.SetPhase(StreamingPhase.Thinking, sessionId: ctx.Session.Id);
        }
        else if (evt.Delta?.Type == "input_json_delta" && evt.Delta.PartialJson != null && ctx.CurrentToolCall != null)
        {
            ctx.CurrentToolCall.Input += evt.Delta.PartialJson;
        }

        return Task.CompletedTask;
    }
}