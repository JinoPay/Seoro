namespace Seoro.Shared.Tests;

public class FileTreeBuilderTests
{
    // --- Build ---

    [Fact]
    public void Build_NestedPaths_CreatesTree()
    {
        var tree = FileTreeBuilder.Build(["src/a.cs", "src/sub/b.cs", "README.md"]);

        Assert.Equal(2, tree.Count);
        var src = tree[0];
        Assert.True(src.IsDirectory);
        Assert.Equal("src", src.FullPath);
        Assert.Equal(2, src.Children.Count);
        Assert.Equal("sub", src.Children[0].Name); // 디렉터리 우선
        Assert.Equal("a.cs", src.Children[1].Name);
        Assert.Equal("README.md", tree[1].Name);
    }

    [Fact]
    public void Build_SortsDirectoriesFirstThenCaseInsensitive()
    {
        var tree = FileTreeBuilder.Build(["b.txt", "A.txt", "zdir/x.txt", "adir/y.txt"]);

        Assert.Equal(["adir", "zdir", "A.txt", "b.txt"], tree.Select(n => n.Name).ToList());
    }

    // --- AddPath ---

    [Fact]
    public void AddPath_CreatesIntermediateDirs_AtSortedPosition()
    {
        var tree = FileTreeBuilder.Build(["src/a.cs", "src/c.cs"]);

        FileTreeBuilder.AddPath(tree, "src/b.cs");
        FileTreeBuilder.AddPath(tree, "src/new/deep/file.cs");

        var src = tree.Single(n => n.FullPath == "src");
        Assert.Equal(["new", "a.cs", "b.cs", "c.cs"], src.Children.Select(n => n.Name).ToList());
        var deep = src.Children[0].Children.Single();
        Assert.Equal("src/new/deep", deep.FullPath);
        Assert.Equal("file.cs", deep.Children.Single().Name);
    }

    [Fact]
    public void AddPath_ExistingPath_IsIdempotent()
    {
        var tree = FileTreeBuilder.Build(["src/a.cs"]);

        FileTreeBuilder.AddPath(tree, "src/a.cs");

        var src = tree.Single();
        Assert.Single(src.Children);
    }

    // --- RemovePath ---

    [Fact]
    public void RemovePath_PrunesEmptyDirectories()
    {
        var tree = FileTreeBuilder.Build(["src/sub/only.cs", "src/keep.cs"]);

        FileTreeBuilder.RemovePath(tree, "src/sub/only.cs");

        var src = tree.Single();
        // sub 디렉터리가 비어서 가지치기됨, keep.cs 는 유지
        Assert.Single(src.Children);
        Assert.Equal("keep.cs", src.Children[0].Name);
    }

    [Fact]
    public void RemovePath_LastFileInRoot_PrunesWholeChain()
    {
        var tree = FileTreeBuilder.Build(["a/b/c/file.cs"]);

        FileTreeBuilder.RemovePath(tree, "a/b/c/file.cs");

        Assert.Empty(tree);
    }

    [Fact]
    public void RemovePath_NonExistentPath_IsNoOp()
    {
        var tree = FileTreeBuilder.Build(["src/a.cs"]);

        FileTreeBuilder.RemovePath(tree, "src/missing.cs");
        FileTreeBuilder.RemovePath(tree, "other/x.cs");

        Assert.Single(tree);
        Assert.Single(tree[0].Children);
    }

    // --- ComputeDiff ---

    [Fact]
    public void ComputeDiff_DetectsAddedAndRemoved()
    {
        var old = new HashSet<string> { "a.cs", "b.cs", "renamed-old.cs" };

        var (added, removed) = FileTreeBuilder.ComputeDiff(old, ["a.cs", "b.cs", "renamed-new.cs", "c.cs"]);

        Assert.Equal(["renamed-new.cs", "c.cs"], added);
        Assert.Equal(["renamed-old.cs"], removed);
    }

    [Fact]
    public void ComputeDiff_NoChanges_ReturnsEmpty()
    {
        var old = new HashSet<string> { "a.cs" };

        var (added, removed) = FileTreeBuilder.ComputeDiff(old, ["a.cs"]);

        Assert.Empty(added);
        Assert.Empty(removed);
    }

    // --- AddPath/RemovePath 조합이 Build 와 동등한지 ---

    [Fact]
    public void IncrementalUpdates_ProduceSameTreeAsFullBuild()
    {
        var initial = new List<string> { "src/a.cs", "src/sub/b.cs", "docs/readme.md" };
        var tree = FileTreeBuilder.Build(initial);

        FileTreeBuilder.RemovePath(tree, "src/sub/b.cs");
        FileTreeBuilder.AddPath(tree, "src/sub2/c.cs");
        FileTreeBuilder.AddPath(tree, "top.txt");

        var expected = FileTreeBuilder.Build(["src/a.cs", "src/sub2/c.cs", "docs/readme.md", "top.txt"]);
        AssertTreesEqual(expected, tree);
    }

    private static void AssertTreesEqual(List<FileNode> expected, List<FileNode> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].FullPath, actual[i].FullPath);
            Assert.Equal(expected[i].IsDirectory, actual[i].IsDirectory);
            AssertTreesEqual(expected[i].Children, actual[i].Children);
        }
    }
}
