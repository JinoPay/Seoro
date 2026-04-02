using System.Text.Json;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.StreamEventHandlers;

public class AssistantMessageHandler(IChatState chatState, ISessionService sessionService) : IStreamEventHandler
{
    public string EventType => "assistant";

    public Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx)
    {
        chatState.SetPhase(StreamingPhase.WritingText, sessionId: ctx.Session.Id);

        // Track parent context for subagent tool calls
        ctx.CurrentParentToolUseId = evt.ParentToolUseId;

        if (evt.Message?.Content != null)
            foreach (var block in evt.Message.Content)
                switch (block.Type)
                {
                    case "text" when block.Text != null:
                        chatState.AppendText(ctx.AssistantMessage, block.Text);
                        break;
                    case "thinking" when (block.Thinking ?? block.Text) != null:
                        chatState.AppendThinking(ctx.AssistantMessage, block.Thinking ?? block.Text!);
                        break;
                    case "redacted_thinking":
                        chatState.AppendThinking(ctx.AssistantMessage, "[사고 내용 생략됨]");
                        break;
                    case "server_tool_use":
                    case "tool_use":
                        var tc = new ToolCall
                        {
                            Id = block.Id ?? "",
                            Name = block.Name ?? "",
                            IsComplete = true,
                            ParentToolUseId = evt.ParentToolUseId
                        };
                        if (block.Input.HasValue)
                            tc.Input = block.Input.Value.ValueKind == JsonValueKind.Undefined
                                ? ""
                                : JsonSerializer.Serialize(block.Input.Value,
                                    new JsonSerializerOptions { WriteIndented = true });
                        chatState.AddToolCall(ctx.AssistantMessage, tc);
                        chatState.SetPhase(StreamingPhase.UsingTool, block.Name, ctx.Session.Id);
                        if (block.Name == "ExitPlanMode")
                            ctx.ExitPlanModeDetected = true;
                        break;
                }

        _ = sessionService.SaveSessionAsync(ctx.Session);

        return Task.CompletedTask;
    }
}