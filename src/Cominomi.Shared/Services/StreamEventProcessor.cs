using Cominomi.Shared.Models;
using Cominomi.Shared.Services.StreamEventHandlers;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class StreamEventProcessor : IStreamEventProcessor
{
    private readonly Dictionary<string, IStreamEventHandler> _handlers;
    private readonly IChatState _chatState;
    private readonly IUsageService _usageService;
    private readonly ILogger<StreamEventProcessor> _logger;

    public StreamEventProcessor(
        IEnumerable<IStreamEventHandler> handlers,
        IChatState chatState,
        IUsageService usageService,
        ILogger<StreamEventProcessor> logger)
    {
        _handlers = handlers.ToDictionary(h => h.EventType);
        _chatState = chatState;
        _usageService = usageService;
        _logger = logger;
    }

    public async Task ProcessEventAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Type != null && _handlers.TryGetValue(evt.Type, out var handler))
            await handler.HandleAsync(evt, ctx);
        else
            _logger.LogDebug("Unhandled Claude event type: {Type}", evt.Type);
    }

    public async Task FinalizeAsync(StreamProcessingContext ctx)
    {
        // Final fallback: if no event recorded usage, use accumulated values
        if (!ctx.UsageRecorded && (ctx.AccInputTokens > 0 || ctx.AccOutputTokens > 0))
        {
            var fallbackUsage = new UsageInfo
            {
                InputTokens = ctx.AccInputTokens,
                OutputTokens = ctx.AccOutputTokens,
                CacheCreationInputTokens = ctx.AccCacheCreation,
                CacheReadInputTokens = ctx.AccCacheRead,
            };
            ctx.Session.TotalInputTokens += fallbackUsage.InputTokens;
            ctx.Session.TotalOutputTokens += fallbackUsage.OutputTokens;
            await StreamEventUtils.RecordUsageAsync(ctx.Session, fallbackUsage, null, _usageService, _logger);
            _logger.LogWarning("Usage recorded from post-loop fallback. In={In}, Out={Out}",
                ctx.AccInputTokens, ctx.AccOutputTokens);
        }

        // Detect plan completion
        if (ctx.Session.PermissionMode == "plan")
        {
            // Layer 2: text-based detection
            if (!ctx.ExitPlanModeDetected)
            {
                var text = ctx.AssistantMessage.Text ?? "";
                if (text.Contains("ExitPlanMode", StringComparison.OrdinalIgnoreCase))
                    ctx.ExitPlanModeDetected = true;
            }

            // Layer 3: file system detection
            if (!ctx.ExitPlanModeDetected && !string.IsNullOrEmpty(ctx.Session.Git.WorktreePath))
            {
                await DetectPlanFileAsync(ctx);
                if (ctx.PlanContent != null)
                    ctx.ExitPlanModeDetected = true;
            }

            if (ctx.ExitPlanModeDetected)
            {
                if (ctx.PlanContent == null)
                    await DetectPlanFileAsync(ctx);

                // Fallback: use assistant message text as plan content
                if (ctx.PlanContent == null && !string.IsNullOrEmpty(ctx.AssistantMessage.Text))
                    ctx.PlanContent = ctx.AssistantMessage.Text;

                ctx.Session.PlanCompleted = true;
                ctx.Session.PlanFilePath = ctx.PlanFilePath;
                ctx.PlanReviewVisible = true;
                ctx.QuickResponseVisible = false;
                ctx.QuickResponseOptions = [];
            }
            else
            {
                ctx.PlanReviewVisible = false;
                ctx.QuickResponseVisible = false;
                ctx.QuickResponseOptions = [];
            }
        }
        else
        {
            ctx.PlanReviewVisible = false;

            var (isQuestion, options) = QuestionDetector.Detect(ctx.AssistantMessage);
            ctx.QuickResponseVisible = isQuestion;
            ctx.QuickResponseOptions = options;
        }
    }

    private static async Task DetectPlanFileAsync(StreamProcessingContext ctx)
    {
        ctx.PlanFilePath = null;
        ctx.PlanContent = null;
        var plansDir = Path.Combine(ctx.Session.Git.WorktreePath, ".claude", "plans");
        if (!Directory.Exists(plansDir)) return;

        var cutoff = ctx.StreamStartTime.AddSeconds(-5);
        var planFile = Directory.GetFiles(plansDir, "*.md")
            .Where(f => File.GetLastWriteTimeUtc(f) > cutoff)
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .FirstOrDefault();
        if (planFile != null)
        {
            ctx.PlanFilePath = planFile;
            ctx.PlanContent = await File.ReadAllTextAsync(planFile);
        }
    }

    /// <summary>
    /// Kept for backward compatibility with existing tests.
    /// Delegates to <see cref="StreamEventUtils.ExtractToolResultContent"/>.
    /// </summary>
    internal static string ExtractToolResultContent(System.Text.Json.JsonElement content)
        => StreamEventUtils.ExtractToolResultContent(content);
}
