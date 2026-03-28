using Cominomi.Shared;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IClaudeService : IDisposable
{
    IAsyncEnumerable<StreamEvent> SendMessageAsync(
        string message,
        string workingDir,
        string model,
        string permissionMode = CominomiConstants.DefaultPermissionMode,
        string effortLevel = CominomiConstants.DefaultEffortLevel,
        string? sessionId = null,
        string? conversationId = null,
        string? systemPrompt = null,
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
    /// Returns the detected Claude CLI version string, or null if not yet detected.
    /// </summary>
    Task<string?> GetDetectedVersionAsync();

    /// <summary>
    /// Generate a commit message from a unified diff using Haiku.
    /// </summary>
    Task<string?> GenerateCommitMessageAsync(string diff, string workingDir);
}
