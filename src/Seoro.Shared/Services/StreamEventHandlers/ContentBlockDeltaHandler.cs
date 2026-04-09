using Seoro.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.StreamEventHandlers;

public class ContentBlockDeltaHandler(IChatState chatState, IChatEventBus eventBus, ILogger<ContentBlockDeltaHandler> logger) : IStreamEventHandler
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

            // 로컬 디렉토리 세션의 타이틀 마커를 스트리밍 중 즉시 감지
            if (ctx.Session.Git.IsLocalDir && !ctx.Session.TitleLocked)
                TryExtractTitleMarker(ctx, chatState);
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

    /// <summary>
    /// 누적된 메시지 텍스트에서 타이틀 마커를 감지하여 즉시 세션 타이틀에 반영.
    /// 마커 제거(strip)는 스트리밍 종료 후 FinalizeAsync에서 처리.
    /// </summary>
    private void TryExtractTitleMarker(StreamProcessingContext ctx, IChatState chatState)
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
            return; // suffix 아직 미도착, 다음 delta에서 재시도

        var title = text[titleStart..endIdx].Trim();
        if (string.IsNullOrEmpty(title))
            return;

        if (title.Length > 30)
            title = title[..30];

        logger.LogWarning("[TRACE] TitleMarker detected: title={Title}, sessionId={SessionId}", title, ctx.Session.Id);
        ctx.Session.Title = title;
        ctx.Session.TitleLocked = true;
        chatState.Tabs.UpdateChatTabTitle(title);
        eventBus.Publish(new SessionTitleChangedEvent(ctx.Session.Id, title));
        logger.LogWarning("[TRACE] SessionTitleChangedEvent published for session {SessionId}", ctx.Session.Id);
    }
}