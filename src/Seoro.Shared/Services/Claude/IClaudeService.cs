
namespace Seoro.Shared.Services.Claude;

public interface IClaudeService : IDisposable
{
    IAsyncEnumerable<StreamEvent> SendMessageAsync(
        string message,
        string workingDir,
        string model,
        string permissionMode = SeoroConstants.DefaultPermissionMode,
        string effortLevel = SeoroConstants.DefaultEffortLevel,
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

    Task<(bool found, string resolvedPath)> DetectCliAsync();

    /// <summary>
    ///     Returns the detected Claude CLI version string, or null if not yet detected.
    /// </summary>
    Task<string?> GetDetectedVersionAsync();

    void Cancel(string? sessionId = null);
}