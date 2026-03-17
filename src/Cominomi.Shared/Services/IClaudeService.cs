using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IClaudeService
{
    IAsyncEnumerable<StreamEvent> SendMessageAsync(
        string message,
        string workingDir,
        string model,
        string permissionMode = "bypassAll",
        string effortLevel = "auto",
        string? sessionId = null,
        string? conversationId = null,
        string? systemPrompt = null,
        string? sessionName = null,
        bool continueMode = false,
        bool forkSession = false,
        int? maxTurns = null,
        decimal? maxBudgetUsd = null,
        List<string>? additionalDirs = null,
        List<string>? allowedTools = null,
        List<string>? disallowedTools = null,
        CancellationToken ct = default);

    void Cancel(string? sessionId = null);

    Task<(bool found, string resolvedPath)> DetectCliAsync();

    /// <summary>
    /// Summarize a user message into a short title using Haiku.
    /// </summary>
    Task<string?> SummarizeAsync(string message, string workingDir);
}
