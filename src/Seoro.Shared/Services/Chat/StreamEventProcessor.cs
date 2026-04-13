using System.Text.Json;
using Seoro.Shared.Services.Chat.StreamEventHandlers;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Chat;

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
            _logger.LogDebug("세션 {SessionId}: 사용량 {InputTokens}in/{OutputTokens}out 토큰",
                ctx.Session.Id, ctx.AccInputTokens, ctx.AccOutputTokens);
        }

        ctx.Session.PendingInputTokens = 0;
        ctx.Session.PendingOutputTokens = 0;

        // Detect plan completion
        if (ctx.Session.PermissionMode == "plan")
        {
            if (ctx.Session.IsCodex)
                await FinalizeCodexPlanAsync(ctx);
            else
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
                    ctx.Session.PlanContent = ctx.PlanContent;
                    ctx.PlanReviewVisible = true;
                    _logger.LogInformation("세션 {SessionId}의 플랜 완료, 플랜 파일: {PlanFile}",
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
            _logger.LogDebug("처리되지 않은 Claude 이벤트 타입: {Type}", evt.Type);
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

        // Fallback: scan worktree-local plan directories for recent plan files
        var candidates = new List<string>();

        if (!string.IsNullOrEmpty(ctx.Session.Git.WorktreePath))
        {
            var worktreePlanDirs = new[]
            {
                Path.Combine(ctx.Session.Git.WorktreePath, ".claude", "plans"),
                Path.Combine(ctx.Session.Git.WorktreePath, ".context", "plans")
            };

            foreach (var dir in worktreePlanDirs)
                if (Directory.Exists(dir))
                    candidates.AddRange(Directory.GetFiles(dir, "*.md"));
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
            _logger.LogDebug("파일시스템 스캔에서 플랜 파일 확인됨: {PlanFilePath}", planFile);
        }
    }

    /// <summary>
    ///     Scans the assistant message's tool calls for Write/Edit operations targeting worktree-local
    ///     plan files to precisely identify which plan file belongs to this session.
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
                        if (IsPlanFilePath(normalized))
                        {
                            ctx.DetectedPlanFilePath = path;
                            _logger.LogDebug("도구 호출에서 플랜 파일 감지됨: {PlanFilePath}", path);
                            return;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "플랜 감지를 위한 도구 호출 입력 파싱 실패");
            }
        }
    }

    private async Task FinalizeCodexPlanAsync(StreamProcessingContext ctx)
    {
        // AskUserQuestion takes priority over plan review
        if (HasAskUserQuestionToolCall(ctx))
        {
            ctx.PlanReviewVisible = false;
            ctx.Session.PlanCompleted = false;
            ctx.Session.PendingAskUserQuestionInput = ctx.AskUserQuestionInput;
            ctx.QuickResponseVisible = false;
            ctx.QuickResponseOptions = [];
            return;
        }

        if (ctx.DetectedPlanFilePath == null)
            DetectPlanFileFromToolCalls(ctx);

        if (ctx.DetectedPlanFilePath != null)
            ctx.Session.PlanFilePath = ctx.DetectedPlanFilePath;

        if (ctx.DetectedPlanFilePath == null && ctx.Session.PlanFilePath != null
                                             && File.Exists(ctx.Session.PlanFilePath))
            ctx.DetectedPlanFilePath = ctx.Session.PlanFilePath;

        if (ctx.PlanContent == null)
            await DetectPlanFileAsync(ctx);

        if (ctx.PlanContent == null && !string.IsNullOrWhiteSpace(ctx.AssistantMessage.Text))
            ctx.PlanContent = ctx.AssistantMessage.Text;

        if (string.IsNullOrWhiteSpace(ctx.PlanContent))
        {
            ctx.PlanReviewVisible = false;
            ctx.QuickResponseVisible = false;
            ctx.QuickResponseOptions = [];
            return;
        }

        ctx.PlanFilePath ??= ctx.DetectedPlanFilePath;
        ctx.Session.PlanCompleted = true;
        ctx.Session.PlanFilePath = ctx.PlanFilePath;
        ctx.Session.PlanContent = ctx.PlanContent;
        ctx.PlanReviewVisible = true;
        ctx.QuickResponseVisible = false;
        ctx.QuickResponseOptions = [];
    }

    private static bool IsPlanFilePath(string normalizedPath)
    {
        return normalizedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
               && (normalizedPath.Contains(".claude/plans/", StringComparison.OrdinalIgnoreCase)
                   || normalizedPath.Contains(".context/plans/", StringComparison.OrdinalIgnoreCase));
    }

    private void ExtractAndApplyTitleMarker(StreamProcessingContext ctx)
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
        var marker = text[startIdx..(endIdx + SeoroConstants.TitleMarkerSuffix.Length)];
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
