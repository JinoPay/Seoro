using System.Text.Json;
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
            // Layer 1: detect plan file path from Write/Edit tool calls (must run first)
            if (ctx.DetectedPlanFilePath == null)
                DetectPlanFileFromToolCalls(ctx);

            // Persist detected path on session so it survives across turns
            if (ctx.DetectedPlanFilePath != null)
                ctx.Session.PlanFilePath = ctx.DetectedPlanFilePath;

            // Restore from session if this turn didn't detect (Write and ExitPlanMode in different turns)
            if (ctx.DetectedPlanFilePath == null && ctx.Session.PlanFilePath != null
                && File.Exists(ctx.Session.PlanFilePath))
                ctx.DetectedPlanFilePath = ctx.Session.PlanFilePath;

            // Layer 2: text-based detection
            if (!ctx.ExitPlanModeDetected)
            {
                var text = ctx.AssistantMessage.Text ?? "";
                if (text.Contains("ExitPlanMode", StringComparison.OrdinalIgnoreCase))
                    ctx.ExitPlanModeDetected = true;
            }

            // Layer 3: file system detection (now uses DetectedPlanFilePath first)
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

            // AskUserQuestion takes priority over plan review
            if (HasAskUserQuestionToolCall(ctx))
            {
                ctx.PlanReviewVisible = false;
                ctx.Session.PlanCompleted = false; // AskUserQuestion supersedes plan review
                ctx.Session.PendingAskUserQuestionInput = ctx.AskUserQuestionInput;
                ctx.QuickResponseVisible = false;
                ctx.QuickResponseOptions = [];
            }
        }
        else
        {
            ctx.PlanReviewVisible = false;

            if (HasAskUserQuestionToolCall(ctx))
            {
                ctx.Session.PendingAskUserQuestionInput = ctx.AskUserQuestionInput;
                ctx.QuickResponseVisible = false;
                ctx.QuickResponseOptions = [];
            }
            else
            {
                ctx.QuickResponseVisible = false;
                ctx.QuickResponseOptions = [];
            }
        }
    }

    private static bool HasAskUserQuestionToolCall(StreamProcessingContext ctx)
    {
        foreach (var part in ctx.AssistantMessage.Parts)
        {
            if (part.Type != ContentPartType.ToolCall || part.ToolCall == null)
                continue;
            if (part.ToolCall.Name.Equals("AskUserQuestion", StringComparison.OrdinalIgnoreCase)
                || part.ToolCall.Name.Equals("ask_user_question", StringComparison.OrdinalIgnoreCase))
            {
                ctx.AskUserQuestionInput = part.ToolCall.Input;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Scans the assistant message's tool calls for Write/Edit operations targeting .claude/plans/*.md
    /// to precisely identify which plan file belongs to this session.
    /// </summary>
    private static void DetectPlanFileFromToolCalls(StreamProcessingContext ctx)
    {
        var writeEditNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Write", "write", "write_file", "Edit", "edit", "edit_file" };

        foreach (var part in ctx.AssistantMessage.Parts)
        {
            if (part.Type != ContentPartType.ToolCall || part.ToolCall == null)
                continue;
            if (!writeEditNames.Contains(part.ToolCall.Name))
                continue;
            if (string.IsNullOrEmpty(part.ToolCall.Input))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(part.ToolCall.Input);
                if (doc.RootElement.TryGetProperty("file_path", out var fp))
                {
                    var path = fp.GetString();
                    if (path != null)
                    {
                        var normalized = path.Replace('\\', '/');
                        if (normalized.Contains(".claude/plans/") && normalized.EndsWith(".md"))
                        {
                            ctx.DetectedPlanFilePath = path;
                            return;
                        }
                    }
                }
            }
            catch { /* ignore parse errors */ }
        }
    }

    private static async Task DetectPlanFileAsync(StreamProcessingContext ctx)
    {
        ctx.PlanFilePath = null;
        ctx.PlanContent = null;

        // Prefer the exact plan file detected from Write/Edit tool calls
        if (!string.IsNullOrEmpty(ctx.DetectedPlanFilePath) && File.Exists(ctx.DetectedPlanFilePath))
        {
            ctx.PlanFilePath = ctx.DetectedPlanFilePath;
            ctx.PlanContent = await File.ReadAllTextAsync(ctx.DetectedPlanFilePath);
            return;
        }

        // Fallback: scan filesystem for recent plan files
        var candidates = new List<string>();

        // Primary: ~/.claude/plans/ (where Claude CLI actually saves plan files)
        var homePlansDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "plans");
        if (Directory.Exists(homePlansDir))
            candidates.AddRange(Directory.GetFiles(homePlansDir, "*.md"));

        // Secondary: {WorktreePath}/.claude/plans/ (in case system prompt directed here)
        if (!string.IsNullOrEmpty(ctx.Session.Git.WorktreePath))
        {
            var worktreePlansDir = Path.Combine(ctx.Session.Git.WorktreePath, ".claude", "plans");
            if (Directory.Exists(worktreePlansDir))
                candidates.AddRange(Directory.GetFiles(worktreePlansDir, "*.md"));
        }

        if (candidates.Count == 0) return;

        // Use a generous cutoff to handle multi-turn plan conversations
        var cutoff = ctx.StreamStartTime.AddMinutes(-30);
        var planFile = candidates
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
