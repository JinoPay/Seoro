namespace Seoro.Shared.Services.Cli.Approval;

/// <summary>
///     모든 승인 요청을 자동으로 허용하는 핸들러.
///     bypassAll 권한 모드와 동일한 동작을 양방향 경로에서 보존한다(Phase 1 기본).
///     Phase 2에서 UI 연동 핸들러로 라우팅된다.
/// </summary>
public sealed class AutoAllowToolApprovalHandler : IToolApprovalHandler
{
    public Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken ct = default)
        => Task.FromResult(ToolApprovalDecision.Allow);
}
