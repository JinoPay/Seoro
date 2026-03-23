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
    // Default values duplicated across AppSettings, Session, ClaudeService, etc.
    public const string DefaultPermissionMode = "bypassAll";
    public const string TruncationMarker = "\n\n[...truncated, {0:N0} tokens total]";
    public static readonly TimeSpan ShellCacheTtl = TimeSpan.FromMinutes(10);

    // Timeout / retry constants
    public static readonly TimeSpan WhichTimeout = TimeSpan.FromSeconds(5);

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