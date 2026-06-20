namespace Seoro.Shared.Services.Cli;

/// <summary>
///     CLI 기반 AI 에이전트 프로바이더 추상화 인터페이스.
///     Claude CLI, Codex CLI 등 각각의 구현체가 이 인터페이스를 구현한다.
/// </summary>
public interface ICliProvider : IDisposable
{
    /// <summary>프로바이더 식별자. "claude" 또는 "codex".</summary>
    string ProviderId { get; }

    /// <summary>UI 표시용 이름. "Claude" 또는 "Codex".</summary>
    string DisplayName { get; }

    /// <summary>이 프로바이더가 지원하는 기능 목록.</summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>
    ///     메시지를 전송하고 스트림 이벤트를 비동기로 반환한다.
    ///     각 구현체는 자신의 CLI 출력을 <see cref="StreamEvent" />로 변환하여 반환한다.
    /// </summary>
    IAsyncEnumerable<StreamEvent> SendMessageAsync(
        CliSendOptions options,
        CancellationToken ct = default);

    /// <summary>CLI 바이너리를 탐지한다.</summary>
    Task<(bool found, string resolvedPath)> DetectCliAsync();

    /// <summary>탐지된 CLI 버전 문자열을 반환한다. 미탐지 시 null.</summary>
    Task<string?> GetDetectedVersionAsync();

    /// <summary>지정된 세션(또는 전체)의 스트리밍을 취소한다.</summary>
    void Cancel(string? sessionId = null);
}
