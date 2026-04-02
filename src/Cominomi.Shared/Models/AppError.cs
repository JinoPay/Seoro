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
    HookFailed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorCategory
{
    Unknown,
    Transient,
    Permanent
}

public record AppError(
    ErrorCode Code,
    ErrorCategory Category,
    string Message,
    string? Details = null)
{
    /// <summary>
    ///     Classify a git push error as Rejected (force-pushable) or generic failure.
    ///     Delegates to <see cref="ProcessErrorClassifier" />.
    /// </summary>
    public static AppError ClassifyPushError(string errorText)
    {
        return ProcessErrorClassifier.ClassifyPushError(errorText);
    }

    public static AppError CloneFailed(string message)
    {
        return new AppError(ErrorCode.GitCloneFailed, ErrorCategory.Transient, message);
    }

    // --- General ---

    public static AppError FromException(ErrorCode code, Exception ex)
    {
        return new AppError(code, ErrorCategory.Unknown, ex.Message, ex.ToString());
    }

    public static AppError General(string message)
    {
        return new AppError(ErrorCode.Unknown, ErrorCategory.Unknown, message);
    }

    public static AppError InvalidGitRepo(string message)
    {
        return new AppError(ErrorCode.NotAGitRepo, ErrorCategory.Permanent, message);
    }

    public static AppError PushFailed(string message)
    {
        return new AppError(ErrorCode.BranchPushFailed, ErrorCategory.Transient, message);
    }

    public static AppError PushRejected(string message)
    {
        return new AppError(ErrorCode.BranchPushRejected, ErrorCategory.Transient, message);
    }

    // --- Streaming ---

    public static AppError Streaming(string message, string? details = null)
    {
        return new AppError(ErrorCode.StreamingFailed, ErrorCategory.Transient, message, details);
    }
    // --- Git ---

    public static AppError WorktreeCreation(string message)
    {
        return new AppError(ErrorCode.WorktreeCreationFailed, ErrorCategory.Permanent, message);
    }
}