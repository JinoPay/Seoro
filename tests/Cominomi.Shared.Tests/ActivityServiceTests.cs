using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class ActivityServiceTests
{
    private static Session CreateSession(string id = "s1", string title = "Test Session") =>
        new()
        {
            Id = id,
            Title = title,
            Git = new GitContext { BranchName = "feature/test", BaseBranch = "main" }
        };

    [Fact]
    public void ParseCommitLine_NulDelimiter_BasicParsing()
    {
        var line = "abc123full\0abc123\0Author Name\02026-03-18T10:00:00+09:00\0feat: add something";
        var session = CreateSession();

        var result = ActivityService.ParseCommitLine(line, session);

        Assert.NotNull(result);
        Assert.Equal("abc123full", result.CommitHash);
        Assert.Equal("abc123", result.ShortHash);
        Assert.Equal("Author Name", result.Author);
        Assert.Equal("feat: add something", result.Message);
        Assert.Equal("s1", result.SessionId);
        Assert.Equal("Test Session", result.SessionTitle);
    }

    [Fact]
    public void ParseCommitLine_MessageWithPipe_ParsesCorrectly()
    {
        var line = "abc123full\0abc123\0Author\02026-03-18T10:00:00+09:00\0fix: handle A | B case";
        var session = CreateSession();

        var result = ActivityService.ParseCommitLine(line, session);

        Assert.NotNull(result);
        Assert.Equal("fix: handle A | B case", result.Message);
    }

    [Fact]
    public void ParseCommitLine_AuthorWithPipe_ParsesCorrectly()
    {
        var line = "abc123full\0abc123\0Author | Corp\02026-03-18T10:00:00+09:00\0some commit";
        var session = CreateSession();

        var result = ActivityService.ParseCommitLine(line, session);

        Assert.NotNull(result);
        Assert.Equal("Author | Corp", result.Author);
        Assert.Equal("some commit", result.Message);
    }

    [Fact]
    public void ParseCommitLine_InsufficientParts_ReturnsNull()
    {
        var line = "abc123full\0abc123\0Author";
        var session = CreateSession();

        var result = ActivityService.ParseCommitLine(line, session);

        Assert.Null(result);
    }

    [Fact]
    public void ParseCommitLine_InvalidTimestamp_ReturnsNull()
    {
        var line = "abc123full\0abc123\0Author\0not-a-date\0some message";
        var session = CreateSession();

        var result = ActivityService.ParseCommitLine(line, session);

        Assert.Null(result);
    }
}
