using System.Text.Json;

namespace Seoro.Shared.Services.Chat.StreamEventHandlers;

public class UserMessageHandler(
    IChatState chatState,
    IGitBranchWatcherService branchWatcher) : IStreamEventHandler
{
    public string EventType => "user";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Message?.Content == null) return Task.CompletedTask;

        // Track parent context for subagent tool results
        ctx.CurrentParentToolUseId = evt.ParentToolUseId;

        // Capture plan file path from tool_use_result (most reliable source)
        if (ctx.DetectedPlanFilePath == null
            && evt.ExtensionData?.TryGetValue("tool_use_result", out var toolResult) == true
            && toolResult.ValueKind == JsonValueKind.Object
            && toolResult.TryGetProperty("filePath", out var fp))
        {
            var path = fp.GetString();
            if (path != null)
            {
                var normalized = path.Replace('\\', '/');
                if (normalized.Contains(".claude/plans/") && normalized.EndsWith(".md"))
                {
                    ctx.DetectedPlanFilePath = path;
                    ctx.Session.PlanFilePath = path;
                }
            }
        }

        var hasBashResult = false;

        foreach (var block in evt.Message.Content)
            if (block.Type is "tool_result" or "server_tool_result" && !string.IsNullOrEmpty(block.ToolUseId))
            {
                var match = ctx.AssistantMessage.ToolCalls.FirstOrDefault(t => t.Id == block.ToolUseId);
                if (match != null)
                {
                    match.IsError = block.IsError ?? false;
                    if (block.Content.HasValue)
                        match.Output = StreamEventUtils.ExtractToolResultContent(block.Content.Value);
                    match.IsComplete = true;

                    if (match.Name is "Bash" or "execute_bash")
                        hasBashResult = true;
                }
            }

        // Refresh branch after Bash tool completes (git branch -m may have run)
        if (hasBashResult && !ctx.Session.Git.IsLocalDir)
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                branchWatcher.RefreshBranchFromHeadFile(ctx.Session);
            });

        chatState.NotifyStateChanged();

        return Task.CompletedTask;
    }
}