namespace Seoro.Shared.Tests;

/// <summary>
///     <see cref="BranchRefNormalizer"/> 테스트.
///     β 버그 #2("origin/main"↔"main" mismatch → detached HEAD 머지 위험) 재발 방지를 위해
///     대표 케이스를 박제한다.
/// </summary>
public class BranchRefNormalizerTests
{
    [Theory]
    [InlineData("main", "main")]
    [InlineData("origin/main", "main")]
    [InlineData("refs/heads/main", "main")]
    [InlineData("refs/remotes/origin/main", "main")]
    [InlineData("refs/remotes/upstream/main", "main")]
    [InlineData("feature/x", "feature/x")]
    [InlineData("origin/feature/x", "feature/x")]
    [InlineData("refs/heads/feature/x", "feature/x")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_StripsPrefixes(string? input, string expected)
    {
        Assert.Equal(expected, BranchRefNormalizer.Normalize(input));
    }

    [Fact]
    public void MatchInGroups_PrefersLocalBranch()
    {
        var groups = new List<BranchGroup>
        {
            new("origin", ["origin/main", "origin/develop"]),
            new("로컬", ["main", "feature/x"])
        };
        // "origin/main" 저장 → 로컬의 "main" 반환 (detached HEAD 방지 핵심)
        Assert.Equal("main", BranchRefNormalizer.MatchInGroups("origin/main", groups));
    }

    [Fact]
    public void MatchInGroups_FallsBackToOriginWhenNoLocal()
    {
        var groups = new List<BranchGroup>
        {
            new("origin", ["origin/main", "origin/release"]),
            new("로컬", ["develop"])
        };
        // 로컬에 release 가 없으니 origin 그룹에서 매칭
        Assert.Equal("origin/release", BranchRefNormalizer.MatchInGroups("release", groups));
    }

    [Fact]
    public void MatchInGroups_ReturnsNullForUnknown()
    {
        var groups = new List<BranchGroup>
        {
            new("origin", ["origin/main"]),
            new("로컬", ["main"])
        };
        Assert.Null(BranchRefNormalizer.MatchInGroups("totally-unknown", groups));
    }

    [Fact]
    public void MatchInGroups_HandlesRefsHeadsInput()
    {
        var groups = new List<BranchGroup>
        {
            new("origin", ["origin/main"]),
            new("로컬", ["main"])
        };
        // refs/heads/main 형태의 저장값도 main 으로 매칭되어야 한다.
        Assert.Equal("main", BranchRefNormalizer.MatchInGroups("refs/heads/main", groups));
    }

    [Fact]
    public void MatchInGroups_HandlesNestedBranchNames()
    {
        var groups = new List<BranchGroup>
        {
            new("origin", ["origin/feature/auth", "origin/feature/ui"]),
            new("로컬", ["feature/auth"])
        };
        Assert.Equal("feature/auth", BranchRefNormalizer.MatchInGroups("origin/feature/auth", groups));
    }

    [Fact]
    public void MatchInGroups_EmptyGroupsReturnsNull()
    {
        Assert.Null(BranchRefNormalizer.MatchInGroups("main", []));
        Assert.Null(BranchRefNormalizer.MatchInGroups(null, []));
    }
}
