using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Chat.StreamEventHandlers;

public class AiTitleHandler(
    IChatState chatState,
    IChatEventBus eventBus,
    ILogger<AiTitleHandler> logger) : IStreamEventHandler
{
    public string EventType => "ai-title";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.ExtensionData == null
            || !evt.ExtensionData.TryGetValue("aiTitle", out var aiTitleElement)
            || aiTitleElement.ValueKind != JsonValueKind.String)
            return Task.CompletedTask;

        var title = aiTitleElement.GetString();
        if (string.IsNullOrWhiteSpace(title))
            return Task.CompletedTask;

        ctx.Session.Title = title;
        ctx.Session.TitleLocked = true;
        chatState.Tabs.UpdateChatTabTitle(title);
        eventBus.Publish(new SessionTitleChangedEvent(ctx.Session.Id, title));

        logger.LogDebug("ai-title 이벤트로 세션 타이틀 설정: {Title}, sessionId={SessionId}",
            title, ctx.Session.Id);

        return Task.CompletedTask;
    }
}
