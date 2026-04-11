namespace Seoro.Shared.Services.Git;

/// <summary>
///     <see cref="IGitService.SquashMergeViaTempCloneAsync"/> 결과.
///     1단계(Alt A)에서는 충돌 시 임시 클론을 즉시 삭제하므로 <see cref="PendingConflictPath"/>는 항상 null.
///     Alt B(헤드리스 AI로 충돌 해결) 사전 호환을 위해 필드만 미리 마련해둔다.
///     — PR #245 재발 방지 주석: 이 필드에 값을 채우는 경로가 추가되기 전에는 소비자가 반드시 null로 가정해야 한다.
/// </summary>
/// <param name="Success">머지 + 푸시까지 모두 성공했는지.</param>
/// <param name="Output">git 명령의 stdout (디버깅용).</param>
/// <param name="Error">실패 시 stderr 또는 에러 요약. 성공 시 빈 문자열.</param>
/// <param name="Conflict">
///     머지 도중 충돌이 감지되어 중단되었는지 여부. <see cref="Success"/>는 false이면서
///     이 값만 true일 수 있다. 이 경우 호출자는 "세션 내 수동 해결" UX 를 보여야 한다.
/// </param>
/// <param name="ConflictingFiles">
///     <see cref="Conflict"/>가 true일 때의 충돌 예상 파일 목록. 그 외에는 빈 리스트.
/// </param>
/// <param name="PendingConflictPath">
///     Alt B 전용. Alt A 에서는 항상 null. Alt B 가 도입되기 전에는 소비자가 이 값을 무시해야 한다.
/// </param>
public sealed record SquashMergeResult(
    bool Success,
    string Output,
    string Error,
    bool Conflict,
    IReadOnlyList<string> ConflictingFiles,
    string? PendingConflictPath)
{
    public static SquashMergeResult Succeeded(string output) =>
        new(true, output, string.Empty, false, [], null);

    public static SquashMergeResult Failed(string error) =>
        new(false, string.Empty, error, false, [], null);

    public static SquashMergeResult ConflictDetected(IReadOnlyList<string> files) =>
        new(false, string.Empty, "머지 도중 충돌이 감지되어 작업을 중단했습니다.", true, files, null);
}
