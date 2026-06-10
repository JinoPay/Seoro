using Seoro.Shared.Resources;
using Seoro.Shared.Services.Ui;
using Seoro.Shared.UiKit;

namespace Seoro.Shared.Services.Infrastructure;

public static class SnackbarExtensions
{
    public static void AppUpdateAvailable(this IToastService snackbar, string version, Action onUpdate)
    {
        snackbar.Add(Strings.Snackbar_AppUpdateAvailable(version), ToastSeverity.Info, opt =>
        {
            opt.VisibleStateDuration = 15000;
            opt.Action = Strings.Snackbar_AppUpdateAction;
            opt.OnClick = _ =>
            {
                onUpdate();
                return Task.CompletedTask;
            };
        });
    }

    public static void AppUpdateReady(this IToastService snackbar, Action onRestart)
    {
        snackbar.Add(Strings.Snackbar_AppUpdateReady, ToastSeverity.Success, opt =>
        {
            opt.VisibleStateDuration = 30000;
            opt.Action = Strings.Snackbar_AppUpdateRestartAction;
            opt.OnClick = _ =>
            {
                onRestart();
                return Task.CompletedTask;
            };
        });
    }

    public static void ClaudeUpdateRequired(this IToastService snackbar, string current, string required)
    {
        snackbar.Add(Strings.Snackbar_ClaudeUpdateRequired(current, required), ToastSeverity.Warning,
            opt => opt.VisibleStateDuration = 8000);
    }

    public static void SessionDeleted(this IToastService snackbar)
    {
        snackbar.Add(Strings.Snackbar_SessionDeleted, ToastSeverity.Info);
    }

    public static void SettingsSaved(this IToastService snackbar)
    {
        snackbar.Add(Strings.Snackbar_SettingsSaved, ToastSeverity.Success);
    }

    public static void StreamingError(this IToastService snackbar, string error)
    {
        snackbar.Add(Strings.Snackbar_StreamingError(error), ToastSeverity.Error);
    }

    public static void WorkspaceCreated(this IToastService snackbar, string name)
    {
        snackbar.Add(Strings.Snackbar_WorkspaceCreated(name), ToastSeverity.Success);
    }

    public static void WorkspaceDeleted(this IToastService snackbar, string name)
    {
        snackbar.Add(Strings.Snackbar_WorkspaceDeleted(name), ToastSeverity.Success);
    }
}
