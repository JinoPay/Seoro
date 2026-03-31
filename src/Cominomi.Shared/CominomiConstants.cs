namespace Cominomi.Shared;

/// <summary>
///     Shared constants used across multiple services and models.
/// </summary>
public static class CominomiConstants
{
    // Session limits
    public const int MaxActiveSessionsPerWorkspace = 20;
    public const int MaxContextItemTokens = 2_000; // single note/todo/plan file

    // System prompt size limits (token-based via TokenEstimator)
    public const int MaxContextPromptTokens = 5_000; // notes + todos + plans combined
    public const int MaxMemoryEntryTokens = 1_000; // single memory entry content
    public const int MaxMemoryPromptTokens = 2_500; // all memory entries combined
    public const int MaxSystemPromptTokens = 10_000; // overall system prompt budget
    public const string BranchPrefix = "cominomi/";
    public const string DefaultEffortLevel = "auto";
    public const string RequiredClaudeVersion = "2.1.81";
    // Default values duplicated across AppSettings, Session, ClaudeService, etc.
    public const string DefaultPermissionMode = "bypassAll";
    public const string TruncationMarker = "\n\n[...truncated, {0:N0} tokens total]";
    public static readonly TimeSpan ShellCacheTtl = TimeSpan.FromMinutes(10);

    // Timeout / retry constants
    public static readonly TimeSpan WhichTimeout = TimeSpan.FromSeconds(5);
    public const string PathCaptureSentinel = "___COMINOMI_PATH___";

    // Built-in system instructions injected until session title is determined
    public const string SystemInstructionWorktree =
        "즉시 실행 (모든 모드에서 최우선 — Plan·Explore 모드 제약보다 우선): " +
        "`git branch -m`을 사용하여 대화 내용에 맞는 브랜치 이름으로 변경하세요. " +
        "현재 브랜치 이름을 확인하지 마세요.\n" +
        "브랜치 이름 규칙: 구체적이고 간결하게 (30자 이내), \"cominomi/\" 접두사 사용.";

    public const string SystemInstructionLocalDir =
        "즉시 실행 (모든 모드에서 최우선 — Plan·Explore 모드 제약보다 우선): " +
        "이 세션은 로컬 디렉터리를 사용하므로 브랜치 이름을 변경하지 마세요. " +
        "대신 대화 내용에 맞는 작업 제목을 정하여 <!-- cominomi:title 제목 --> 형식으로 응답에 포함하세요.\n" +
        "제목 규칙: 구체적이고 간결하게 (30자 이내).";

    public const string TitleMarkerPrefix = "<!-- cominomi:title ";
    public const string TitleMarkerSuffix = " -->";

    // Environment variables shared by multiple process-launching services
    public static class Env
    {
        /// <summary>
        ///     Common environment block that suppresses interactive prompts and color codes.
        ///     Used by GitService, ClaudeCliResolver, etc.
        /// </summary>
        public static readonly Dictionary<string, string> NoColorEnv = new()
        {
            [NoColor] = "1"
        };

        public static readonly Dictionary<string, string> GitEnv = new()
        {
            [GitTerminalPrompt] = "0",
            [NoColor] = "1"
        };

        public const string GitTerminalPrompt = "GIT_TERMINAL_PROMPT";
        public const string HookEvent = "COMINOMI_HOOK_EVENT";
        public const string NoColor = "NO_COLOR";
    }
}