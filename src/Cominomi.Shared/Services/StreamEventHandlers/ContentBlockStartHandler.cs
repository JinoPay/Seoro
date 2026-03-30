using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class ContentBlockStartHandler : IStreamEventHandler
{
    private readonly IChatState _chatState;
    private readonly IGitBranchWatcherService _branchWatcher;

    public ContentBlockStartHandler(IChatState chatState, IGitBranchWatcherService branchWatcher)
    {
        _chatState = chatState;
        _branchWatcher = branchWatcher;
    }

    public string EventType => "content_block_start";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        // Track parent context for subagent tool calls
        ctx.CurrentParentToolUseId = evt.ParentToolUseId;

        switch (evt.ContentBlock?.Type)
        {
            case "thinking":
                _chatState.SetPhase(StreamingPhase.Thinking, sessionId: ctx.Session.Id);
                break;

            case "redacted_thinking":
                _chatState.AppendThinking(ctx.AssistantMessage, "[사고 내용 생략됨]");
                break;

            case "server_tool_use":
            case "tool_use":
                ctx.CurrentToolCall = new ToolCall
                {
                    Id = evt.ContentBlock.Id ?? "",
                    Name = evt.ContentBlock.Name ?? "",
                    ParentToolUseId = evt.ParentToolUseId
                };
                _chatState.AddToolCall(ctx.AssistantMessage, ctx.CurrentToolCall);
                _chatState.SetPhase(StreamingPhase.UsingTool, evt.ContentBlock.Name, ctx.Session.Id);
                if (evt.ContentBlock.Name == "ExitPlanMode")
                    ctx.ExitPlanModeDetected = true;
                break;

            case "server_tool_result":
            case "tool_result":
                if (evt.Index.HasValue && !string.IsNullOrEmpty(evt.ContentBlock.ToolUseId))
                {
                    ctx.ToolResultBlockMap[evt.Index.Value] = evt.ContentBlock.ToolUseId;
                    var matchingTool = ctx.AssistantMessage.ToolCalls.FirstOrDefault(t => t.Id == evt.ContentBlock.ToolUseId);
                    if (matchingTool != null)
                    {
                        matchingTool.IsError = evt.ContentBlock.IsError ?? false;
                        if (evt.ContentBlock.Content != null)
                            matchingTool.Output = StreamEventUtils.ExtractToolResultContent(evt.ContentBlock.Content.Value);
                        _chatState.NotifyStateChanged();

                        // Refresh branch from HEAD file after Bash tool completes
                        if (matchingTool.Name is "Bash" or "execute_bash")
                            _branchWatcher.RefreshBranchFromHeadFile(ctx.Session);
                    }
                }
                break;
        }

        return Task.CompletedTask;
    }
}
