using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class MessageDeltaHandler(IChatState chatState) : IStreamEventHandler
{
    public string EventType => "message_delta";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        var deltaUsage = evt.Usage ?? evt.Message?.Usage;
        if (deltaUsage != null)
        {
            if (deltaUsage.InputTokens > 0) ctx.AccInputTokens = deltaUsage.InputTokens;
            if (deltaUsage.OutputTokens > 0) ctx.AccOutputTokens = deltaUsage.OutputTokens;
            if (deltaUsage.CacheCreationInputTokens is > 0)
                ctx.AccCacheCreation = deltaUsage.CacheCreationInputTokens.Value;
            if (deltaUsage.CacheReadInputTokens is > 0) ctx.AccCacheRead = deltaUsage.CacheReadInputTokens.Value;

            // Mirror accumulators to session for real-time ring display
            ctx.Session.PendingInputTokens = ctx.AccInputTokens;
            ctx.Session.PendingOutputTokens = ctx.AccOutputTokens;
        }

        var stopReason = evt.Delta?.StopReason ?? evt.Message?.StopReason;
        if (!string.IsNullOrEmpty(stopReason) && stopReason == "max_tokens")
            chatState.AppendText(ctx.AssistantMessage, "\n\n⚠️ *응답이 최대 토큰 한도에 도달하여 잘렸습니다.*");

        return Task.CompletedTask;
    }
}