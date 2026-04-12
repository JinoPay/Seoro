using Seoro.Shared.Services.Codex;

namespace Seoro.Shared.Tests;

public class CodexArgumentBuilderTests
{
    [Fact]
    public void Build_AlwaysIncludesExecAndJsonFlag()
    {
        var result = CodexArgumentBuilder.Build("", "o4-mini", "bypassAll", "/workspace");
        Assert.Contains("exec", result);
        Assert.Contains("--json", result);
    }

    [Fact]
    public void Build_IncludesModel()
    {
        var result = CodexArgumentBuilder.Build("", "o4-mini", "bypassAll", "/workspace");
        Assert.Contains("--model \"o4-mini\"", result);
    }

    [Fact]
    public void Build_BypassAll_UsesDangerouslyBypass()
    {
        var result = CodexArgumentBuilder.Build("", "o4-mini", "bypassAll", "/workspace");
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", result);
        Assert.DoesNotContain("--full-auto", result);
    }

    [Fact]
    public void Build_DangerouslySkipPermissions_UsesDangerouslyBypass()
    {
        var result = CodexArgumentBuilder.Build("", "o4-mini", "dangerouslySkipPermissions", "/workspace");
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", result);
    }

    [Fact]
    public void Build_OtherPermissionMode_UsesConfigOverride()
    {
        // plan 등 비-bypass 모드는 --config approval_policy + --sandbox 조합
        var result = CodexArgumentBuilder.Build("", "o4-mini", "plan", "/workspace");
        Assert.DoesNotContain("--full-auto", result);
        Assert.DoesNotContain("--ask-for-approval", result);
        Assert.DoesNotContain("--dangerously-bypass-approvals-and-sandbox", result);
        Assert.Contains("--config approval_policy=never", result);
        Assert.Contains("--sandbox workspace-write", result);
    }

    [Fact]
    public void Build_IncludesWorkingDir()
    {
        var result = CodexArgumentBuilder.Build("", "o4-mini", "bypassAll", "/my/project");
        Assert.Contains("--cd \"/my/project\"", result);
    }

    [Fact]
    public void Build_EmptyWorkingDir_OmitsCdFlag()
    {
        var result = CodexArgumentBuilder.Build("", "o4-mini", "bypassAll", "");
        Assert.DoesNotContain("--cd", result);
    }

    [Fact]
    public void Build_WithAdditionalDirs_IncludesAddDir()
    {
        var result = CodexArgumentBuilder.Build("", "o4-mini", "bypassAll", "/ws",
            ["/extra1", "/extra2"]);
        Assert.Contains("--add-dir \"/extra1\"", result);
        Assert.Contains("--add-dir \"/extra2\"", result);
    }

    [Fact]
    public void Build_EmptyAdditionalDirs_OmitsAddDir()
    {
        var result = CodexArgumentBuilder.Build("", "o4-mini", "bypassAll", "/ws", []);
        Assert.DoesNotContain("--add-dir", result);
    }

    [Fact]
    public void Build_WithBaseArgs_PrependsThem()
    {
        var result = CodexArgumentBuilder.Build("/c codex.cmd ", "o4-mini", "bypassAll", "/ws");
        Assert.StartsWith("/c codex.cmd", result);
    }

    [Fact]
    public void Build_EmptyModel_OmitsModelFlag()
    {
        var result = CodexArgumentBuilder.Build("", "", "bypassAll", "/ws");
        Assert.DoesNotContain("--model", result);
    }

    // ── Resume ──

    [Fact]
    public void BuildResume_StartsWithExecResumeSubcommand()
    {
        var result = CodexArgumentBuilder.BuildResume("", "thread-123", "o4-mini", "bypassAll", "/ws");
        Assert.StartsWith("exec resume", result);
    }

    [Fact]
    public void BuildResume_IncludesThreadId()
    {
        var result = CodexArgumentBuilder.BuildResume("", "thread-abc", "o4-mini", "bypassAll", "/ws");
        Assert.Contains("\"thread-abc\"", result);
    }

    [Fact]
    public void BuildResume_IncludesJsonFlag()
    {
        var result = CodexArgumentBuilder.BuildResume("", "tid", "o4-mini", "bypassAll", "/ws");
        Assert.Contains("--json", result);
    }

    [Fact]
    public void BuildResume_BypassAll_UsesDangerouslyBypass()
    {
        var result = CodexArgumentBuilder.BuildResume("", "tid", "o4-mini", "bypassAll", "/ws");
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", result);
    }

    [Fact]
    public void BuildResume_OtherMode_UsesConfigOverride()
    {
        var result = CodexArgumentBuilder.BuildResume("", "tid", "o4-mini", "plan", "/ws");
        Assert.DoesNotContain("--full-auto", result);
        Assert.DoesNotContain("--ask-for-approval", result);
        Assert.Contains("--config approval_policy=never", result);
        Assert.Contains("--sandbox workspace-write", result);
    }

    [Fact]
    public void BuildResume_DoesNotIncludeWorkingDir()
    {
        var result = CodexArgumentBuilder.BuildResume("", "tid", "o4-mini", "bypassAll", "/my/project");
        Assert.DoesNotContain("--cd", result);
    }

    // ── CodexBuildOptions 기반 테스트 ──

    [Fact]
    public void Build_WithReasoningEffort_IncludesConfigFlag()
    {
        var result = CodexArgumentBuilder.Build(new CodexBuildOptions
        {
            BaseArgs = "", Model = "gpt-5.4", WorkingDir = "/ws",
            DangerouslyBypass = true, ReasoningEffort = "high"
        });
        Assert.Contains("--config model_reasoning_effort=high", result);
    }

    [Fact]
    public void Build_WithDefaultReasoningEffort_OmitsConfigFlag()
    {
        var result = CodexArgumentBuilder.Build(new CodexBuildOptions
        {
            BaseArgs = "", Model = "gpt-5.4", WorkingDir = "/ws",
            DangerouslyBypass = true, ReasoningEffort = "medium"
        });
        Assert.DoesNotContain("model_reasoning_effort", result);
    }

    [Fact]
    public void Build_WithApprovalAndSandbox_IncludesSeparateFlags()
    {
        // approval_policy는 --config 오버라이드로 지정 (--ask-for-approval 플래그 없음, TUI 전용)
        var result = CodexArgumentBuilder.Build(new CodexBuildOptions
        {
            BaseArgs = "", Model = "gpt-5.4", WorkingDir = "/ws",
            ApprovalPolicy = "on-request", SandboxMode = "read-only"
        });
        Assert.DoesNotContain("--ask-for-approval", result);
        Assert.Contains("--config approval_policy=on-request", result);
        Assert.Contains("--sandbox read-only", result);
        Assert.DoesNotContain("--full-auto", result);
        Assert.DoesNotContain("--dangerously-bypass", result);
    }

    [Fact]
    public void Build_NeverAndWorkspaceWrite_UsesConfigOverride()
    {
        // --full-auto는 exec에서 sandbox만 설정, approval_policy는 --config로 명시 전달
        var result = CodexArgumentBuilder.Build(new CodexBuildOptions
        {
            BaseArgs = "", Model = "gpt-5.4", WorkingDir = "/ws",
            ApprovalPolicy = "never", SandboxMode = "workspace-write"
        });
        Assert.DoesNotContain("--ask-for-approval", result);
        Assert.Contains("--config approval_policy=never", result);
        Assert.Contains("--sandbox workspace-write", result);
    }

    [Fact]
    public void Build_DangerouslyBypass_UsesYoloFlag()
    {
        var result = CodexArgumentBuilder.Build(new CodexBuildOptions
        {
            BaseArgs = "", Model = "gpt-5.4", WorkingDir = "/ws",
            DangerouslyBypass = true
        });
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", result);
    }

    [Fact]
    public void Build_WithWebSearch_IncludesSearchFlag()
    {
        var result = CodexArgumentBuilder.Build(new CodexBuildOptions
        {
            BaseArgs = "", Model = "gpt-5.4", WorkingDir = "/ws",
            DangerouslyBypass = true, WebSearch = true
        });
        Assert.Contains("--config web_search=live", result);
    }

    [Fact]
    public void Build_WithEphemeral_IncludesEphemeralFlag()
    {
        var result = CodexArgumentBuilder.Build(new CodexBuildOptions
        {
            BaseArgs = "", Model = "gpt-5.4", WorkingDir = "/ws",
            DangerouslyBypass = true, Ephemeral = true
        });
        Assert.Contains("--ephemeral", result);
    }

    [Fact]
    public void Build_WithImages_IncludesImageFlags()
    {
        var result = CodexArgumentBuilder.Build(new CodexBuildOptions
        {
            BaseArgs = "", Model = "gpt-5.4", WorkingDir = "/ws",
            DangerouslyBypass = true,
            ImagePaths = ["/img/a.png", "/img/b.jpg"]
        });
        Assert.Contains("--image \"/img/a.png\"", result);
        Assert.Contains("--image \"/img/b.jpg\"", result);
    }

    [Fact]
    public void Build_WithConfigOverrides_IncludesConfigFlags()
    {
        var result = CodexArgumentBuilder.Build(new CodexBuildOptions
        {
            BaseArgs = "", Model = "gpt-5.4", WorkingDir = "/ws",
            DangerouslyBypass = true,
            ConfigOverrides = new Dictionary<string, string>
            {
                ["web_search"] = "live",
                ["agents.max_threads"] = "4"
            }
        });
        Assert.Contains("--config web_search=live", result);
        Assert.Contains("--config agents.max_threads=4", result);
    }

    [Fact]
    public void Build_WithSkipGitRepoCheck_IncludesFlag()
    {
        var result = CodexArgumentBuilder.Build(new CodexBuildOptions
        {
            BaseArgs = "", Model = "gpt-5.4", WorkingDir = "/ws",
            DangerouslyBypass = true, SkipGitRepoCheck = true
        });
        Assert.Contains("--skip-git-repo-check", result);
    }

    [Fact]
    public void BuildResume_WithOptions_IncludesAllFlags()
    {
        var result = CodexArgumentBuilder.BuildResume(new CodexResumeBuildOptions
        {
            BaseArgs = "", ThreadId = "t-123", Model = "gpt-5.4", WorkingDir = "/ws",
            ApprovalPolicy = "untrusted", SandboxMode = "danger-full-access",
            ReasoningEffort = "xhigh", WebSearch = true, All = true
        });
        Assert.Contains("exec resume", result);
        Assert.Contains("\"t-123\"", result);
        Assert.DoesNotContain("--ask-for-approval", result);
        Assert.Contains("--config approval_policy=untrusted", result);
        Assert.Contains("--sandbox danger-full-access", result);
        Assert.Contains("--config model_reasoning_effort=xhigh", result);
        Assert.Contains("--config web_search=live", result);
        Assert.Contains("--all", result);
    }

    [Fact]
    public void BuildResume_WithLastFlag()
    {
        var result = CodexArgumentBuilder.BuildResume(new CodexResumeBuildOptions
        {
            BaseArgs = "", ThreadId = "t-1", Model = "gpt-5.4", WorkingDir = "/ws",
            DangerouslyBypass = true, Last = true
        });
        Assert.Contains("--last", result);
    }
}
