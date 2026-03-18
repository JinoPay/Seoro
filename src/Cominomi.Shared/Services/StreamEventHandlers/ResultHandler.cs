using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class ResultHandler : IStreamEventHandler
{
    private readonly IChatState _chatState;
    private readonly IUsageService _usageService;
    private readonly ILogger<ResultHandler> _logger;

    public ResultHandler(IChatState chatState, IUsageService usageService, ILogger<ResultHandler> logger)
    {
        _chatState = chatState;
        _usageService = usageService;
        _logger = logger;
    }

    public string EventType => "result";

    public async Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        var session = ctx.Session;

        if (string.IsNullOrEmpty(session.ConversationId) && !string.IsNullOrEmpty(evt.SessionId))
            session.ConversationId = evt.SessionId;

        var resultUsage = evt.Usage ?? evt.Message?.Usage ?? StreamEventUtils.TryExtractUsageFromExtensionData(evt);
        var costOverride = StreamEventUtils.TryExtractCost(evt);

        _logger.LogDebug("Result event: Usage={HasUsage}, Cost={Cost}, AccIn={AccIn}, AccOut={AccOut}",
            evt.Usage != null, costOverride, ctx.AccInputTokens, ctx.AccOutputTokens);

        if (resultUsage != null && !ctx.UsageRecorded)
        {
            session.TotalInputTokens += resultUsage.InputTokens;
            session.TotalOutputTokens += resultUsage.OutputTokens;
            await StreamEventUtils.RecordUsageAsync(session, resultUsage, costOverride, _usageService, _logger);
            ctx.UsageRecorded = true;
        }
        else if (!ctx.UsageRecorded && (ctx.AccInputTokens > 0 || ctx.AccOutputTokens > 0))
        {
            var accUsage = new UsageInfo
            {
                InputTokens = ctx.AccInputTokens,
                OutputTokens = ctx.AccOutputTokens,
                CacheCreationInputTokens = ctx.AccCacheCreation,
                CacheReadInputTokens = ctx.AccCacheRead,
            };
            session.TotalInputTokens += accUsage.InputTokens;
            session.TotalOutputTokens += accUsage.OutputTokens;
            await StreamEventUtils.RecordUsageAsync(session, accUsage, costOverride, _usageService, _logger);
            ctx.UsageRecorded = true;
            _logger.LogWarning("Usage recorded from accumulated deltas. In={In}, Out={Out}",
                ctx.AccInputTokens, ctx.AccOutputTokens);
        }
        else if (!ctx.UsageRecorded && costOverride.HasValue && costOverride > 0)
        {
            var costOnlyUsage = new UsageInfo { InputTokens = 0, OutputTokens = 0 };
            await StreamEventUtils.RecordUsageAsync(session, costOnlyUsage, costOverride, _usageService, _logger);
            ctx.UsageRecorded = true;
            _logger.LogWarning("Usage recorded with cost only. Cost=${Cost:F6}", costOverride.Value);
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
    }
}
