using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class ErrorHandler : IStreamEventHandler
{
    private readonly IChatState _chatState;

    public ErrorHandler(IChatState chatState) => _chatState = chatState;

    public string EventType => "error";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        var errorMsg = evt.GetErrorMessage();
        if (!string.IsNullOrEmpty(errorMsg))
            _chatState.AppendText(ctx.AssistantMessage, $"\n\n**Error:** {errorMsg}");

        return Task.CompletedTask;
    }
}
