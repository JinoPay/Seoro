using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class ResultHandler : IStreamEventHandler
{
    private readonly IChatState _chatState;
    private readonly ILogger<ResultHandler> _logger;

    public ResultHandler(IChatState chatState, ILogger<ResultHandler> logger)
    {
        _chatState = chatState;
        _logger = logger;
    }

    public string EventType => "result";

    public async Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        var session = ctx.Session;

        if (string.IsNullOrEmpty(session.ConversationId) && !string.IsNullOrEmpty(evt.SessionId))
            session.ConversationId = evt.SessionId;

        var resultUsage = evt.Usage ?? evt.Message?.Usage ?? StreamEventUtils.TryExtractUsageFromExtensionData(evt);

        _logger.LogDebug("Result event: Usage={HasUsage}, AccIn={AccIn}, AccOut={AccOut}",
            evt.Usage != null, ctx.AccInputTokens, ctx.AccOutputTokens);

        // Accumulate session-level token counts (for in-session display)
        if (resultUsage != null && !ctx.UsageRecorded)
        {
            session.TotalInputTokens += resultUsage.InputTokens;
            session.TotalOutputTokens += resultUsage.OutputTokens;
            ctx.UsageRecorded = true;
        }
        else if (!ctx.UsageRecorded && (ctx.AccInputTokens > 0 || ctx.AccOutputTokens > 0))
        {
            session.TotalInputTokens += ctx.AccInputTokens;
            session.TotalOutputTokens += ctx.AccOutputTokens;
            ctx.UsageRecorded = true;
            _logger.LogDebug("Usage from accumulated deltas. In={In}, Out={Out}",
                ctx.AccInputTokens, ctx.AccOutputTokens);
        }

        // Fallback: populate Parts from result content if empty
        if (ctx.AssistantMessage.Parts.Count == 0)
        {
            if (evt.Message?.Content != null)
            {
                foreach (var block in evt.Message.Content)
                {
                    if (block.Type == "text" && block.Text != null)
                        _chatState.AppendText(ctx.AssistantMessage, block.Text);
                    if (block.Type == "tool_use" && block.Name == "ExitPlanMode")
                        ctx.ExitPlanModeDetected = true;
                }
            }
            if (string.IsNullOrEmpty(ctx.AssistantMessage.Text) && !string.IsNullOrEmpty(evt.Result))
                _chatState.AppendText(ctx.AssistantMessage, evt.Result);
        }
        else
        {
            // Still check for ExitPlanMode even if Parts exist
            if (evt.Message?.Content != null)
            {
                foreach (var block in evt.Message.Content)
                {
                    if (block.Type == "tool_use" && block.Name == "ExitPlanMode")
                        ctx.ExitPlanModeDetected = true;
                }
            }
        }

        await Task.CompletedTask;
    }
}
