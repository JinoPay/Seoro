using MudBlazor;

namespace Cominomi.Shared.Services;

public static class SnackbarExtensions
{
    public static void PushSuccess(this ISnackbar snackbar, string branchName)
        => snackbar.Add($"'{branchName}' 브랜치가 푸시되었습니다.", Severity.Success);

    public static void PushError(this ISnackbar snackbar, string error)
        => snackbar.Add($"푸시 실패: {error}", Severity.Error);

    public static void PrCreated(this ISnackbar snackbar, int prNumber)
        => snackbar.Add($"PR #{prNumber} 생성 완료", Severity.Success);

    public static void PrCreateError(this ISnackbar snackbar, string error)
        => snackbar.Add($"PR 생성 실패: {error}", Severity.Error);

    public static void MergeSuccess(this ISnackbar snackbar, string branchName)
        => snackbar.Add($"'{branchName}' PR이 병합되었습니다.", Severity.Success);

    public static void MergeError(this ISnackbar snackbar, string error)
        => snackbar.Add($"병합 실패: {error}", Severity.Error);

    public static void WorkspaceCreated(this ISnackbar snackbar, string name)
        => snackbar.Add($"'{name}' 워크스페이스가 생성되었습니다.", Severity.Success);

    public static void SettingsSaved(this ISnackbar snackbar)
        => snackbar.Add("설정이 저장되었습니다.", Severity.Success);

    public static void SessionDeleted(this ISnackbar snackbar)
        => snackbar.Add("세션이 삭제되었습니다.", Severity.Info);

    public static void ConflictDetected(this ISnackbar snackbar, string branchName)
        => snackbar.Add($"'{branchName}' 브랜치에서 병합 충돌이 감지되었습니다.", Severity.Warning);

    public static void StreamingError(this ISnackbar snackbar, string error)
        => snackbar.Add($"응답 오류: {error}", Severity.Error);
}
