using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Chat.StreamEventHandlers;

public class AssistantMessageHandler(
    IChatState chatState,
    IChatEventBus eventBus,
    ISessionService sessionService,
    ILogger<AssistantMessageHandler> logger) : IStreamEventHandler
{
    public string EventType => "assistant";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        chatState.SetPhase(StreamingPhase.WritingText, sessionId: ctx.Session.Id);

        // Track parent context for subagent tool calls
        ctx.CurrentParentToolUseId = evt.ParentToolUseId;

        var hasNewText = false;

        if (evt.Message?.Content != null)
            foreach (var block in evt.Message.Content)
                switch (block.Type)
                {
                    case "text" when block.Text != null:
                        chatState.AppendText(ctx.AssistantMessage, block.Text);
                        hasNewText = true;
                        break;
                    case "thinking" when (block.Thinking ?? block.Text) != null:
                        chatState.AppendThinking(ctx.AssistantMessage, block.Thinking ?? block.Text!);
                        break;
                    case "redacted_thinking":
                        chatState.AppendThinking(ctx.AssistantMessage, "[사고 내용 생략됨]");
                        break;
                    case "server_tool_use":
                    case "tool_use":
                        var tc = new ToolCall
                        {
                            Id = block.Id ?? "",
                            Name = block.Name ?? "",
                            IsComplete = true,
                            ParentToolUseId = evt.ParentToolUseId
                        };
                        if (block.Input.HasValue)
                            tc.Input = block.Input.Value.ValueKind == JsonValueKind.Undefined
                                ? ""
                                : JsonSerializer.Serialize(block.Input.Value,
                                    new JsonSerializerOptions { WriteIndented = true });
                        chatState.AddToolCall(ctx.AssistantMessage, tc);
                        chatState.SetPhase(StreamingPhase.UsingTool, block.Name, ctx.Session.Id);
                        if (block.Name == "ExitPlanMode")
                            ctx.ExitPlanModeDetected = true;
                        break;
                }

        // Detect title marker in local dir sessions (real-time, during streaming)
        if (hasNewText && ctx.Session.Git.IsLocalDir && !ctx.Session.TitleLocked)
            TryExtractTitleMarker(ctx);

        _ = sessionService.SaveSessionAsync(ctx.Session);

        return Task.CompletedTask;
    }

    private void TryExtractTitleMarker(StreamProcessingContext ctx)
    {
        var text = ctx.AssistantMessage.Text;
        if (string.IsNullOrEmpty(text))
            return;

        var startIdx = text.IndexOf(SeoroConstants.TitleMarkerPrefix, StringComparison.Ordinal);
        if (startIdx < 0)
            return;

        var titleStart = startIdx + SeoroConstants.TitleMarkerPrefix.Length;
        var endIdx = text.IndexOf(SeoroConstants.TitleMarkerSuffix, titleStart, StringComparison.Ordinal);
        if (endIdx < 0)
            return;

        var title = text[titleStart..endIdx].Trim();
        if (string.IsNullOrEmpty(title) || title.Length > 30)
        {
            if (!string.IsNullOrEmpty(title))
                title = title[..30];
            else
                return;
        }

        logger.LogWarning("[TRACE] AssistantMessageHandler: title marker detected: {Title}", title);
        ctx.Session.Title = title;
        ctx.Session.TitleLocked = true;
        chatState.Tabs.UpdateChatTabTitle(title);
        eventBus.Publish(new SessionTitleChangedEvent(ctx.Session.Id, title));
    }
}