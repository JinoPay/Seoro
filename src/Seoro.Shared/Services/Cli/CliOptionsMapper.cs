using AgentBridge;
using AgentBridge.Claude;
using AgentBridge.Codex;
using Seoro.Shared.Models.Settings;

namespace Seoro.Shared.Services.Cli;

/// <summary>
/// Seoro의 <see cref="CliSendOptions"/>(+ <see cref="AppSettings"/>)를 AgentBridge의 프로바이더별
/// 옵션(<see cref="ClaudeAgentOptions"/> / <see cref="CodexAgentOptions"/>)으로 변환하는 순수 함수 모음.
/// </summary>
public static class CliOptionsMapper
{
    public static ClaudeAgentOptions BuildClaudeOptions(CliSendOptions o, AppSettings settings)
    {
        return new ClaudeAgentOptions
        {
            Model = o.Model,
            WorkingDirectory = o.WorkingDir,
            SystemPrompt = string.IsNullOrEmpty(o.SystemPrompt) ? null : o.SystemPrompt,
            AppendSystemPrompt = false, // Seoro는 전체 교체 프롬프트를 직접 조립한다.
            AllowedTools = o.AllowedTools?.ToArray() ?? Array.Empty<string>(),
            DisallowedTools = o.DisallowedTools?.ToArray() ?? Array.Empty<string>(),
            MaxTurns = o.MaxTurns,
            Effort = NormalizeEffort(o.EffortLevel),
            MaxBudgetUsd = o.MaxBudgetUsd,
            Environment = settings.EnvironmentVariables.Count > 0
                ? new Dictionary<string, string>(settings.EnvironmentVariables)
                : new Dictionary<string, string>(),
            AdditionalDirectories = o.AdditionalDirs?.ToArray() ?? Array.Empty<string>(),
            ExecutablePath = string.IsNullOrWhiteSpace(settings.ClaudePath) ? null : settings.ClaudePath,
            PermissionMode = MapPermissionMode(o.PermissionMode),
            Resume = string.IsNullOrEmpty(o.ConversationId) ? null : o.ConversationId,
            ContinueConversation = o.ContinueMode,
            ForkSession = o.ForkSession,
            FallbackModel = string.IsNullOrWhiteSpace(settings.FallbackModel) ? null : settings.FallbackModel,
            ExternalMcpConfig = ResolveMcpConfig(settings.McpConfigPath),
            // Seoro는 토큰 단위 실시간 스트리밍을 사용한다.
            IncludePartialMessages = true,
        };
    }

    public static CodexAgentOptions BuildCodexOptions(CliSendOptions o, AppSettings settings)
    {
        var bypass = o.PermissionMode is "bypassAll" or "dangerouslySkipPermissions";
        var isPlan = o.PermissionMode == "plan";

        return new CodexAgentOptions
        {
            Model = o.Model,
            WorkingDirectory = o.WorkingDir,
            AdditionalDirectories = o.AdditionalDirs?.ToArray() ?? Array.Empty<string>(),
            Environment = settings.EnvironmentVariables.Count > 0
                ? new Dictionary<string, string>(settings.EnvironmentVariables)
                : new Dictionary<string, string>(),
            ExecutablePath = string.IsNullOrWhiteSpace(settings.CodexPath) ? null : settings.CodexPath,
            ReasoningEffort = MapCodexEffort(o.EffortLevel, settings.CodexReasoningEffort),
            WebSearch = settings.CodexWebSearch,
            Resume = string.IsNullOrEmpty(o.ConversationId) ? null : o.ConversationId,
            SkipGitRepoCheck = true,
            DangerouslyBypassApprovalsAndSandbox = bypass,
            // bypass면 승인/샌드박스 설정은 무시되므로 비워둔다. plan은 읽기 전용/요청 시 승인.
            ApprovalPolicy = bypass ? null : ParseApprovalPolicy(isPlan ? "on-request" : settings.CodexApprovalPolicy),
            Sandbox = bypass ? null : ParseSandbox(isPlan ? "read-only" : settings.CodexSandboxMode),
        };
    }

    /// <summary>Codex는 시스템 프롬프트 플래그가 없어 메시지 본문에 합쳐 전달한다(기존 동작 보존).</summary>
    public static string ComposeCodexPrompt(string message, string? systemPrompt)
        => string.IsNullOrEmpty(systemPrompt)
            ? message
            : $"[System Instructions]\n{systemPrompt}\n\n[User]\n{message}";

    private static string? NormalizeEffort(string? effort)
        => string.IsNullOrEmpty(effort) || string.Equals(effort, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : effort;

    private static PermissionMode? MapPermissionMode(string? mode) => mode switch
    {
        "plan" => PermissionMode.Plan,
        "acceptEdits" => PermissionMode.AcceptEdits,
        "dontAsk" => PermissionMode.DontAsk,
        "bypassPermissions" => PermissionMode.BypassPermissions,
        "bypassAll" => PermissionMode.BypassPermissions,
        "dangerouslySkipPermissions" => PermissionMode.BypassPermissions,
        _ => PermissionMode.Default,
    };

    private static string? ResolveMcpConfig(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;

    /// <summary>Seoro effort 레벨 + 글로벌 폴백 → Codex reasoning effort enum (기존 MapEffortLevel 동작 보존).</summary>
    internal static CodexReasoningEffort MapCodexEffort(string? effortLevel, string fallback)
    {
        var resolved = (effortLevel ?? string.Empty).ToLowerInvariant() switch
        {
            "auto" => fallback,
            "low" or "minimal" => "low",
            "medium" => "medium",
            "high" => "high",
            "max" or "xhigh" => "xhigh",
            _ => fallback,
        };
        return ParseCodexEffort(resolved);
    }

    private static CodexReasoningEffort ParseCodexEffort(string value) => value.ToLowerInvariant() switch
    {
        "minimal" => CodexReasoningEffort.Minimal,
        "low" => CodexReasoningEffort.Low,
        "medium" => CodexReasoningEffort.Medium,
        "high" => CodexReasoningEffort.High,
        "xhigh" => CodexReasoningEffort.XHigh,
        _ => CodexReasoningEffort.Medium,
    };

    private static CodexApprovalPolicy ParseApprovalPolicy(string value) => value.ToLowerInvariant() switch
    {
        "untrusted" => CodexApprovalPolicy.Untrusted,
        "on-failure" => CodexApprovalPolicy.OnFailure,
        "on-request" => CodexApprovalPolicy.OnRequest,
        "never" => CodexApprovalPolicy.Never,
        _ => CodexApprovalPolicy.Never,
    };

    private static CodexSandbox ParseSandbox(string value) => value.ToLowerInvariant() switch
    {
        "read-only" => CodexSandbox.ReadOnly,
        "workspace-write" => CodexSandbox.WorkspaceWrite,
        "danger-full-access" => CodexSandbox.DangerFullAccess,
        _ => CodexSandbox.WorkspaceWrite,
    };
}
