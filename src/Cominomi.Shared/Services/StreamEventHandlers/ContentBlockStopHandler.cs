using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class ContentBlockStopHandler : IStreamEventHandler
{
    private readonly IChatState _chatState;
    private readonly ISessionService _sessionService;

    public ContentBlockStopHandler(IChatState chatState, ISessionService sessionService)
    {
        _chatState = chatState;
        _sessionService = sessionService;
    }

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
            _chatState.NotifyStateChanged();
        }

        _chatState.SetPhase(StreamingPhase.Thinking, sessionId: ctx.Session.Id);
        _ = _sessionService.SaveSessionAsync(ctx.Session);

        return Task.CompletedTask;
    }
}
