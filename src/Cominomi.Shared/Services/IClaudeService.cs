using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IClaudeService
{
    IAsyncEnumerable<StreamEvent> SendMessageAsync(
        string message,
        string workingDir,
        string model,
        string permissionMode = "bypassAll",
        bool thinkingEnabled = false,
        string? sessionId = null,
        string? conversationId = null,
        string? systemPrompt = null,
        CancellationToken ct = default);

    void Cancel(string? sessionId = null);

    Task<(bool found, string resolvedPath)> DetectCliAsync();
}
