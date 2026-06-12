using System.Text.Json;
using Seoro.Shared.Services.Claude.Bidirectional;
using Seoro.Shared.Services.Cli.Approval;

namespace Seoro.Shared.Tests;

public class ControlProtocolCodecTests
{
    // --- BuildUserMessage (공식 형식) ---

    [Fact]
    public void BuildUserMessage_ProducesSingleLineUserEnvelope()
    {
        var json = ControlProtocolCodec.BuildUserMessage("hello world");

        Assert.DoesNotContain('\n', json); // JSONL: 한 줄
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("user", root.GetProperty("type").GetString());
        Assert.Equal("user", root.GetProperty("message").GetProperty("role").GetString());
        Assert.Equal("hello world", root.GetProperty("message").GetProperty("content").GetString());
    }

    [Fact]
    public void BuildUserMessage_OmitsNullSessionId()
    {
        var json = ControlProtocolCodec.BuildUserMessage("hi");
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("session_id", out _));
    }

    [Fact]
    public void BuildUserMessage_IncludesSessionIdWhenProvided()
    {
        var json = ControlProtocolCodec.BuildUserMessage("hi", "sess_123");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("sess_123", doc.RootElement.GetProperty("session_id").GetString());
    }

    // --- BuildInterruptRequest ---

    [Fact]
    public void BuildInterruptRequest_HasInterruptSubtype()
    {
        var json = ControlProtocolCodec.BuildInterruptRequest("req_1");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("control_request", root.GetProperty("type").GetString());
        Assert.Equal("req_1", root.GetProperty("request_id").GetString());
        Assert.Equal("interrupt", root.GetProperty("request").GetProperty("subtype").GetString());
    }

    // --- BuildInitializeRequest ---

    [Fact]
    public void BuildInitializeRequest_HasInitializeSubtype()
    {
        var json = ControlProtocolCodec.BuildInitializeRequest("init_1");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("control_request", root.GetProperty("type").GetString());
        Assert.Equal("init_1", root.GetProperty("request_id").GetString());
        Assert.Equal("initialize", root.GetProperty("request").GetProperty("subtype").GetString());
    }

    // --- TryParseControlRequest ---

    [Fact]
    public void TryParseControlRequest_ControlRequest_ReturnsTrue()
    {
        const string line =
            """{"type":"control_request","request_id":"r1","request":{"subtype":"permission","tool_name":"Bash"}}""";
        using var doc = JsonDocument.Parse(line);

        var ok = ControlProtocolCodec.TryParseControlRequest(doc.RootElement, out var req);

        Assert.True(ok);
        Assert.NotNull(req);
        Assert.Equal("r1", req!.RequestId);
        Assert.Equal("permission", req.Subtype);
    }

    [Fact]
    public void TryParseControlRequest_NormalStreamEvent_ReturnsFalse()
    {
        const string line =
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"hi"}}""";
        using var doc = JsonDocument.Parse(line);

        var ok = ControlProtocolCodec.TryParseControlRequest(doc.RootElement, out var req);

        Assert.False(ok);
        Assert.Null(req);
    }

    // --- BuildPermissionResponse ---

    [Fact]
    public void BuildPermissionResponse_Allow_UsesNestedWrappingWithAllowBehavior()
    {
        var json = ControlProtocolCodec.BuildPermissionResponse("r2", ToolApprovalDecision.Allow);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("control_response", root.GetProperty("type").GetString());

        // 중첩 래핑: response.subtype/request_id, response.response.behavior
        var outer = root.GetProperty("response");
        Assert.Equal("success", outer.GetProperty("subtype").GetString());
        Assert.Equal("r2", outer.GetProperty("request_id").GetString());
        Assert.Equal("allow", outer.GetProperty("response").GetProperty("behavior").GetString());
    }

    [Fact]
    public void BuildPermissionResponse_Deny_HasDenyBehaviorAndReason()
    {
        var decision = new ToolApprovalDecision { Outcome = ApprovalOutcome.Deny, DenyReason = "nope" };
        var json = ControlProtocolCodec.BuildPermissionResponse("r3", decision);
        using var doc = JsonDocument.Parse(json);
        var inner = doc.RootElement.GetProperty("response").GetProperty("response");
        Assert.Equal("deny", inner.GetProperty("behavior").GetString());
        Assert.Equal("nope", inner.GetProperty("message").GetString());
    }

    // --- ToApprovalRequest ---

    [Fact]
    public void ToApprovalRequest_Permission_MapsToolName()
    {
        const string line =
            """{"type":"control_request","request_id":"r4","request":{"subtype":"permission","tool_name":"Bash","tool_input":{"command":"ls"}}}""";
        using var doc = JsonDocument.Parse(line);
        ControlProtocolCodec.TryParseControlRequest(doc.RootElement, out var control);

        var approval = ControlProtocolCodec.ToApprovalRequest(control!, "sess_x");

        Assert.NotNull(approval);
        Assert.Equal("sess_x", approval!.SessionId);
        Assert.Equal("Bash", approval.ToolName);
    }

    [Fact]
    public void ToApprovalRequest_NonPermissionSubtype_ReturnsNull()
    {
        const string line =
            """{"type":"control_request","request_id":"r5","request":{"subtype":"initialize"}}""";
        using var doc = JsonDocument.Parse(line);
        ControlProtocolCodec.TryParseControlRequest(doc.RootElement, out var control);

        var approval = ControlProtocolCodec.ToApprovalRequest(control!, "sess_x");

        Assert.Null(approval);
    }
}
