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
    public void ClassifyGitError_MergeConflict_ReturnsPrMergeConflict()
    {
        var error = ProcessErrorClassifier.ClassifyGitError("CONFLICT (content): Merge conflict in file.cs");
        Assert.Equal(ErrorCode.PrMergeConflict, error.Code);
    }

    [Fact]
    public void ClassifyGitError_Unknown_ReturnsFallback()
    {
        var error = ProcessErrorClassifier.ClassifyGitError("some unknown error");
        Assert.Equal(ErrorCode.BranchPushFailed, error.Code);
    }

    // --- GitHub ---

    [Theory]
    [InlineData("API rate limit exceeded")]
    [InlineData("secondary rate limit")]
    [InlineData("abuse detection mechanism")]
    public void IsGhRateLimitError_RateLimitTexts_ReturnsTrue(string stderr)
    {
        Assert.True(ProcessErrorClassifier.IsGhRateLimitError(stderr));
    }

    [Fact]
    public void IsGhRateLimitError_EmptyString_ReturnsFalse()
    {
        Assert.False(ProcessErrorClassifier.IsGhRateLimitError(""));
    }

    [Fact]
    public void IsGhRateLimitError_NonRateLimit_ReturnsFalse()
    {
        Assert.False(ProcessErrorClassifier.IsGhRateLimitError("permission denied"));
    }

    [Fact]
    public void ClassifyGhError_NotFound_ReturnsPrNotFound()
    {
        var error = ProcessErrorClassifier.ClassifyGhError("Could not resolve to a PullRequest with the number of 999");
        Assert.Equal(ErrorCode.PrNotFound, error.Code);
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

    [Theory]
    [InlineData("merge conflict detected", null)]
    [InlineData("error", "not mergeable")]
    [InlineData("", "conflicting files found")]
    [InlineData("required status check is failing", null)]
    public void ClassifyMergeError_ConflictPatterns_ReturnsPrConflict(string stderr, string? stdout)
    {
        var error = ProcessErrorClassifier.ClassifyMergeError(stderr, stdout);
        Assert.Equal(ErrorCode.PrMergeConflict, error.Code);
    }

    [Fact]
    public void ClassifyMergeError_Other_ReturnsPrMerge()
    {
        var error = ProcessErrorClassifier.ClassifyMergeError("internal server error");
        Assert.Equal(ErrorCode.PrMergeFailed, error.Code);
    }
}
