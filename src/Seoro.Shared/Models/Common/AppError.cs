using System.Text.Json.Serialization;
using Seoro.Shared.Services;

namespace Seoro.Shared.Models.Common;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorCode
{
    Unknown,

    // Git 작업
    WorktreeCreationFailed,
    BranchPushRejected,
    BranchPushFailed,
    BranchRenameFailed,
    BranchDeleteFailed,
    WorktreeRemoveFailed,
    GitCloneFailed,
    NotAGitRepo,

    // Claude / 스트리밍
    StreamingFailed,
    ClaudeProcessFailed,

    // 세션 / 워크스페이스
    SessionNotFound,
    WorkspaceNotFound,
    SessionFileCorrupted,

    // 프로세스
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
    ///     git push 오류를 거부됨(강제 푸시 가능) 또는 일반 실패로 분류합니다.
    ///     <see cref="ProcessErrorClassifier" />에 위임됩니다.
    /// </summary>
    public static AppError ClassifyPushError(string errorText)
    {
        return ProcessErrorClassifier.ClassifyPushError(errorText);
    }

    public static AppError CloneFailed(string message)
    {
        return new AppError(ErrorCode.GitCloneFailed, ErrorCategory.Transient, message);
    }

    // --- 일반 ---

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

    // --- 스트리밍 ---

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