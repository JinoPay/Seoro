using Seoro.Shared.Models.Common;
using Seoro.Shared.Resources;

namespace Seoro.Shared.Tests;

public class AppErrorUserMessageTests
{
    public AppErrorUserMessageTests() => Strings.SetCulture("ko");

    [Theory]
    [InlineData(ErrorCode.WorktreeCreationFailed)]
    [InlineData(ErrorCode.BranchPushRejected)]
    [InlineData(ErrorCode.BranchPushFailed)]
    [InlineData(ErrorCode.BranchRenameFailed)]
    [InlineData(ErrorCode.BranchDeleteFailed)]
    [InlineData(ErrorCode.WorktreeRemoveFailed)]
    [InlineData(ErrorCode.GitCloneFailed)]
    [InlineData(ErrorCode.NotAGitRepo)]
    [InlineData(ErrorCode.StreamingFailed)]
    [InlineData(ErrorCode.ClaudeProcessFailed)]
    [InlineData(ErrorCode.CodexProcessFailed)]
    [InlineData(ErrorCode.CodexSandboxViolation)]
    [InlineData(ErrorCode.SessionNotFound)]
    [InlineData(ErrorCode.WorkspaceNotFound)]
    [InlineData(ErrorCode.SessionFileCorrupted)]
    [InlineData(ErrorCode.ProcessFailed)]
    [InlineData(ErrorCode.HookFailed)]
    public void UserMessage_KnownCode_ReturnsFriendlyText_NotRawAndNotResxKey(ErrorCode code)
    {
        var rawStderr = "fatal: some/raw/git error 12345";
        var err = new AppError(code, ErrorCategory.Permanent, rawStderr);

        var msg = err.UserMessage;

        // 친화 메시지는 원본 stderr 와 달라야 한다.
        Assert.NotEqual(rawStderr, msg);
        // resx 키 자체가 노출되면(Get 폴백) 키와 동일 — 즉 resx 엔트리 누락. 이를 잡는다.
        Assert.NotEqual($"Error_{code}", msg);
        Assert.False(string.IsNullOrWhiteSpace(msg));
    }

    [Fact]
    public void UserMessage_UnknownCode_FallsBackToRawMessage()
    {
        var raw = "unclassified failure detail";
        var err = new AppError(ErrorCode.Unknown, ErrorCategory.Unknown, raw);
        Assert.Equal(raw, err.UserMessage);
    }
}
