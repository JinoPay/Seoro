using System.Text.Json.Serialization;
using Seoro.Shared.Resources;
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

    // Codex
    CodexProcessFailed,
    CodexSandboxViolation,

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
    ///     사용자에게 보여줄 친화적·실행 가능한 메시지. <see cref="Code" />에 따라 현지화된
    ///     설명을 반환하며, 분류되지 않은 경우(<see cref="ErrorCode.Unknown" />)에만 원본
    ///     <see cref="Message" />(보통 stderr)로 폴백한다. 원본 텍스트는 로그·툴팁용으로
    ///     <see cref="Message" />/<see cref="Details" />에 그대로 보존된다.
    /// </summary>
    [JsonIgnore]
    public string UserMessage => Code switch
    {
        ErrorCode.WorktreeCreationFailed => Strings.Error_WorktreeCreationFailed,
        ErrorCode.BranchPushRejected => Strings.Error_BranchPushRejected,
        ErrorCode.BranchPushFailed => Strings.Error_BranchPushFailed,
        ErrorCode.BranchRenameFailed => Strings.Error_BranchRenameFailed,
        ErrorCode.BranchDeleteFailed => Strings.Error_BranchDeleteFailed,
        ErrorCode.WorktreeRemoveFailed => Strings.Error_WorktreeRemoveFailed,
        ErrorCode.GitCloneFailed => Strings.Error_GitCloneFailed,
        ErrorCode.NotAGitRepo => Strings.Error_NotAGitRepo,
        ErrorCode.StreamingFailed => Strings.Error_StreamingFailed,
        ErrorCode.ClaudeProcessFailed => Strings.Error_ClaudeProcessFailed,
        ErrorCode.CodexProcessFailed => Strings.Error_CodexProcessFailed,
        ErrorCode.CodexSandboxViolation => Strings.Error_CodexSandboxViolation,
        ErrorCode.SessionNotFound => Strings.Error_SessionNotFound,
        ErrorCode.WorkspaceNotFound => Strings.Error_WorkspaceNotFound,
        ErrorCode.SessionFileCorrupted => Strings.Error_SessionFileCorrupted,
        ErrorCode.ProcessFailed => Strings.Error_ProcessFailed,
        ErrorCode.HookFailed => Strings.Error_HookFailed,
        _ => Message
    };


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