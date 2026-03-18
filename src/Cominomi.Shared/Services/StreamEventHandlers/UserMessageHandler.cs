using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class UserMessageHandler : IStreamEventHandler
{
    private readonly IChatState _chatState;

    public UserMessageHandler(IChatState chatState) => _chatState = chatState;

    public string EventType => "user";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        if (evt.Message?.Content == null) return Task.CompletedTask;

        foreach (var block in evt.Message.Content)
        {
            if (block.Type is "tool_result" or "server_tool_result" && !string.IsNullOrEmpty(block.ToolUseId))
            {
                var match = ctx.AssistantMessage.ToolCalls.FirstOrDefault(t => t.Id == block.ToolUseId);
                if (match != null)
                {
                    match.IsError = block.IsError ?? false;
                    if (block.Content.HasValue)
                        match.Output = StreamEventUtils.ExtractToolResultContent(block.Content.Value);
                    match.IsComplete = true;
                }
            }
        }

        _chatState.NotifyStateChanged();

        return Task.CompletedTask;
    }
}
