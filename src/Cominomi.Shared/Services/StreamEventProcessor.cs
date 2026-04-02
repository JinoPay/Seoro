using System.Text.Json;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services.StreamEventHandlers;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class StreamEventProcessor : IStreamEventProcessor
{
    private readonly Dictionary<string, IStreamEventHandler> _handlers;
    private readonly IChatState _chatState;
    private readonly ILogger<StreamEventProcessor> _logger;

    public StreamEventProcessor(
        IEnumerable<IStreamEventHandler> handlers,
        IChatState chatState,
        ILogger<StreamEventProcessor> logger)
    {
        _handlers = handlers.ToDictionary(h => h.EventType);
        _chatState = chatState;
        _logger = logger;
    }

    public async Task FinalizeAsync(StreamProcessingContext ctx)
    {
        // Final fallback: accumulate session-level token counts
        if (!ctx.UsageRecorded && (ctx.AccInputTokens > 0 || ctx.AccOutputTokens > 0))
        {
            ctx.Session.TotalInputTokens += ctx.AccInputTokens;
            ctx.Session.TotalOutputTokens += ctx.AccOutputTokens;
            ctx.UsageRecorded = true;
            _logger.LogDebug("Session {SessionId}: usage {InputTokens}in/{OutputTokens}out tokens",
                ctx.Session.Id, ctx.AccInputTokens, ctx.AccOutputTokens);
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
                _logger.LogInformation("Plan completed for session {SessionId}, plan file: {PlanFile}",
                    ctx.Session.Id, ctx.PlanFilePath);
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

        // Title marker extraction for local dir sessions
        if (ctx.Session.Git.IsLocalDir)
            ExtractAndApplyTitleMarker(ctx);
    }

    public async Task ProcessEventAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Type != null && _handlers.TryGetValue(evt.Type, out var handler))
            await handler.HandleAsync(evt, ctx);
        else
            _logger.LogDebug("Unhandled Claude event type: {Type}", evt.Type);
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

    private async Task DetectPlanFileAsync(StreamProcessingContext ctx)
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
            _logger.LogDebug("Plan file resolved from filesystem scan: {PlanFilePath}", planFile);
        }
    }

    /// <summary>
    ///     Scans the assistant message's tool calls for Write/Edit operations targeting .claude/plans/*.md
    ///     to precisely identify which plan file belongs to this session.
    /// </summary>
    private void DetectPlanFileFromToolCalls(StreamProcessingContext ctx)
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
                            _logger.LogDebug("Plan file detected from tool call: {PlanFilePath}", path);
                            return;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse tool call input for plan detection");
            }
        }
    }

    private void ExtractAndApplyTitleMarker(StreamProcessingContext ctx)
    {
        var text = ctx.AssistantMessage.Text;
        if (string.IsNullOrEmpty(text))
            return;

        var startIdx = text.IndexOf(CominomiConstants.TitleMarkerPrefix, StringComparison.Ordinal);
        if (startIdx < 0)
            return;

        var titleStart = startIdx + CominomiConstants.TitleMarkerPrefix.Length;
        var endIdx = text.IndexOf(CominomiConstants.TitleMarkerSuffix, titleStart, StringComparison.Ordinal);
        if (endIdx < 0)
            return;

        var title = text[titleStart..endIdx].Trim();
        if (string.IsNullOrEmpty(title))
            return;

        if (title.Length > 30)
            title = title[..30];

        if (!ctx.Session.TitleLocked)
        {
            ctx.Session.Title = title;
            ctx.Session.TitleLocked = true;
            _chatState.Tabs.UpdateChatTabTitle(title);
        }

        // Strip the marker from message text and parts
        var marker = text[startIdx..(endIdx + CominomiConstants.TitleMarkerSuffix.Length)];
        ctx.AssistantMessage.Text = text.Replace(marker, "").TrimStart();

        foreach (var part in ctx.AssistantMessage.Parts)
            if (part.Type == ContentPartType.Text && part.Text != null)
                part.Text = part.Text.Replace(marker, "").TrimStart();
    }

    /// <summary>
    ///     Kept for backward compatibility with existing tests.
    ///     Delegates to <see cref="StreamEventUtils.ExtractToolResultContent" />.
    /// </summary>
    internal static string ExtractToolResultContent(JsonElement content)
    {
        return StreamEventUtils.ExtractToolResultContent(content);
    }
}