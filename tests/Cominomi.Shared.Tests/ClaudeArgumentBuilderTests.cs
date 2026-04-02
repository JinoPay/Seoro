using Cominomi.Shared;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class ClaudeArgumentBuilderTests
{
    private static CliCapabilities DefaultCaps(bool verbose = false) => new()
    {
        Version = "1.0.0",
        SupportsVerbose = verbose
    };

    [Fact]
    public void Build_BasicArgs_IncludesStreamJsonAndModel()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps());

        Assert.Contains("--print", result);
        Assert.Contains("--output-format stream-json", result);
        Assert.Contains("--model \"sonnet\"", result);
    }

    [Fact]
    public void Build_WithVerbose_IncludesVerboseFlag()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(verbose: true));
        Assert.Contains("--verbose", result);
    }

    [Fact]
    public void Build_WithoutVerbose_OmitsVerboseFlag()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(verbose: false));
        Assert.DoesNotContain("--verbose", result);
    }

    [Theory]
    [InlineData("plan", "--permission-mode plan")]
    [InlineData("acceptEdits", "--permission-mode acceptEdits")]
    [InlineData("dontAsk", "--permission-mode dontAsk")]
    [InlineData("bypassPermissions", "--permission-mode bypassPermissions")]
    [InlineData("bypassAll", "--dangerously-skip-permissions")]
    public void Build_PermissionModes_GeneratesCorrectFlag(string mode, string expected)
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", mode, DefaultCaps());
        Assert.Contains(expected, result);
    }

    [Fact]
    public void Build_DefaultPermission_NoPermissionFlag()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps());
        Assert.DoesNotContain("--permission-mode", result);
        Assert.DoesNotContain("--dangerously-skip-permissions", result);
    }

    [Theory]
    [InlineData("low", "--effort low")]
    [InlineData("high", "--effort high")]
    public void Build_EffortLevel_IncludesFlag(string level, string expected)
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(), effortLevel: level);
        Assert.Contains(expected, result);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("")]
    public void Build_DefaultEffort_NoEffortFlag(string level)
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(), effortLevel: level);
        Assert.DoesNotContain("--effort", result);
    }

    [Fact]
    public void Build_WithConversationId_IncludesResumeFlag()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(), conversationId: "conv-123");
        Assert.Contains("--resume conv-123", result);
    }

    [Fact]
    public void Build_ContinueModeWithConversationId_IncludesBothFlags()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(),
            conversationId: "conv-123", continueMode: true);
        Assert.Contains("--resume conv-123", result);
        Assert.Contains("--continue", result);
    }

    [Fact]
    public void Build_ContinueModeWithoutConversationId_IncludesContinueOnly()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(), continueMode: true);
        Assert.DoesNotContain("--resume", result);
        Assert.Contains("--continue", result);
    }

    [Fact]
    public void Build_ForkSession_IncludesFlag()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(),
            conversationId: "conv-123", forkSession: true);
        Assert.Contains("--fork-session", result);
    }

    [Fact]
    public void Build_MaxTurns_IncludesFlag()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(), maxTurns: 5);
        Assert.Contains("--max-turns 5", result);
    }

    [Fact]
    public void Build_MaxBudget_IncludesFormattedFlag()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(), maxBudgetUsd: 1.50m);
        Assert.Contains("--max-budget-usd 1.50", result);
    }

    [Fact]
    public void Build_FallbackModel_IncludesFlag()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(), fallbackModel: "haiku");
        Assert.Contains("--fallback-model haiku", result);
    }

    [Fact]
    public void Build_McpConfigPath_IncludesQuotedFlag()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(), mcpConfigPath: "/path/to/mcp.json");
        Assert.Contains("--mcp-config \"/path/to/mcp.json\"", result);
    }

    [Fact]
    public void Build_DebugMode_IncludesFlag()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(), debugMode: true);
        Assert.Contains("--debug", result);
    }

    [Fact]
    public void Build_AdditionalDirs_IncludesMultipleFlags()
    {
        var dirs = new List<string> { "/dir1", "/dir2" };
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(), additionalDirs: dirs);
        Assert.Contains("--add-dir \"/dir1\"", result);
        Assert.Contains("--add-dir \"/dir2\"", result);
    }

    [Fact]
    public void Build_AllowedAndDisallowedTools_IncludesFlags()
    {
        var allowed = new List<string> { "Read", "Write" };
        var disallowed = new List<string> { "Bash" };
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(),
            allowedTools: allowed, disallowedTools: disallowed);
        Assert.Contains("--allowedTools \"Read\"", result);
        Assert.Contains("--allowedTools \"Write\"", result);
        Assert.Contains("--disallowedTools \"Bash\"", result);
    }

    [Fact]
    public void Build_SystemPrompt_EscapesSpecialCharacters()
    {
        var prompt = "Line1\nLine2\tTabbed \"quoted\"";
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps(), systemPrompt: prompt);
        Assert.Contains("--append-system-prompt", result);
        Assert.Contains("\\n", result);
        Assert.Contains("\\t", result);
        Assert.Contains("\\\"quoted\\\"", result);
    }

    [Fact]
    public void Build_BaseArgs_PrependedToResult()
    {
        var result = ClaudeArgumentBuilder.Build("/c \"claude\" ", "sonnet", "default", DefaultCaps());
        Assert.StartsWith("/c \"claude\" --print", result);
    }

    [Fact]
    public void Build_NullOptionalParams_OmitsFlags()
    {
        var result = ClaudeArgumentBuilder.Build("", "sonnet", "default", DefaultCaps());

        Assert.DoesNotContain("--resume", result);
        Assert.DoesNotContain("--continue", result);
        Assert.DoesNotContain("--fork-session", result);
        Assert.DoesNotContain("--max-turns", result);
        Assert.DoesNotContain("--max-budget-usd", result);
        Assert.DoesNotContain("--fallback-model", result);
        Assert.DoesNotContain("--mcp-config", result);
        Assert.DoesNotContain("--debug", result);
        Assert.DoesNotContain("--add-dir", result);
        Assert.DoesNotContain("--append-system-prompt", result);
    }
}
