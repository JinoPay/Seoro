namespace Seoro.Shared.Services.Codex.AppServer;

/// <summary>
///     Codex app-server JSON-RPC 메서드/알림 이름 상수.
///     experimental 프로토콜의 버전 드리프트를 이 한 파일에 격리한다(codex 0.139.0 실측 기준).
/// </summary>
internal static class CodexRpcMethods
{
    // 클라이언트 → 서버 요청
    public const string Initialize = "initialize";
    public const string ThreadStart = "thread/start";
    public const string ThreadResume = "thread/resume";
    public const string ThreadFork = "thread/fork";
    public const string TurnStart = "turn/start";
    public const string TurnInterrupt = "turn/interrupt";

    // 클라이언트 → 서버 알림
    public const string Initialized = "initialized";

    // 서버 → 클라이언트 스트리밍 알림
    public const string ThreadStarted = "thread/started";
    public const string TurnStarted = "turn/started";
    public const string TurnCompleted = "turn/completed";
    public const string ItemStarted = "item/started";
    public const string ItemCompleted = "item/completed";
    public const string ItemAgentMessageDelta = "item/agentMessage/delta";
    public const string ItemCommandOutputDelta = "item/commandExecution/outputDelta";
    public const string ThreadTokenUsageUpdated = "thread/tokenUsage/updated";

    // 서버 → 클라이언트 승인 요청(id 있음)
    public const string CommandExecutionRequestApproval = "item/commandExecution/requestApproval";
    public const string FileChangeRequestApproval = "item/fileChange/requestApproval";
    public const string PermissionsRequestApproval = "item/permissions/requestApproval";
    public const string ToolRequestUserInput = "item/tool/requestUserInput";
}
