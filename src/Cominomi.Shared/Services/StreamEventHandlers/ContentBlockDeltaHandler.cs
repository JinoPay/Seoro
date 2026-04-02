using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class ContentBlockDeltaHandler(IChatState chatState) : IStreamEventHandler
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
            chatState.AppendText(ctx.AssistantMessage, evt.Delta.Text);
            chatState.SetPhase(StreamingPhase.WritingText, sessionId: ctx.Session.Id);
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