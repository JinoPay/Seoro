using Pty.Net;

namespace Seoro.Shared.Services.Infrastructure;

/// <summary>PTY 생성 추상화 — 테스트에서 실제 ConPTY/유닉스 PTY 없이 TerminalService를 검증하기 위함.</summary>
public interface IPtySpawner
{
    Task<IPtyConnection> SpawnAsync(PtyOptions options, CancellationToken ct);
}

public sealed class PtyNetSpawner : IPtySpawner
{
    public Task<IPtyConnection> SpawnAsync(PtyOptions options, CancellationToken ct)
    {
        return PtyProvider.SpawnAsync(options, ct);
    }
}
