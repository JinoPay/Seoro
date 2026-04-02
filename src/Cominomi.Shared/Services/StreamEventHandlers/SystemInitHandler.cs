using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class SystemInitHandler : IStreamEventHandler
{
    public string EventType => "system";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Subtype != "init") return Task.CompletedTask;

        if (evt.Model != null)
            ctx.Session.ResolvedModel = evt.Model;
        if (!string.IsNullOrEmpty(evt.SessionId))
            ctx.Session.ConversationId = evt.SessionId;

        return Task.CompletedTask;
    }
}