using System.Text.Json;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class StreamEventProcessor : IStreamEventProcessor
{
    private readonly IChatState _chatState;
    private readonly ISessionService _sessionService;
    private readonly IUsageService _usageService;
    private readonly ILogger<StreamEventProcessor> _logger;

    public StreamEventProcessor(IChatState chatState, ISessionService sessionService,
        IUsageService usageService, ILogger<StreamEventProcessor> logger)
    {
        _chatState = chatState;
        _sessionService = sessionService;
        _usageService = usageService;
        _logger = logger;
    }

    public async Task ProcessEventAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        var session = ctx.Session;
        var assistantMsg = ctx.AssistantMessage;

        switch (evt.Type)
        {
            case "system" when evt.Subtype == "init":
                if (evt.Model != null)
                    session.ResolvedModel = evt.Model;
                if (!string.IsNullOrEmpty(evt.SessionId))
                    session.ConversationId = evt.SessionId;
                break;

            case "content_block_start" when evt.ContentBlock?.Type == "thinking":
                _chatState.SetPhase(StreamingPhase.Thinking, sessionId: session.Id);
                break;

            case "content_block_start" when evt.ContentBlock?.Type == "redacted_thinking":
                _chatState.AppendThinking(assistantMsg, "[사고 내용 생략됨]");
                break;

            case "content_block_start" when evt.ContentBlock?.Type is "server_tool_use" or "tool_use":
                ctx.CurrentToolCall = new ToolCall
                {
                    Id = evt.ContentBlock.Id ?? "",
                    Name = evt.ContentBlock.Name ?? ""
                };
                _chatState.AddToolCall(assistantMsg, ctx.CurrentToolCall);
                _chatState.SetPhase(StreamingPhase.UsingTool, evt.ContentBlock.Name, session.Id);
                if (evt.ContentBlock.Name == "ExitPlanMode")
                    ctx.ExitPlanModeDetected = true;
                break;

            case "content_block_start" when evt.ContentBlock?.Type is "server_tool_result" or "tool_result":
                if (evt.Index.HasValue && !string.IsNullOrEmpty(evt.ContentBlock.ToolUseId))
                {
                    ctx.ToolResultBlockMap[evt.Index.Value] = evt.ContentBlock.ToolUseId;
                    var matchingTool = assistantMsg.ToolCalls.FirstOrDefault(t => t.Id == evt.ContentBlock.ToolUseId);
                    if (matchingTool != null)
                    {
                        matchingTool.IsError = evt.ContentBlock.IsError ?? false;
                        if (evt.ContentBlock.Content != null)
                            matchingTool.Output = ExtractToolResultContent(evt.ContentBlock.Content.Value);
                        _chatState.NotifyStateChanged();
                    }
                }
                break;

            case "content_block_delta":
                ProcessContentBlockDelta(evt, ctx);
                break;

            case "content_block_stop":
                if (evt.Index.HasValue && ctx.ToolResultBlockMap.Remove(evt.Index.Value))
                {
                    // tool_result block finished
                }
                else if (ctx.CurrentToolCall != null)
                {
                    ctx.CurrentToolCall.IsComplete = true;
                    ctx.CurrentToolCall = null;
                    _chatState.NotifyStateChanged();
                }
                _chatState.SetPhase(StreamingPhase.Thinking, sessionId: session.Id);
                _ = _sessionService.SaveSessionAsync(session);
                break;

            case "assistant":
                _chatState.SetPhase(StreamingPhase.WritingText, sessionId: session.Id);
                ProcessAssistantMessage(evt, ctx);
                _ = _sessionService.SaveSessionAsync(session);
                break;

            case "user":
                ProcessUserMessage(evt, ctx);
                break;

            case "message_start":
                ProcessMessageStart(evt, ctx);
                break;

            case "message_delta":
                ProcessMessageDelta(evt, ctx);
                break;

            case "message_stop":
                break;

            case "result":
                await ProcessResultAsync(evt, ctx);
                break;

            case "error":
            {
                var errorMsg = evt.GetErrorMessage();
                if (!string.IsNullOrEmpty(errorMsg))
                    _chatState.AppendText(assistantMsg, $"\n\n**Error:** {errorMsg}");
                break;
            }

            default:
                _logger.LogDebug("Unhandled Claude event type: {Type}", evt.Type);
                break;
        }
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
            await RecordUsageAsync(ctx.Session, fallbackUsage);
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
                DetectPlanFile(ctx);
                if (ctx.PlanContent != null)
                    ctx.ExitPlanModeDetected = true;
            }

            if (ctx.ExitPlanModeDetected)
            {
                if (ctx.PlanContent == null)
                    DetectPlanFile(ctx);
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

    private void ProcessContentBlockDelta(StreamEvent evt, StreamProcessingContext ctx)
    {
        var assistantMsg = ctx.AssistantMessage;

        if (evt.Index.HasValue && ctx.ToolResultBlockMap.TryGetValue(evt.Index.Value, out var resultToolId))
        {
            var tool = assistantMsg.ToolCalls.FirstOrDefault(t => t.Id == resultToolId);
            if (tool != null && evt.Delta?.Text != null)
            {
                tool.Output += evt.Delta.Text;
                _chatState.NotifyStateChanged();
            }
        }
        else if (evt.Delta?.Type == "text_delta" && evt.Delta.Text != null)
        {
            _chatState.AppendText(assistantMsg, evt.Delta.Text);
            _chatState.SetPhase(StreamingPhase.WritingText, sessionId: ctx.Session.Id);
        }
        else if (evt.Delta?.Type == "thinking_delta" && evt.Delta.Thinking != null)
        {
            _chatState.AppendThinking(assistantMsg, evt.Delta.Thinking);
            _chatState.SetPhase(StreamingPhase.Thinking, sessionId: ctx.Session.Id);
        }
        else if (evt.Delta?.Type == "input_json_delta" && evt.Delta.PartialJson != null && ctx.CurrentToolCall != null)
        {
            ctx.CurrentToolCall.Input += evt.Delta.PartialJson;
        }
    }

    private void ProcessAssistantMessage(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Message?.Content == null) return;

        foreach (var block in evt.Message.Content)
        {
            switch (block.Type)
            {
                case "text" when block.Text != null:
                    _chatState.AppendText(ctx.AssistantMessage, block.Text);
                    break;
                case "thinking" when (block.Thinking ?? block.Text) != null:
                    _chatState.AppendThinking(ctx.AssistantMessage, block.Thinking ?? block.Text!);
                    break;
                case "redacted_thinking":
                    _chatState.AppendThinking(ctx.AssistantMessage, "[사고 내용 생략됨]");
                    break;
                case "server_tool_use":
                case "tool_use":
                    var tc = new ToolCall
                    {
                        Id = block.Id ?? "",
                        Name = block.Name ?? "",
                        IsComplete = true
                    };
                    if (block.Input.HasValue)
                    {
                        tc.Input = block.Input.Value.ValueKind == JsonValueKind.Undefined
                            ? ""
                            : JsonSerializer.Serialize(block.Input.Value,
                                new JsonSerializerOptions { WriteIndented = true });
                    }
                    _chatState.AddToolCall(ctx.AssistantMessage, tc);
                    _chatState.SetPhase(StreamingPhase.UsingTool, block.Name, ctx.Session.Id);
                    if (block.Name == "ExitPlanMode")
                        ctx.ExitPlanModeDetected = true;
                    break;
            }
        }
    }

    private void ProcessUserMessage(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Message?.Content == null) return;

        foreach (var block in evt.Message.Content)
        {
            if (block.Type is "tool_result" or "server_tool_result" && !string.IsNullOrEmpty(block.ToolUseId))
            {
                var match = ctx.AssistantMessage.ToolCalls.FirstOrDefault(t => t.Id == block.ToolUseId);
                if (match != null)
                {
                    match.IsError = block.IsError ?? false;
                    if (block.Content.HasValue)
                        match.Output = ExtractToolResultContent(block.Content.Value);
                    match.IsComplete = true;
                }
            }
        }
        _chatState.NotifyStateChanged();
    }

    private void ProcessMessageStart(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Message?.Model is string startModel && !string.IsNullOrEmpty(startModel))
            ctx.Session.ResolvedModel = startModel;
        ctx.UsageRecorded = false;
        if (evt.Message?.Usage is { } startUsage)
        {
            ctx.AccInputTokens = startUsage.InputTokens;
            ctx.AccCacheCreation = startUsage.CacheCreationInputTokens ?? 0;
            ctx.AccCacheRead = startUsage.CacheReadInputTokens ?? 0;
            ctx.AccOutputTokens = 0;
        }
    }

    private void ProcessMessageDelta(StreamEvent evt, StreamProcessingContext ctx)
    {
        var deltaUsage = evt.Usage ?? evt.Message?.Usage;
        if (deltaUsage != null)
        {
            if (deltaUsage.InputTokens > 0) ctx.AccInputTokens = deltaUsage.InputTokens;
            if (deltaUsage.OutputTokens > 0) ctx.AccOutputTokens = deltaUsage.OutputTokens;
            if (deltaUsage.CacheCreationInputTokens is > 0) ctx.AccCacheCreation = deltaUsage.CacheCreationInputTokens.Value;
            if (deltaUsage.CacheReadInputTokens is > 0) ctx.AccCacheRead = deltaUsage.CacheReadInputTokens.Value;
        }

        var stopReason = evt.Delta?.StopReason ?? evt.Message?.StopReason;
        if (!string.IsNullOrEmpty(stopReason) && stopReason == "max_tokens")
        {
            _chatState.AppendText(ctx.AssistantMessage, "\n\n⚠️ *응답이 최대 토큰 한도에 도달하여 잘렸습니다.*");
        }
    }

    private async Task ProcessResultAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        var session = ctx.Session;

        if (string.IsNullOrEmpty(session.ConversationId) && !string.IsNullOrEmpty(evt.SessionId))
            session.ConversationId = evt.SessionId;

        var resultUsage = evt.Usage ?? evt.Message?.Usage ?? TryExtractUsageFromExtensionData(evt);
        var costOverride = TryExtractCost(evt);

        _logger.LogDebug("Result event: Usage={HasUsage}, Cost={Cost}, AccIn={AccIn}, AccOut={AccOut}",
            evt.Usage != null, costOverride, ctx.AccInputTokens, ctx.AccOutputTokens);

        if (resultUsage != null && !ctx.UsageRecorded)
        {
            session.TotalInputTokens += resultUsage.InputTokens;
            session.TotalOutputTokens += resultUsage.OutputTokens;
            await RecordUsageAsync(session, resultUsage, costOverride);
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
            await RecordUsageAsync(session, accUsage, costOverride);
            ctx.UsageRecorded = true;
            _logger.LogWarning("Usage recorded from accumulated deltas. In={In}, Out={Out}",
                ctx.AccInputTokens, ctx.AccOutputTokens);
        }
        else if (!ctx.UsageRecorded && costOverride.HasValue && costOverride > 0)
        {
            var costOnlyUsage = new UsageInfo { InputTokens = 0, OutputTokens = 0 };
            await RecordUsageAsync(session, costOnlyUsage, costOverride);
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

    private async Task RecordUsageAsync(Session session, UsageInfo usage, decimal? costOverride = null)
    {
        try
        {
            var model = session.ResolvedModel ?? session.Model ?? "unknown";
            var entry = new UsageEntry
            {
                Timestamp = DateTime.UtcNow,
                Model = model,
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                CacheCreationTokens = usage.CacheCreationInputTokens ?? 0,
                CacheReadTokens = usage.CacheReadInputTokens ?? 0,
                SessionId = session.Id,
                ProjectPath = session.Git.WorktreePath
            };
            entry.CostUsd = costOverride ?? _usageService.CalculateCost(
                model, entry.InputTokens, entry.OutputTokens,
                entry.CacheCreationTokens, entry.CacheReadTokens);

            _logger.LogInformation("Recording usage: Model={Model}, In={In}, Out={Out}, Cost=${Cost:F6}",
                model, entry.InputTokens, entry.OutputTokens, entry.CostUsd);

            await _usageService.RecordUsageAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record usage for session {SessionId}", session.Id);
        }
    }

    private void DetectPlanFile(StreamProcessingContext ctx)
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
            ctx.PlanContent = File.ReadAllText(planFile);
        }
    }

    private static decimal? TryExtractCost(StreamEvent evt)
    {
        if (evt.CostUsd is > 0) return evt.CostUsd.Value;
        if (evt.TotalCostUsd is > 0) return evt.TotalCostUsd.Value;

        if (evt.ExtensionData != null)
        {
            if (evt.ExtensionData.TryGetValue("cost_usd", out var costEl) && costEl.TryGetDecimal(out var cost) && cost > 0)
                return cost;
            if (evt.ExtensionData.TryGetValue("total_cost_usd", out var tcEl) && tcEl.TryGetDecimal(out var tc) && tc > 0)
                return tc;
        }
        return null;
    }

    private static UsageInfo? TryExtractUsageFromExtensionData(StreamEvent evt)
    {
        if (evt.ExtensionData == null) return null;

        int input = 0, output = 0;
        int? cacheCreation = null, cacheRead = null;

        if (evt.ExtensionData.TryGetValue("input_tokens", out var inEl))
            inEl.TryGetInt32(out input);
        if (evt.ExtensionData.TryGetValue("output_tokens", out var outEl))
            outEl.TryGetInt32(out output);
        if (evt.ExtensionData.TryGetValue("cache_creation_input_tokens", out var cwEl) && cwEl.TryGetInt32(out var cw))
            cacheCreation = cw;
        if (evt.ExtensionData.TryGetValue("cache_read_input_tokens", out var crEl) && crEl.TryGetInt32(out var cr))
            cacheRead = cr;

        if (input == 0 && output == 0
            && evt.ExtensionData.TryGetValue("usage", out var usageEl)
            && usageEl.ValueKind == JsonValueKind.Object)
        {
            if (usageEl.TryGetProperty("input_tokens", out var inProp))
                inProp.TryGetInt32(out input);
            if (usageEl.TryGetProperty("output_tokens", out var outProp))
                outProp.TryGetInt32(out output);
            if (usageEl.TryGetProperty("cache_creation_input_tokens", out var cwProp) && cwProp.TryGetInt32(out var cwVal))
                cacheCreation = cwVal;
            if (usageEl.TryGetProperty("cache_read_input_tokens", out var crProp) && crProp.TryGetInt32(out var crVal))
                cacheRead = crVal;
        }

        if (input == 0 && output == 0) return null;

        return new UsageInfo
        {
            InputTokens = input,
            OutputTokens = output,
            CacheCreationInputTokens = cacheCreation,
            CacheReadInputTokens = cacheRead,
        };
    }

    internal static string ExtractToolResultContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";

        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                    parts.Add(textProp.GetString() ?? "");
            }
            return string.Join("\n", parts);
        }

        return content.ToString();
    }
}
