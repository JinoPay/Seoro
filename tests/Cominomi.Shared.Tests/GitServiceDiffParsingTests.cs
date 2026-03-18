using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class GitServiceDiffParsingTests
{
    [Theory]
    [InlineData("diff --git a/src/file.cs b/src/file.cs", "src/file.cs")]
    [InlineData("diff --git a/README.md b/README.md", "README.md")]
    [InlineData("diff --git a/a b/a", "a")]
    public void ExtractPathFromDiffHeader_NormalPaths(string header, string expected)
    {
        Assert.Equal(expected, GitService.ExtractPathFromDiffHeader(header));
    }

    [Theory]
    [InlineData("diff --git a/src/a b/config.txt b/src/a b/config.txt", "src/a b/config.txt")]
    [InlineData("diff --git a/a b/c b/a b/c", "a b/c")]
    [InlineData("diff --git a/x/a b/y/z b/x/a b/y/z", "x/a b/y/z")]
    public void ExtractPathFromDiffHeader_PathsContainingSpaceB(string header, string expected)
    {
        Assert.Equal(expected, GitService.ExtractPathFromDiffHeader(header));
    }

    [Theory]
    [InlineData("a/src/file.cs b/src/file.cs", "src/file.cs")]
    [InlineData("a/a b/c b/a b/c", "a b/c")]
    public void ExtractPathFromDiffHeader_ShortPrefixFormat(string header, string expected)
    {
        Assert.Equal(expected, GitService.ExtractPathFromDiffHeader(header));
    }

    [Theory]
    [InlineData("diff --git a/old.txt b/new.txt")]  // rename
    [InlineData("not a diff header")]
    [InlineData("")]
    public void ExtractPathFromDiffHeader_ReturnsNull_ForRenamesAndInvalid(string header)
    {
        Assert.Null(GitService.ExtractPathFromDiffHeader(header));
    }

    [Fact]
    public void ParseDiff_PathContainingSpaceB_MatchesCorrectly()
    {
        var nameStatus = "M\tsrc/a b/config.txt";
        var rawDiff = """
            diff --git a/src/a b/config.txt b/src/a b/config.txt
            index abc1234..def5678 100644
            --- a/src/a b/config.txt
            +++ b/src/a b/config.txt
            @@ -1,3 +1,4 @@
             line1
             line2
            +added line
             line3
            """.Replace("            ", "");

        var result = GitService.ParseDiff(nameStatus, rawDiff);

        Assert.Single(result.Files);
        Assert.Equal("src/a b/config.txt", result.Files[0].FilePath);
        Assert.Equal(1, result.Files[0].Additions);
        Assert.Equal(0, result.Files[0].Deletions);
    }

    [Fact]
    public void ParseDiff_RenamedFile_FallsBackToPlusLine()
    {
        var nameStatus = "R100\told.txt\tnew.txt";
        var rawDiff = """
            diff --git a/old.txt b/new.txt
            similarity index 100%
            rename from old.txt
            rename to new.txt
            --- a/old.txt
            +++ b/new.txt
            @@ -1,2 +1,3 @@
             existing
            +added
            """.Replace("            ", "");

        var result = GitService.ParseDiff(nameStatus, rawDiff);

        Assert.Single(result.Files);
        Assert.Equal("new.txt", result.Files[0].FilePath);
        Assert.Equal(1, result.Files[0].Additions);
    }

    [Fact]
    public void ParseDiff_MultipleFiles_ParsesAll()
    {
        var nameStatus = "M\tfile1.cs\nA\tfile2.cs";
        var rawDiff = """
            diff --git a/file1.cs b/file1.cs
            index 1111111..2222222 100644
            --- a/file1.cs
            +++ b/file1.cs
            @@ -1,2 +1,2 @@
            -old line
            +new line
             context
            diff --git a/file2.cs b/file2.cs
            new file mode 100644
            index 0000000..3333333
            --- /dev/null
            +++ b/file2.cs
            @@ -0,0 +1,2 @@
            +line1
            +line2
            """.Replace("            ", "");

        var result = GitService.ParseDiff(nameStatus, rawDiff);

        Assert.Equal(2, result.Files.Count);

        var file1 = result.Files.First(f => f.FilePath == "file1.cs");
        Assert.Equal(1, file1.Additions);
        Assert.Equal(1, file1.Deletions);

        var file2 = result.Files.First(f => f.FilePath == "file2.cs");
        Assert.Equal(2, file2.Additions);
        Assert.Equal(0, file2.Deletions);
    }
}
