using Microsoft.Extensions.Logging.Abstractions;
using Seoro.Shared.Tests.Fakes;

namespace Seoro.Shared.Tests;

public class SessionInitializerTests
{
    private readonly ConfigurableGitService _git = new();
    private readonly SessionInitializer _sut;

    public SessionInitializerTests()
    {
        _sut = new SessionInitializer(_git, NullLogger<SessionInitializer>.Instance);
    }

    [Fact]
    public async Task LoadBranchesAsync_ReturnsGroupsAndDetectedDefaultBranch()
    {
        var groups = new List<BranchGroup> { new("local", ["main", "dev"]) };
        _git.ListAllBranchesGroupedHook = _ => Task.FromResult(groups);
        _git.DetectDefaultBranchHook = _ => Task.FromResult<string?>("main");

        var (resultGroups, defaultBranch) = await _sut.LoadBranchesAsync("/repo");

        Assert.Same(groups, resultGroups);
        Assert.Equal("main", defaultBranch);
    }

    [Fact]
    public async Task LoadBranchesAsync_FetchFailure_StillReturnsCachedBranches()
    {
        _git.FetchAllHook = _ => throw new InvalidOperationException("offline");
        _git.ListAllBranchesGroupedHook = _ => Task.FromResult(
            new List<BranchGroup> { new("local", ["main"]) });
        _git.DetectDefaultBranchHook = _ => Task.FromResult<string?>("main");

        var (groups, defaultBranch) = await _sut.LoadBranchesAsync("/repo");

        Assert.Single(groups);
        Assert.Equal("main", defaultBranch);
    }

    [Fact]
    public async Task LoadBranchesAsync_NullDefaultBranch_FallsBackToFirstBranch()
    {
        _git.ListAllBranchesGroupedHook = _ => Task.FromResult(
            new List<BranchGroup> { new("local", ["feature-x", "feature-y"]) });
        _git.DetectDefaultBranchHook = _ => Task.FromResult<string?>(null);

        var (_, defaultBranch) = await _sut.LoadBranchesAsync("/repo");

        Assert.Equal("feature-x", defaultBranch);
    }

    [Fact]
    public async Task LoadBranchesAsync_NoDefaultAndNoBranches_FallsBackToEmptyString()
    {
        _git.ListAllBranchesGroupedHook = _ => Task.FromResult(new List<BranchGroup>());
        _git.DetectDefaultBranchHook = _ => Task.FromResult<string?>(null);

        var (groups, defaultBranch) = await _sut.LoadBranchesAsync("/repo");

        Assert.Empty(groups);
        Assert.Equal("", defaultBranch);
    }
}
