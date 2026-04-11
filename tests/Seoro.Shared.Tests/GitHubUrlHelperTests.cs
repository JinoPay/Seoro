namespace Seoro.Shared.Tests;

/// <summary>
///     <see cref="GitHubUrlHelper"/> 순수 함수 테스트.
///     β 버그 #2(브랜치 매칭)와 마찬가지로 URL 파싱 실수가 GitHub compare URL 오류로 이어질 수 있어
///     대표 케이스를 박제한다.
/// </summary>
public class GitHubUrlHelperTests
{
    [Theory]
    [InlineData("https://github.com/anthropics/claude-code", "anthropics", "claude-code")]
    [InlineData("https://github.com/anthropics/claude-code.git", "anthropics", "claude-code")]
    [InlineData("https://github.com/anthropics/claude-code/", "anthropics", "claude-code")]
    [InlineData("http://github.com/anthropics/claude-code", "anthropics", "claude-code")]
    [InlineData("git@github.com:anthropics/claude-code.git", "anthropics", "claude-code")]
    [InlineData("git@github.com:anthropics/claude-code", "anthropics", "claude-code")]
    [InlineData("ssh://git@github.com/anthropics/claude-code.git", "anthropics", "claude-code")]
    [InlineData("https://github.com/org-name/repo_name.with.dots", "org-name", "repo_name.with.dots")]
    public void TryParseGitHub_Recognized(string url, string owner, string repo)
    {
        var parsed = GitHubUrlHelper.TryParseGitHub(url);
        Assert.NotNull(parsed);
        Assert.Equal(owner, parsed!.Value.Owner);
        Assert.Equal(repo, parsed.Value.Repo);
    }

    [Theory]
    [InlineData("https://gitlab.com/foo/bar")]
    [InlineData("https://bitbucket.org/foo/bar")]
    [InlineData("git@gitlab.com:foo/bar.git")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("https://github.com/onlyone")]
    [InlineData("not-a-url")]
    public void TryParseGitHub_Rejected(string? url)
    {
        var parsed = GitHubUrlHelper.TryParseGitHub(url);
        Assert.Null(parsed);
    }

    [Fact]
    public void BuildRemoteInfo_NoneForEmpty()
    {
        Assert.Same(RemoteInfo.None, GitHubUrlHelper.BuildRemoteInfo(null));
        Assert.Same(RemoteInfo.None, GitHubUrlHelper.BuildRemoteInfo("   "));
    }

    [Fact]
    public void BuildRemoteInfo_GitHubMode()
    {
        var info = GitHubUrlHelper.BuildRemoteInfo("https://github.com/foo/bar.git");
        Assert.Equal(RemoteMode.GitHub, info.Mode);
        Assert.Equal("foo", info.Owner);
        Assert.Equal("bar", info.Repo);
        Assert.Equal("https://github.com/foo/bar.git", info.Url);
    }

    [Fact]
    public void BuildRemoteInfo_OtherForNonGitHub()
    {
        var info = GitHubUrlHelper.BuildRemoteInfo("https://gitlab.com/foo/bar");
        Assert.Equal(RemoteMode.Other, info.Mode);
        Assert.Null(info.Owner);
        Assert.Null(info.Repo);
    }

    [Theory]
    [InlineData("main", "feature/x", "https://github.com/a/b/compare/main...feature/x")]
    [InlineData("refs/heads/main", "refs/remotes/origin/feature/x", "https://github.com/a/b/compare/main...feature/x")]
    [InlineData("origin/main", "feature/x", "https://github.com/a/b/compare/main...feature/x")]
    public void BuildCompareUrl_NormalizesRefs(string baseBranch, string headBranch, string expected)
    {
        var url = GitHubUrlHelper.BuildCompareUrl("a", "b", baseBranch, headBranch);
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("https://user:token@github.com/foo/bar", "https://user:***@github.com/foo/bar")]
    [InlineData("https://user:abc123@gitlab.com/foo", "https://user:***@gitlab.com/foo")]
    [InlineData("https://github.com/foo/bar", "https://github.com/foo/bar")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void MaskCredentials_HidesSecrets(string? input, string expected)
    {
        Assert.Equal(expected, GitHubUrlHelper.MaskCredentials(input));
    }
}
