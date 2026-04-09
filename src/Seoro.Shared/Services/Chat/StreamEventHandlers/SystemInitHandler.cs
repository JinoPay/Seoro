
namespace Seoro.Shared.Services.Chat.StreamEventHandlers;

public class SystemInitHandler : IStreamEventHandler
{
    public string EventType => "system";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Subtype != "init") return Task.CompletedTask;

        if (!string.IsNullOrEmpty(evt.SessionId))
            ctx.Session.ConversationId = evt.SessionId;

        return Task.CompletedTask;
    }
}