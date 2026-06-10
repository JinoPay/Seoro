using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Chat.StreamEventHandlers;

public class ContentBlockStartHandler(IChatState chatState, IGitBranchWatcherService branchWatcher, ILogger<ContentBlockStartHandler> logger)
    : IStreamEventHandler
{
    public string EventType => "content_block_start";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        // Track parent context for subagent tool calls
        ctx.CurrentParentToolUseId = evt.ParentToolUseId;

        switch (evt.ContentBlock?.Type)
        {
            case "thinking":
                chatState.SetPhase(StreamingPhase.Thinking, sessionId: ctx.Session.Id);
                break;

            case "redacted_thinking":
                chatState.AppendThinking(ctx.AssistantMessage, "[사고 내용 생략됨]");
                break;

            case "server_tool_use":
            case "tool_use":
                ctx.CurrentToolCall = new ToolCall
                {
                    Id = evt.ContentBlock.Id ?? "",
                    Name = evt.ContentBlock.Name ?? "",
                    ParentToolUseId = evt.ParentToolUseId,
                    // Codex는 input을 delta가 아닌 ContentBlock에 바로 제공
                    Input = evt.ContentBlock.Input?.GetRawText() ?? ""
                };
                chatState.AddToolCall(ctx.AssistantMessage, ctx.CurrentToolCall);
                chatState.SetPhase(StreamingPhase.UsingTool, evt.ContentBlock.Name, ctx.Session.Id);
                if (evt.ContentBlock.Name == "ExitPlanMode")
                    ctx.ExitPlanModeDetected = true;
                // Codex: Input이 ContentBlock에 즉시 도착하는 경우 스냅샷 즉시 갱신
                if (TodoSnapshotParser.IsTodoWriteTool(ctx.CurrentToolCall.Name)
                    && TodoSnapshotParser.TryParse(ctx.CurrentToolCall.Input, out var snap))
                {
                    chatState.UpdateTodoSnapshot(ctx.Session.Id, snap);
                }
                else if (TaskListTracker.IsTaskTool(ctx.CurrentToolCall.Name)
                         && !string.IsNullOrEmpty(ctx.CurrentToolCall.Input))
                {
                    var tracker = chatState.GetTaskTracker(ctx.Session.Id);
                    var applied = TaskListTracker.IsTaskCreate(ctx.CurrentToolCall.Name)
                        ? tracker.ApplyTaskCreate(ctx.CurrentToolCall.Id, ctx.CurrentToolCall.Input)
                        : tracker.ApplyTaskUpdate(ctx.CurrentToolCall.Input);
                    if (applied)
                        chatState.UpdateTodoSnapshot(ctx.Session.Id, tracker.ToSnapshot());
                }
                break;

            case "server_tool_result":
            case "tool_result":
                if (evt.Index.HasValue && !string.IsNullOrEmpty(evt.ContentBlock.ToolUseId))
                {
                    ctx.ToolResultBlockMap[evt.Index.Value] = evt.ContentBlock.ToolUseId;
                    var matchingTool =
                        ctx.AssistantMessage.ToolCalls.FirstOrDefault(t => t.Id == evt.ContentBlock.ToolUseId);
                    if (matchingTool != null)
                    {
                        matchingTool.IsError = evt.ContentBlock.IsError ?? false;
                        if (evt.ContentBlock.Content != null)
                            matchingTool.Output =
                                StreamEventUtils.ExtractToolResultContent(evt.ContentBlock.Content.Value);
                        else if (!string.IsNullOrEmpty(evt.ContentBlock.Text))
                            // Codex는 output을 Content가 아닌 ContentBlock.Text에 직접 제공
                            matchingTool.Output = evt.ContentBlock.Text;
                        chatState.NotifyStateChanged();

                        // TaskCreate 결과에서 CLI가 부여한 실제 taskId를 트래커에 바인딩
                        if (matchingTool.IsError != true && TaskListTracker.IsTaskCreate(matchingTool.Name))
                            chatState.GetTaskTracker(ctx.Session.Id)
                                .ApplyTaskCreateResult(matchingTool.Id, matchingTool.Output);

                        // Refresh branch from HEAD file after Bash tool completes
                        // Delay slightly to let git finish writing HEAD file
                        if (matchingTool.Name is "Bash" or "execute_bash")
                        {
                            logger.LogWarning("[TRACE] Bash tool completed, scheduling RefreshBranchFromHeadFile in 150ms for session {SessionId}", ctx.Session.Id);
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(150);
                                logger.LogWarning("[TRACE] RefreshBranchFromHeadFile firing now for session {SessionId}", ctx.Session.Id);
                                branchWatcher.RefreshBranchFromHeadFile(ctx.Session);
                            });
                        }
                    }
                }

                break;
        }

        return Task.CompletedTask;
    }
}