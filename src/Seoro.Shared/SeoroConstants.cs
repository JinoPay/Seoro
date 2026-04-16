namespace Seoro.Shared;

/// <summary>
///     여러 서비스와 모델에서 사용되는 공유 상수들입니다.
/// </summary>
public static class SeoroConstants
{
    // 세션 제한
    public const int MaxActiveSessionsPerWorkspace = 20;
    public const int MaxContextItemTokens = 2_000; // 단일 메모/todo/계획 파일

    // 시스템 프롬프트 크기 제한 (TokenEstimator를 통한 토큰 기반)
    public const int MaxContextPromptTokens = 5_000; // 메모 + todo + 계획 합계
    public const int MaxMemoryEntryTokens = 1_000; // 단일 메모리 항목 콘텐츠
    public const int MaxMemoryPromptTokens = 2_500; // 모든 메모리 항목 합계
    public const int MaxSystemPromptTokens = 10_000; // 전체 시스템 프롬프트 예산
    public const string BranchPrefix = "seoro/";

    public const string DefaultEffortLevel = "auto";

    // 채팅 입력: 이 임계값보다 긴 붙여넣은 텍스트는 자동으로 파일 첨부로 변환됨
    public const int PasteAsFileThreshold = 500;

    // AppSettings, Session, ClaudeService 등에 중복되는 기본값
    public const string DefaultPermissionMode = "bypassAll";
    public const string PathCaptureSentinel = "___SEORO_PATH___";
    public const string RequiredClaudeVersion = "2.1.81";

    // 영구 워크트리 디렉터리 제약 — 항상 워크트리 세션에 주입됨
    public const string SystemInstructionWorktreeDir =
        "작업 디렉터리 제약 (절대 위반 금지): 이 세션은 git worktree에서 실행됩니다. " +
        "모든 파일 읽기·쓰기·검색은 반드시 워크트리 디렉터리({0}) 내에서 수행하세요. " +
        "원본 저장소 경로나 워크트리 외부 경로에서 절대 파일을 읽거나 수정하지 마세요. " +
        "사용자가 명시적으로 지정한 경로가 아닌 한, 워크트리 외부로 나가지 마세요.";

    // 세션 제목이 결정될 때까지 주입되는 내장 시스템 지시사항
    public static string GetSystemInstructionWorktree(string language = "en")
    {
        var langRule = language == "en"
            ? "use lowercase-kebab-case English words"
            : "한글 또는 영문 소문자 kebab-case";

        return "필수 최우선 실행 (Plan·Explore 모드 포함 모든 상황에서 최우선 — 절대 생략·지연 금지):\n" +
               "`git branch -m seoro/<설명적-이름>`을 즉시 실행하여 현재 브랜치 이름을 변경하세요.\n" +
               "현재 브랜치 이름을 확인하지 마세요. 이 단계를 건너뛰거나 미루지 마세요.\n" +
               $"브랜치 이름 규칙: 구체적이고 간결하게 (30자 이내), \"seoro/\" 접두사, {langRule}.";
    }

    public static string GetSystemInstructionLocalDir(string language = "en")
    {
        var langRule = language == "en"
            ? "Title must be in English."
            : "제목은 한국어로 작성하세요.";

        return "필수 최우선 실행 (Plan·Explore 모드 포함 모든 상황에서 최우선 — 절대 생략·지연 금지):\n" +
               "이 세션은 로컬 디렉터리를 사용하므로 브랜치 이름을 변경하지 마세요.\n" +
               "대신 대화 내용에 맞는 작업 제목을 정하여 첫 응답에 반드시 포함하세요:\n" +
               "<!-- seoro:title 제목 -->\n" +
               "이 마커를 절대 생략하지 마세요.\n" +
               $"제목 규칙: 구체적이고 간결하게 (30자 이내). {langRule}";
    }

    public const string TitleMarkerPrefix = "<!-- seoro:title ";
    public const string TitleMarkerSuffix = " -->";

    // ─── 머지 AI 프롬프트 기본 템플릿 ───────────────────────────────────────
    // 변수: {branch}, {target}, {uncommittedNote}, {conflictFiles}
    // AppSettings.MergePrompt* 가 null 이면 이 상수를 사용.

    public const string DefaultMergePromptCreatePr =
        """
        현재 워크트리 브랜치(`{branch}`)의 변경사항으로 GitHub PR을 생성해주세요. 순서대로 진행하세요:

        1. `git status`로 현재 상태를 확인하세요.{uncommittedNote}
        2. 커밋되지 않은 변경이 있으면 변경 내용을 요약한 커밋 메시지로 커밋하세요.
        3. `git push origin {branch}`로 원격에 푸시하세요 (필요 시 `--set-upstream` 추가).
        4. `gh pr create --base {target} --head {branch} --title "..." --body "..."`로 PR을 생성하세요.
           - 제목과 본문은 변경 내용을 보고 자동으로 작성하세요.
        5. 생성된 PR URL을 알려주세요.

        **중요**: 워크트리 경로 바깥은 절대 읽거나 쓰지 마세요.
        """;

    public const string DefaultMergePromptPush =
        """
        현재 브랜치(`{branch}`)의 변경사항을 origin에 푸시해주세요. 순서대로 진행하세요:

        1. `git status`로 현재 상태를 확인하세요.
        2. 스테이지되지 않은 변경이 있으면 `git add -A`로 전부 스테이지하세요.
        3. 커밋되지 않은 변경이 있으면 변경 내용을 요약한 커밋 메시지로 커밋하세요.
        4. `git fetch origin {branch}`로 원격 상태를 확인하고,
           원격이 앞서 있으면 `git pull --rebase origin {branch}`로 rebase 하세요.
           rebase 중 충돌이 발생하면 충돌을 해결한 뒤 `git rebase --continue` 하세요.
        5. `git push origin {branch}`로 푸시하세요.
        """;

    public const string DefaultMergePromptResolveConflict =
        """
        현재 워크트리에 머지 충돌이 있습니다 (`.git/MERGE_HEAD` 가 존재합니다).
        충돌 파일 목록:
        - {conflictFiles}

        이 파일들의 충돌을 해결한 뒤 `git merge --continue` (또는 커밋)까지 완료해주세요.
        **중요**: 워크트리 경로 바깥은 절대 읽거나 쓰지 마세요.
        """;

    public const string DefaultMergePromptRebaseOnTarget =
        """
        현재 브랜치(`{branch}`)를 타겟 브랜치(`{target}`)에 rebase해주세요. 순서대로 진행하세요:

        1. `git fetch origin`으로 최신 상태를 가져오세요.
        2. `git rebase origin/{target}`를 실행하세요.
        3. 충돌이 발생하면:
           - 각 충돌 파일을 열어 충돌을 해결하세요.
           - `git add <해결된 파일>`로 스테이지하세요.
           - `git rebase --continue`로 rebase를 계속하세요.
           - 모든 충돌이 해결될 때까지 반복하세요.
        4. rebase 완료 후 `git push origin {branch} --force-with-lease`로 푸시하세요.

        **중요**: 워크트리 경로 바깥은 절대 읽거나 쓰지 마세요.
        """;

    public const string TruncationMarker = "\n\n[...truncated, {0:N0} tokens total]";
    public static readonly TimeSpan ShellCacheTtl = TimeSpan.FromMinutes(10);

    // 타임아웃 / 재시도 상수
    public static readonly TimeSpan WhichTimeout = TimeSpan.FromSeconds(5);

    // 여러 프로세스 시작 서비스에서 공유하는 환경 변수
    public static class Env
    {
        /// <summary>
        ///     대화형 프롬프트와 색상 코드를 억제하는 공통 환경 블록입니다.
        ///     GitService, ClaudeCliResolver 등에서 사용됩니다.
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
        public const string HookEvent = "SEORO_HOOK_EVENT";
        public const string NoColor = "NO_COLOR";
    }
}