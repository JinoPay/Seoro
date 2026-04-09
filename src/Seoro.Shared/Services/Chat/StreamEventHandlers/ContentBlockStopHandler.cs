
namespace Seoro.Shared.Services.Chat.StreamEventHandlers;

public class ContentBlockStopHandler(IChatState chatState, ISessionService sessionService) : IStreamEventHandler
{
    public string EventType => "content_block_stop";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Index.HasValue && ctx.ToolResultBlockMap.Remove(evt.Index.Value))
        {
            // tool_result block finished
        }
        else if (ctx.CurrentToolCall != null)
        {
            ctx.CurrentToolCall.IsComplete = true;
            ctx.CurrentToolCall = null;
            chatState.NotifyStateChanged();
        }

        chatState.SetPhase(StreamingPhase.Thinking, sessionId: ctx.Session.Id);
        _ = sessionService.SaveSessionAsync(ctx.Session);

        return Task.CompletedTask;
    }
}