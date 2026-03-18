using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class MessageStartHandler : IStreamEventHandler
{
    public string EventType => "message_start";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Message?.Model is string startModel && !string.IsNullOrEmpty(startModel))
            ctx.Session.ResolvedModel = startModel;

        ctx.UsageRecorded = false;

        if (evt.Message?.Usage is { } startUsage)
        {
            ctx.AccInputTokens = startUsage.InputTokens;
            ctx.AccCacheCreation = startUsage.CacheCreationInputTokens ?? 0;
            ctx.AccCacheRead = startUsage.CacheReadInputTokens ?? 0;
            ctx.AccOutputTokens = 0;
        }

        return Task.CompletedTask;
    }
}
