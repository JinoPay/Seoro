using Cominomi.Shared.Models;
using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cominomi.Shared.Tests;

public class SkillChainTests
{
    private readonly SkillRegistry _registry;
    private readonly Session _session;

    public SkillChainTests()
    {
        _registry = new SkillRegistry(NullLogger<SkillRegistry>.Instance);
        _session = new Session { Id = "test", Git = new GitContext { WorktreePath = "/tmp" } };
    }

    [Fact]
    public void PipeSyntax_TwoSkills_ReturnsTwoSteps()
    {
        var result = _registry.TryParseSkillChain("/commit | /review", _session, out var steps);

        Assert.True(result);
        Assert.Equal(2, steps.Count);
        Assert.Equal("commit", steps[0].SkillName);
        Assert.Equal("review", steps[1].SkillName);
    }

    [Fact]
    public void PipeSyntax_WithArgs_PassesArgsCorrectly()
    {
        var result = _registry.TryParseSkillChain("/commit fix types | /review detailed", _session, out var steps);

        Assert.True(result);
        Assert.Equal(2, steps.Count);
        Assert.Equal("commit", steps[0].SkillName);
        Assert.Equal("fix types", steps[0].Args);
        Assert.Equal("review", steps[1].SkillName);
        Assert.Equal("detailed", steps[1].Args);
    }

    [Fact]
    public void PipeSyntax_ThreeSkills_ReturnsThreeSteps()
    {
        var result = _registry.TryParseSkillChain("/commit | /review | /security-review", _session, out var steps);

        Assert.True(result);
        Assert.Equal(3, steps.Count);
        Assert.Equal("commit", steps[0].SkillName);
        Assert.Equal("review", steps[1].SkillName);
        Assert.Equal("security-review", steps[2].SkillName);
    }

    [Fact]
    public void SingleSkillWithNoChain_ReturnsFalse()
    {
        var result = _registry.TryParseSkillChain("/commit", _session, out var steps);

        Assert.False(result);
        Assert.Empty(steps);
    }

    [Fact]
    public void NonSkillText_ReturnsFalse()
    {
        var result = _registry.TryParseSkillChain("just a message", _session, out var steps);

        Assert.False(result);
        Assert.Empty(steps);
    }

    [Fact]
    public void PipeInRegularText_NotTreatedAsChain()
    {
        var result = _registry.TryParseSkillChain("use grep | sort", _session, out var steps);

        Assert.False(result);
    }

    [Fact]
    public void DefinitionChain_AppendsChainedSkills()
    {
        _registry.Register(new SkillDefinition
        {
            Name = "deploy",
            PromptTemplate = "Deploy the app. {args}",
            IsBuiltIn = false,
            Chain = ["commit", "review"]
        });

        var result = _registry.TryParseSkillChain("/deploy", _session, out var steps);

        Assert.True(result);
        Assert.Equal(3, steps.Count);
        Assert.Equal("deploy", steps[0].SkillName);
        Assert.Equal("commit", steps[1].SkillName);
        Assert.Equal("review", steps[2].SkillName);
    }

    [Fact]
    public void PipeAndDefinitionChain_AreMerged_NoDuplicates()
    {
        _registry.Register(new SkillDefinition
        {
            Name = "deploy",
            PromptTemplate = "Deploy the app. {args}",
            IsBuiltIn = false,
            Chain = ["security-review"]
        });

        var result = _registry.TryParseSkillChain("/deploy | /commit", _session, out var steps);

        Assert.True(result);
        Assert.Equal(3, steps.Count);
        Assert.Equal("deploy", steps[0].SkillName);
        Assert.Equal("commit", steps[1].SkillName);
        Assert.Equal("security-review", steps[2].SkillName);
    }

    [Fact]
    public void PipeAndDefinitionChain_SkipsDuplicateFromDefinition()
    {
        _registry.Register(new SkillDefinition
        {
            Name = "deploy",
            PromptTemplate = "Deploy the app. {args}",
            IsBuiltIn = false,
            Chain = ["commit"]
        });

        var result = _registry.TryParseSkillChain("/deploy | /commit", _session, out var steps);

        Assert.True(result);
        Assert.Equal(2, steps.Count);
        Assert.Equal("deploy", steps[0].SkillName);
        Assert.Equal("commit", steps[1].SkillName);
    }

    [Fact]
    public void ExpandedText_ContainsPromptContent()
    {
        var result = _registry.TryParseSkillChain("/commit | /review", _session, out var steps);

        Assert.True(result);
        Assert.Contains("git diff", steps[0].ExpandedText);
        Assert.Contains("code quality", steps[1].ExpandedText);
    }

    [Fact]
    public void UnknownSkillInPipe_IsSkipped()
    {
        var result = _registry.TryParseSkillChain("/commit | /nonexistent | /review", _session, out var steps);

        Assert.True(result);
        Assert.Equal(2, steps.Count);
        Assert.Equal("commit", steps[0].SkillName);
        Assert.Equal("review", steps[1].SkillName);
    }
}

public class SkillFileStoreChainTests
{
    [Fact]
    public void ParseCommandFile_WithChainFrontmatter_ParsesChain()
    {
        var content = """
            ---
            description: "Deploy workflow"
            chain:
              - commit
              - review
            ---
            Deploy the application. $ARGUMENTS
            """;

        var skill = SkillFileStore.ParseCommandFile("/tmp/deploy.md", content, "user", "/tmp");

        Assert.NotNull(skill);
        Assert.Equal(2, skill!.Chain.Count);
        Assert.Equal("commit", skill.Chain[0]);
        Assert.Equal("review", skill.Chain[1]);
    }

    [Fact]
    public void ParseCommandFile_WithInlineChain_ParsesCorrectly()
    {
        var content = """
            ---
            description: "Deploy workflow"
            chain: [commit, review]
            ---
            Deploy the application.
            """;

        var skill = SkillFileStore.ParseCommandFile("/tmp/deploy.md", content, "user", "/tmp");

        Assert.NotNull(skill);
        Assert.Equal(2, skill!.Chain.Count);
        Assert.Equal("commit", skill.Chain[0]);
        Assert.Equal("review", skill.Chain[1]);
    }

    [Fact]
    public void ParseCommandFile_AllowedToolsAndChain_BothParsedCorrectly()
    {
        var content = """
            ---
            description: "Deploy"
            allowed-tools:
              - bash
              - git
            chain:
              - review
            ---
            Do the thing.
            """;

        var skill = SkillFileStore.ParseCommandFile("/tmp/deploy.md", content, "user", "/tmp");

        Assert.NotNull(skill);
        Assert.Equal(2, skill!.AllowedTools.Count);
        Assert.Contains("bash", skill.AllowedTools);
        Assert.Contains("git", skill.AllowedTools);
        Assert.Single(skill.Chain);
        Assert.Equal("review", skill.Chain[0]);
    }

    [Fact]
    public void ParseCommandFile_NoChain_EmptyChainList()
    {
        var content = """
            ---
            description: "Simple command"
            ---
            Just do the thing.
            """;

        var skill = SkillFileStore.ParseCommandFile("/tmp/simple.md", content, "user", "/tmp");

        Assert.NotNull(skill);
        Assert.Empty(skill!.Chain);
    }
}
