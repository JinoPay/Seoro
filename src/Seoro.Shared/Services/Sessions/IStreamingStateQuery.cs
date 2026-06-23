namespace Seoro.Shared.Services.Sessions;

/// <summary>
///     스트리밍 진행 여부에 대한 좁은 읽기 전용 쿼리. 거대한 <c>IChatState</c>에
///     직접 의존하지 않고도 다른 도메인(예: Account)이 "지금 스트리밍 중인가"만
///     물을 수 있도록 하는 중립 인터페이스.
/// </summary>
public interface IStreamingStateQuery
{
    bool HasAnyStreaming();
}
