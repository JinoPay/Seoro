namespace Seoro.Shared.Services.Git;

/// <summary>
///     <c>git merge-tree</c> 기반 비파괴 머지 시뮬레이션 결과.
///     실제 ref/working tree 를 건드리지 않고 "지금 머지하면 충돌이 날까?"를
///     계산한다. <see cref="MergeStatusService"/>가 라이브 상태 추적에 활용.
/// </summary>
/// <param name="Success">merge-tree 명령 자체가 정상 실행되었는지 여부. false면 git 버전 문제 등이 가능.</param>
/// <param name="WouldConflict">시뮬레이션 결과 충돌이 발생할지 여부.</param>
/// <param name="ConflictingFiles">충돌 예상 파일 상대 경로 목록. <see cref="WouldConflict"/>가 false면 빈 리스트.</param>
/// <param name="AheadCount">source 가 target 보다 앞선 커밋 수.</param>
/// <param name="BehindCount">target 이 source 보다 앞선 커밋 수 (stale 경고 기준).</param>
/// <param name="ErrorMessage"><see cref="Success"/>가 false일 때의 에러 요약.</param>
public sealed record MergeSimulationResult(
    bool Success,
    bool WouldConflict,
    IReadOnlyList<string> ConflictingFiles,
    int AheadCount,
    int BehindCount,
    string? ErrorMessage)
{
    public static MergeSimulationResult Failed(string error) =>
        new(false, false, [], 0, 0, error);
}
