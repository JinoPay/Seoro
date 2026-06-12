using System.Text.Json;
using Seoro.Shared.Services.Cli.Approval;

namespace Seoro.Shared.Services.Claude.Bidirectional;

/// <summary>
///     Claude CLI 양방향 stream-json 프로토콜의 와이어 포맷을 담당하는 <b>유일한</b> 격리 지점.
///     user 메시지 형식은 공식 문서화돼 있으나, control_request/response(권한·interrupt)는
///     공식 스키마가 없어(GitHub anthropics/claude-code#24594) SDK 소스에서 역추적한 형식이다.
///     CLI 버전업으로 형식이 바뀌면 이 파일만 수정한다.
/// </summary>
public static class ControlProtocolCodec
{
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    ///     공식 형식: 영속 프로세스 stdin으로 흘려보내는 user 메시지 한 줄(JSONL).
    /// </summary>
    public static string BuildUserMessage(string content, string? sessionId = null, string? parentToolUseId = null)
    {
        var obj = new
        {
            type = "user",
            message = new { role = "user", content },
            session_id = sessionId,
            parent_tool_use_id = parentToolUseId
        };
        return JsonSerializer.Serialize(obj, CompactOptions);
    }

    /// <summary>
    ///     비공식 형식: 진행 중인 턴을 중단시키는 interrupt control_request.
    /// </summary>
    public static string BuildInterruptRequest(string requestId)
    {
        var obj = new
        {
            type = "control_request",
            request_id = requestId,
            request = new { subtype = "interrupt" }
        };
        return JsonSerializer.Serialize(obj, CompactOptions);
    }

    /// <summary>
    ///     비공식 형식: 권한 control protocol을 활성화하는 initialize 핸드셰이크.
    ///     실측 확인됨 — 이 메시지를 보내야 CLI가 권한/제어 control_request를 발행한다.
    ///     CLI는 control_response(subtype=success)로 응답한다(현재는 수신 후 무시).
    /// </summary>
    public static string BuildInitializeRequest(string requestId)
    {
        var obj = new
        {
            type = "control_request",
            request_id = requestId,
            request = new { subtype = "initialize", hooks = new { }, sdk_mcp_servers = new { } }
        };
        return JsonSerializer.Serialize(obj, CompactOptions);
    }

    /// <summary>
    ///     권한 control_request에 대한 응답. CLI의 control_response 와이어 형식은
    ///     중첩 래핑 구조다(실측 확인): { type, response: { subtype:"success", request_id, response: {...} } }.
    /// </summary>
    public static string BuildPermissionResponse(string requestId, ToolApprovalDecision decision)
    {
        var behavior = decision.Outcome switch
        {
            ApprovalOutcome.Allow or ApprovalOutcome.AllowForSession => "allow",
            _ => "deny"
        };
        var inner = decision.Outcome is ApprovalOutcome.Deny or ApprovalOutcome.Cancel
            ? new { behavior, message = decision.DenyReason ?? "Denied by user" }
            : new { behavior, message = (string?)null } as object;

        var obj = new
        {
            type = "control_response",
            response = new
            {
                subtype = "success",
                request_id = requestId,
                response = inner
            }
        };
        return JsonSerializer.Serialize(obj, CompactOptions);
    }

    /// <summary>
    ///     한 줄을 파싱해 control_request이면 true. 일반 stream-json 이벤트면 false.
    ///     형식 파싱이 실패하면 false(호출자는 일반 이벤트 경로로 처리).
    /// </summary>
    internal static bool TryParseControlRequest(JsonElement root, out ClaudeControlRequest? request)
    {
        request = null;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "control_request")
            return false;

        var requestId = root.TryGetProperty("request_id", out var idEl) ? idEl.GetString() ?? "" : "";
        var inner = root.TryGetProperty("request", out var reqEl) ? reqEl : default;
        var subtype = inner.ValueKind == JsonValueKind.Object && inner.TryGetProperty("subtype", out var subEl)
            ? subEl.GetString() ?? ""
            : "";

        request = new ClaudeControlRequest(requestId, subtype, inner);
        return true;
    }

    /// <summary>
    ///     control_request(permission)를 도메인 <see cref="ToolApprovalRequest" />로 변환한다.
    ///     permission이 아니거나 형식이 안 맞으면 null.
    /// </summary>
    internal static ToolApprovalRequest? ToApprovalRequest(ClaudeControlRequest control, string sessionId)
    {
        if (control.Subtype is not ("permission" or "can_use_tool")) return null;

        var req = control.Request;
        var toolName = req.TryGetProperty("tool_name", out var tn) ? tn.GetString() ?? "" : "";
        var input = req.TryGetProperty("tool_input", out var ti) ? ti.Clone() : (JsonElement?)null;

        return new ToolApprovalRequest
        {
            SessionId = sessionId,
            Kind = ToolApprovalKind.GenericTool,
            ToolName = toolName,
            RawInput = input
        };
    }
}
