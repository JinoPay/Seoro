namespace Seoro.Shared.Services.Chat;

/// <summary>
///     <see cref="IStreamingStateQuery"/>의 Chat 측 구현. 스트리밍 상태의 진실
///     소스인 <see cref="IChatState"/>에 위임한다. 이 어댑터 덕분에 Account 등
///     다른 도메인은 Chat 타입을 직접 참조하지 않는다.
/// </summary>
public class ChatStreamingStateQuery(IChatState chatState) : IStreamingStateQuery
{
    public bool HasAnyStreaming() => chatState.HasAnyStreaming();
}
