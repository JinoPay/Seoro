using System.Text.Json.Serialization;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorCode
{
    Unknown,

    // Git operations
    WorktreeCreationFailed,
    BranchPushRejected,
    BranchPushFailed,
    BranchRenameFailed,
    BranchDeleteFailed,
    WorktreeRemoveFailed,
    GitCloneFailed,
    NotAGitRepo,

    // Claude / Streaming
    StreamingFailed,
    ClaudeProcessFailed,

    // Session / Workspace
    SessionNotFound,
    WorkspaceNotFound,
    SessionFileCorrupted,

    // Process
    ProcessFailed,
    HookFailed,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorCategory
{
    Unknown,
    Transient,
    Permanent,
}

public record AppError(
    ErrorCode Code,
    ErrorCategory Category,
    string Message,
    string? Details = null)
{
    // --- Git ---

    public static AppError WorktreeCreation(string message) =>
        new(ErrorCode.WorktreeCreationFailed, ErrorCategory.Permanent, message);

    public static AppError PushRejected(string message) =>
        new(ErrorCode.BranchPushRejected, ErrorCategory.Transient, message);

    public static AppError PushFailed(string message) =>
        new(ErrorCode.BranchPushFailed, ErrorCategory.Transient, message);

    public static AppError CloneFailed(string message) =>
        new(ErrorCode.GitCloneFailed, ErrorCategory.Transient, message);

    public static AppError InvalidGitRepo(string message) =>
        new(ErrorCode.NotAGitRepo, ErrorCategory.Permanent, message);

    // --- Streaming ---

    public static AppError Streaming(string message, string? details = null) =>
        new(ErrorCode.StreamingFailed, ErrorCategory.Transient, message, details);

    // --- General ---

    public static AppError FromException(ErrorCode code, Exception ex) =>
        new(code, ErrorCategory.Unknown, ex.Message, ex.ToString());

    public static AppError General(string message) =>
        new(ErrorCode.Unknown, ErrorCategory.Unknown, message);

    /// <summary>
    /// Classify a git push error as Rejected (force-pushable) or generic failure.
    /// Delegates to <see cref="ProcessErrorClassifier"/>.
    /// </summary>
    public static AppError ClassifyPushError(string errorText)
        => ProcessErrorClassifier.ClassifyPushError(errorText);

}
