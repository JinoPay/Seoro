using Cominomi.Shared.Resources;
using MudBlazor;

namespace Cominomi.Shared.Services;

public static class SnackbarExtensions
{
    public static void WorkspaceCreated(this ISnackbar snackbar, string name)
        => snackbar.Add(Strings.Snackbar_WorkspaceCreated(name), Severity.Success);

    public static void SettingsSaved(this ISnackbar snackbar)
        => snackbar.Add(Strings.Snackbar_SettingsSaved, Severity.Success);

    public static void SessionDeleted(this ISnackbar snackbar)
        => snackbar.Add(Strings.Snackbar_SessionDeleted, Severity.Info);

    public static void StreamingError(this ISnackbar snackbar, string error)
        => snackbar.Add(Strings.Snackbar_StreamingError(error), Severity.Error);

    public static void ClaudeUpdateRequired(this ISnackbar snackbar, string current, string required)
        => snackbar.Add(Strings.Snackbar_ClaudeUpdateRequired(current, required), Severity.Warning,
            opt => opt.VisibleStateDuration = 8000);

    public static void AppUpdateAvailable(this ISnackbar snackbar, string version, Action onUpdate)
        => snackbar.Add(Strings.Snackbar_AppUpdateAvailable(version), Severity.Info, opt =>
        {
            opt.VisibleStateDuration = 15000;
            opt.Action = Strings.Snackbar_AppUpdateAction;
            opt.OnClick = _ => { onUpdate(); return Task.CompletedTask; };
        });

    public static void AppUpdateReady(this ISnackbar snackbar, Action onRestart)
        => snackbar.Add(Strings.Snackbar_AppUpdateReady, Severity.Success, opt =>
        {
            opt.VisibleStateDuration = 30000;
            opt.Action = Strings.Snackbar_AppUpdateRestartAction;
            opt.OnClick = _ => { onRestart(); return Task.CompletedTask; };
        });
}
