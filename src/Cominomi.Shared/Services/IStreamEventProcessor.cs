using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

/// <summary>
///     Mutable context shared across stream event processing for a single message exchange.
/// </summary>
public class StreamProcessingContext
{
    // Plan mode detection
    public bool ExitPlanModeDetected { get; set; }
    public bool PlanReviewVisible { get; set; }
    public bool QuickResponseVisible { get; set; }
    public bool UsageRecorded { get; set; }
    public ChatMessage AssistantMessage { get; set; } = null!;
    public DateTime StreamStartTime { get; set; }
    public Dictionary<int, string> ToolResultBlockMap { get; } = new();
    public int AccCacheCreation { get; set; }
    public int AccCacheRead { get; set; }

    // Token accumulation
    public int AccInputTokens { get; set; }
    public int AccOutputTokens { get; set; }
    public List<string> QuickResponseOptions { get; set; } = [];
    public Session Session { get; set; } = null!;

    /// <summary>
    ///     Raw JSON input from the AskUserQuestion tool call, used to render the bottom bar.
    /// </summary>
    public string? AskUserQuestionInput { get; set; }

    public string? CurrentParentToolUseId { get; set; }

    /// <summary>
    ///     Plan file path detected from Write/Edit tool calls targeting .claude/plans/.
    ///     Used to avoid picking up unrelated plan files from other sessions.
    /// </summary>
    public string? DetectedPlanFilePath { get; set; }

    public string? PlanContent { get; set; }

    // Plan file results (populated by FinalizeAsync)
    public string? PlanFilePath { get; set; }

    // Tool tracking
    public ToolCall? CurrentToolCall { get; set; }
}

public interface IStreamEventProcessor
{
    /// <summary>
    ///     Finalize after the stream loop: record remaining usage, detect plan completion, detect questions.
    /// </summary>
    Task FinalizeAsync(StreamProcessingContext ctx);

    /// <summary>
    ///     Process a single stream event, updating the context accordingly.
    /// </summary>
    Task ProcessEventAsync(StreamEvent evt, StreamProcessingContext ctx);
}