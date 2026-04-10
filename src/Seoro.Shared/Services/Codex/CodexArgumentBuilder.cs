using System.Text;

namespace Seoro.Shared.Services.Codex;

/// <summary>
///     Codex CLI 호출에 필요한 빌드 옵션.
/// </summary>
public record CodexBuildOptions
{
    /// <summary>resolver가 반환한 기본 인자 프리픽스 (예: "/c codex.cmd ").</summary>
    public required string BaseArgs { get; init; }

    /// <summary>사용할 모델 ID (예: "gpt-5.4").</summary>
    public required string Model { get; init; }

    /// <summary>작업 디렉터리.</summary>
    public required string WorkingDir { get; init; }

    /// <summary>승인 정책. untrusted | on-request | never.</summary>
    public string ApprovalPolicy { get; init; } = "never";

    /// <summary>샌드박스 모드. read-only | workspace-write | danger-full-access.</summary>
    public string SandboxMode { get; init; } = "workspace-write";

    /// <summary>추론 노력 수준. minimal | low | medium | high | xhigh.</summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>웹 검색 활성화.</summary>
    public bool WebSearch { get; init; }

    /// <summary>임시 세션 (세션 파일 미저장).</summary>
    public bool Ephemeral { get; init; }

    /// <summary>Git 저장소 검사 건너뛰기.</summary>
    public bool SkipGitRepoCheck { get; init; }

    /// <summary>승인 및 샌드박스를 완전 우회 (--dangerously-bypass-approvals-and-sandbox).</summary>
    public bool DangerouslyBypass { get; init; }

    /// <summary>추가 접근 허용 디렉터리 목록.</summary>
    public List<string>? AdditionalDirs { get; init; }

    /// <summary>이미지 첨부 파일 경로 목록.</summary>
    public List<string>? ImagePaths { get; init; }

    /// <summary>--config key=value 오버라이드.</summary>
    public Dictionary<string, string>? ConfigOverrides { get; init; }
}

/// <summary>
///     Codex CLI 재개용 빌드 옵션.
/// </summary>
public record CodexResumeBuildOptions : CodexBuildOptions
{
    /// <summary>재개할 세션/스레드 ID.</summary>
    public required string ThreadId { get; init; }

    /// <summary>현재 디렉터리 외부 세션도 포함.</summary>
    public bool All { get; init; }

    /// <summary>가장 최근 세션을 자동 재개.</summary>
    public bool Last { get; init; }
}

/// <summary>
///     Codex CLI (`openai/codex`) 호출에 필요한 인자 문자열을 조합한다.
///     Codex CLI는 Claude CLI와 다른 플래그 체계를 사용한다.
/// </summary>
public static class CodexArgumentBuilder
{
    /// <summary>
    ///     일반 메시지 전송용 인자를 조합한다.
    /// </summary>
    public static string Build(CodexBuildOptions opts)
    {
        var sb = new StringBuilder(opts.BaseArgs);

        // exec 서브커맨드 사용 (--json은 exec에서만 지원)
        sb.Append("exec ");

        // JSONL 출력 활성화 (필수)
        sb.Append("--json ");

        // 모델
        if (!string.IsNullOrWhiteSpace(opts.Model))
            sb.Append($"--model \"{opts.Model}\" ");

        // 승인/샌드박스 모드 매핑
        AppendPermissionFlags(sb, opts);

        // 추론 노력 수준
        if (!string.IsNullOrWhiteSpace(opts.ReasoningEffort) && opts.ReasoningEffort != "medium")
            sb.Append($"--config model_reasoning_effort={opts.ReasoningEffort} ");

        // 웹 검색 (exec에서는 --config web_search=live, 기본값은 캐시 검색)
        if (opts.WebSearch)
            sb.Append("--config web_search=live ");

        // 임시 세션
        if (opts.Ephemeral)
            sb.Append("--ephemeral ");

        // Git 저장소 검사 건너뛰기
        if (opts.SkipGitRepoCheck)
            sb.Append("--skip-git-repo-check ");

        // 이미지 첨부
        if (opts.ImagePaths is { Count: > 0 })
            foreach (var img in opts.ImagePaths)
                sb.Append($"--image \"{img}\" ");

        // config 오버라이드
        if (opts.ConfigOverrides is { Count: > 0 })
            foreach (var (key, value) in opts.ConfigOverrides)
                sb.Append($"--config {key}={value} ");

        // 작업 디렉터리
        if (!string.IsNullOrWhiteSpace(opts.WorkingDir))
            sb.Append($"--cd \"{opts.WorkingDir}\" ");

        // 추가 디렉터리
        if (opts.AdditionalDirs is { Count: > 0 })
            foreach (var dir in opts.AdditionalDirs)
                sb.Append($"--add-dir \"{dir}\" ");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    ///     세션 재개용 인자를 조합한다.
    /// </summary>
    public static string BuildResume(CodexResumeBuildOptions opts)
    {
        var sb = new StringBuilder(opts.BaseArgs);

        // exec resume 서브커맨드
        sb.Append("exec resume ");
        sb.Append($"\"{opts.ThreadId}\" ");
        sb.Append("--json ");

        if (!string.IsNullOrWhiteSpace(opts.Model))
            sb.Append($"--model \"{opts.Model}\" ");

        AppendPermissionFlags(sb, opts);

        if (!string.IsNullOrWhiteSpace(opts.ReasoningEffort) && opts.ReasoningEffort != "medium")
            sb.Append($"--config model_reasoning_effort={opts.ReasoningEffort} ");

        if (opts.WebSearch)
            sb.Append("--config web_search=live ");

        if (opts.Ephemeral)
            sb.Append("--ephemeral ");

        if (opts.SkipGitRepoCheck)
            sb.Append("--skip-git-repo-check ");

        if (opts.ImagePaths is { Count: > 0 })
            foreach (var img in opts.ImagePaths)
                sb.Append($"--image \"{img}\" ");

        if (opts.ConfigOverrides is { Count: > 0 })
            foreach (var (key, value) in opts.ConfigOverrides)
                sb.Append($"--config {key}={value} ");

        if (opts.All)
            sb.Append("--all ");

        if (opts.Last)
            sb.Append("--last ");

        if (opts.AdditionalDirs is { Count: > 0 })
            foreach (var dir in opts.AdditionalDirs)
                sb.Append($"--add-dir \"{dir}\" ");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    ///     레거시 호환 — 기존 호출 코드/테스트용.
    /// </summary>
    public static string Build(
        string baseArgs,
        string model,
        string permissionMode,
        string workingDir,
        List<string>? additionalDirs = null)
    {
        return Build(new CodexBuildOptions
        {
            BaseArgs = baseArgs,
            Model = model,
            WorkingDir = workingDir,
            DangerouslyBypass = permissionMode is "bypassAll" or "dangerouslySkipPermissions",
            AdditionalDirs = additionalDirs
        });
    }

    /// <summary>
    ///     레거시 호환 — 기존 호출 코드/테스트용.
    /// </summary>
    public static string BuildResume(
        string baseArgs,
        string threadId,
        string model,
        string permissionMode,
        string workingDir,
        List<string>? additionalDirs = null)
    {
        return BuildResume(new CodexResumeBuildOptions
        {
            BaseArgs = baseArgs,
            ThreadId = threadId,
            Model = model,
            WorkingDir = workingDir,
            DangerouslyBypass = permissionMode is "bypassAll" or "dangerouslySkipPermissions",
            AdditionalDirs = additionalDirs
        });
    }

    private static void AppendPermissionFlags(StringBuilder sb, CodexBuildOptions opts)
    {
        if (opts.DangerouslyBypass)
        {
            sb.Append("--dangerously-bypass-approvals-and-sandbox ");
            return;
        }

        // Codex exec: --ask-for-approval 플래그 없음 (TUI 전용)
        // approval_policy는 --config 오버라이드로 지정 (config.schema.json 기준)
        var approval = opts.ApprovalPolicy;
        var sandbox = opts.SandboxMode;

        if (!string.IsNullOrWhiteSpace(approval))
            sb.Append($"--config approval_policy={approval} ");

        if (!string.IsNullOrWhiteSpace(sandbox))
            sb.Append($"--sandbox {sandbox} ");
    }
}
