
namespace Seoro.Shared.Services.Chat.StreamEventHandlers;

public class ErrorHandler(IChatState chatState) : IStreamEventHandler
{
    public string EventType => "error";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        var errorMsg = evt.GetErrorMessage();
        if (!string.IsNullOrEmpty(errorMsg))
            chatState.AppendText(ctx.AssistantMessage, $"\n\n**Error:** {errorMsg}");

        return Task.CompletedTask;
    }
}