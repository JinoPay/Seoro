using AgentBridge;
using AgentBridge.Codex;
using Seoro.Shared.Models.Settings;
using Seoro.Shared.Services.Cli;

namespace Seoro.Shared.Tests;

public class CliOptionsMapperTests
{
    private static CliSendOptions Opts(
        string permission = "bypassAll",
        string effort = "auto",
        string? conversationId = null,
        bool continueMode = false)
        => new()
        {
            Message = "hi",
            WorkingDir = "/work",
            Model = "claude-x",
            PermissionMode = permission,
            EffortLevel = effort,
            ConversationId = conversationId,
            ContinueMode = continueMode,
        };

    [Theory]
    [InlineData("plan", PermissionMode.Plan)]
    [InlineData("acceptEdits", PermissionMode.AcceptEdits)]
    [InlineData("dontAsk", PermissionMode.DontAsk)]
    [InlineData("bypassPermissions", PermissionMode.BypassPermissions)]
    [InlineData("bypassAll", PermissionMode.BypassPermissions)]
    [InlineData("weird", PermissionMode.Default)]
    public void Claude_MapsPermissionMode(string input, PermissionMode expected)
    {
        var claude = CliOptionsMapper.BuildClaudeOptions(Opts(permission: input), new AppSettings());
        Assert.Equal(expected, claude.PermissionMode);
    }

    [Fact]
    public void Claude_EnablesPartialStreaming_AndDropsAutoEffort()
    {
        var claude = CliOptionsMapper.BuildClaudeOptions(Opts(effort: "auto"), new AppSettings());
        Assert.True(claude.IncludePartialMessages);
        Assert.Null(claude.Effort);
    }

    [Fact]
    public void Claude_PassesEffortAndResume()
    {
        var claude = CliOptionsMapper.BuildClaudeOptions(
            Opts(effort: "high", conversationId: "conv-1"), new AppSettings());
        Assert.Equal("high", claude.Effort);
        Assert.Equal("conv-1", claude.Resume);
        Assert.Equal("/work", claude.WorkingDirectory);
    }

    [Fact]
    public void Claude_ContinueMode_SetsContinueConversation()
    {
        var claude = CliOptionsMapper.BuildClaudeOptions(Opts(continueMode: true), new AppSettings());
        Assert.True(claude.ContinueConversation);
    }

    [Fact]
    public void Codex_BypassAll_SetsDangerousBypass()
    {
        var codex = CliOptionsMapper.BuildCodexOptions(Opts(permission: "bypassAll"), new AppSettings());
        Assert.True(codex.DangerouslyBypassApprovalsAndSandbox);
        // bypass면 승인/샌드박스는 비워둔다(플래그가 무시되므로).
        Assert.Null(codex.ApprovalPolicy);
        Assert.Null(codex.Sandbox);
    }

    [Fact]
    public void Codex_PlanMode_ReadOnlyAndOnRequest()
    {
        var codex = CliOptionsMapper.BuildCodexOptions(Opts(permission: "plan"), new AppSettings());
        Assert.False(codex.DangerouslyBypassApprovalsAndSandbox);
        Assert.Equal(CodexApprovalPolicy.OnRequest, codex.ApprovalPolicy);
        Assert.Equal(CodexSandbox.ReadOnly, codex.Sandbox);
    }

    [Fact]
    public void Codex_DefaultPermission_UsesSettings()
    {
        var settings = new AppSettings
        {
            CodexApprovalPolicy = "untrusted",
            CodexSandboxMode = "workspace-write",
        };
        var codex = CliOptionsMapper.BuildCodexOptions(Opts(permission: "default"), settings);
        Assert.Equal(CodexApprovalPolicy.Untrusted, codex.ApprovalPolicy);
        Assert.Equal(CodexSandbox.WorkspaceWrite, codex.Sandbox);
    }

    [Theory]
    [InlineData("auto", "high", CodexReasoningEffort.High)] // auto → 글로벌 폴백
    [InlineData("low", "medium", CodexReasoningEffort.Low)]
    [InlineData("minimal", "medium", CodexReasoningEffort.Low)]
    [InlineData("max", "medium", CodexReasoningEffort.XHigh)]
    [InlineData("high", "medium", CodexReasoningEffort.High)]
    public void Codex_MapsEffort(string effort, string fallback, CodexReasoningEffort expected)
    {
        Assert.Equal(expected, CliOptionsMapper.MapCodexEffort(effort, fallback));
    }

    [Fact]
    public void Codex_ComposesSystemPromptIntoMessage()
    {
        Assert.Equal("just the message", CliOptionsMapper.ComposeCodexPrompt("just the message", null));
        var composed = CliOptionsMapper.ComposeCodexPrompt("do it", "be careful");
        Assert.Contains("[System Instructions]", composed);
        Assert.Contains("be careful", composed);
        Assert.Contains("[User]", composed);
        Assert.Contains("do it", composed);
    }
}
