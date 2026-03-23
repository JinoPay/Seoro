using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class ProcessErrorClassifierTests
{
    // --- Git ---

    [Fact]
    public void ClassifyGitError_Rejected_ReturnsPushRejected()
    {
        var error = ProcessErrorClassifier.ClassifyGitError("! [rejected] main -> main (non-fast-forward)");
        Assert.Equal(ErrorCode.BranchPushRejected, error.Code);
    }

    [Fact]
    public void ClassifyGitError_NotARepo_ReturnsNotAGitRepo()
    {
        var error = ProcessErrorClassifier.ClassifyGitError("fatal: not a git repository");
        Assert.Equal(ErrorCode.NotAGitRepo, error.Code);
    }

    [Fact]
    public void ClassifyGitError_Unknown_ReturnsFallback()
    {
        var error = ProcessErrorClassifier.ClassifyGitError("some unknown error");
        Assert.Equal(ErrorCode.BranchPushFailed, error.Code);
    }

    // --- Claude ---

    [Fact]
    public void ClassifyClaudeError_RequiresVerbose_ReturnsClaudeProcessFailed()
    {
        var error = ProcessErrorClassifier.ClassifyClaudeError("requires --verbose flag");
        Assert.Equal(ErrorCode.ClaudeProcessFailed, error.Code);
    }

    [Fact]
    public void ClassifyClaudeError_ApiOverloaded_ReturnsStreamingFailed()
    {
        var error = ProcessErrorClassifier.ClassifyClaudeError("API error: service overloaded");
        Assert.Equal(ErrorCode.StreamingFailed, error.Code);
    }

    [Fact]
    public void ClassifyClaudeError_AuthError_ReturnsPermanent()
    {
        var error = ProcessErrorClassifier.ClassifyClaudeError("invalid api key provided");
        Assert.Equal(ErrorCode.ClaudeProcessFailed, error.Code);
        Assert.Equal(ErrorCategory.Permanent, error.Category);
    }

    // --- Push/Merge backward compatibility ---

    [Fact]
    public void ClassifyPushError_Rejected_ReturnsPushRejected()
    {
        var error = ProcessErrorClassifier.ClassifyPushError("! [rejected]");
        Assert.Equal(ErrorCode.BranchPushRejected, error.Code);
    }

    [Fact]
    public void ClassifyPushError_Other_ReturnsPushFailed()
    {
        var error = ProcessErrorClassifier.ClassifyPushError("network error");
        Assert.Equal(ErrorCode.BranchPushFailed, error.Code);
    }

}
