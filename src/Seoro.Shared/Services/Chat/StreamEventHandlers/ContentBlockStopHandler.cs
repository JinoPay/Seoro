
namespace Seoro.Shared.Services.Chat.StreamEventHandlers;

public class ContentBlockStopHandler(IChatState chatState) : IStreamEventHandler
{
    public string EventType => "content_block_stop";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Index.HasValue && ctx.ToolResultBlockMap.Remove(evt.Index.Value))
        {
            // tool_result block finished
        }
        else if (ctx.CurrentToolCall != null)
        {
            ctx.CurrentToolCall.IsComplete = true;
            if (TodoSnapshotParser.IsTodoWriteTool(ctx.CurrentToolCall.Name)
                && TodoSnapshotParser.TryParse(ctx.CurrentToolCall.Input, out var snap))
            {
                chatState.GetTaskTracker(ctx.Session.Id).ResetFromTodoWrite(snap);
                chatState.UpdateTodoSnapshot(ctx.Session.Id, snap);
            }
            else if (TaskListTracker.IsTaskTool(ctx.CurrentToolCall.Name))
            {
                var tracker = chatState.GetTaskTracker(ctx.Session.Id);
                var applied = TaskListTracker.IsTaskCreate(ctx.CurrentToolCall.Name)
                    ? tracker.ApplyTaskCreate(ctx.CurrentToolCall.Id, ctx.CurrentToolCall.Input)
                    : tracker.ApplyTaskUpdate(ctx.CurrentToolCall.Input);
                if (applied)
                    chatState.UpdateTodoSnapshot(ctx.Session.Id, tracker.ToSnapshot());
            }
            ctx.CurrentToolCall = null;
            chatState.NotifyStateChanged();
        }

        chatState.SetPhase(StreamingPhase.Thinking, sessionId: ctx.Session.Id);

        return Task.CompletedTask;
    }
}