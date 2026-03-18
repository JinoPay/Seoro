using Cominomi.Shared.Resources;
using MudBlazor;

namespace Cominomi.Shared.Services;

public static class SnackbarExtensions
{
    public static void PushSuccess(this ISnackbar snackbar, string branchName)
        => snackbar.Add(Strings.Snackbar_PushSuccess(branchName), Severity.Success);

    public static void PushError(this ISnackbar snackbar, string error)
        => snackbar.Add(Strings.Snackbar_PushError(error), Severity.Error);

    public static void PrCreated(this ISnackbar snackbar, int prNumber)
        => snackbar.Add(Strings.Snackbar_PrCreated(prNumber), Severity.Success);

    public static void PrCreateError(this ISnackbar snackbar, string error)
        => snackbar.Add(Strings.Snackbar_PrCreateError(error), Severity.Error);

    public static void MergeSuccess(this ISnackbar snackbar, string branchName)
        => snackbar.Add(Strings.Snackbar_MergeSuccess(branchName), Severity.Success);

    public static void MergeError(this ISnackbar snackbar, string error)
        => snackbar.Add(Strings.Snackbar_MergeError(error), Severity.Error);

    public static void WorkspaceCreated(this ISnackbar snackbar, string name)
        => snackbar.Add(Strings.Snackbar_WorkspaceCreated(name), Severity.Success);

    public static void SettingsSaved(this ISnackbar snackbar)
        => snackbar.Add(Strings.Snackbar_SettingsSaved, Severity.Success);

    public static void SessionDeleted(this ISnackbar snackbar)
        => snackbar.Add(Strings.Snackbar_SessionDeleted, Severity.Info);

    public static void ConflictDetected(this ISnackbar snackbar, string branchName)
        => snackbar.Add(Strings.Snackbar_ConflictDetected(branchName), Severity.Warning);

    public static void StreamingError(this ISnackbar snackbar, string error)
        => snackbar.Add(Strings.Snackbar_StreamingError(error), Severity.Error);

    public static void IssueLinked(this ISnackbar snackbar, int number)
        => snackbar.Add(Strings.Snackbar_IssueLinked(number), Severity.Success);

    public static void IssueCreated(this ISnackbar snackbar, int number)
        => snackbar.Add(Strings.Snackbar_IssueCreated(number), Severity.Success);
}
