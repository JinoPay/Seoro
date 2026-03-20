using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace Cominomi.WinUI;

public static class Program
{
    [STAThread]
    static async Task<int> Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var isRedirect = await HandleSingleInstanceAsync();
        if (isRedirect)
            return 0;

        Microsoft.UI.Xaml.Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);

            new App();
        });

        return 0;
    }

    private static async Task<bool> HandleSingleInstanceAsync()
    {
        var mainInstance = AppInstance.FindOrRegisterForKey("CominomiMain");
        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();

        if (!mainInstance.IsCurrent)
        {
            // 이미 실행 중인 인스턴스로 활성화를 리다이렉트하고 종료
            await mainInstance.RedirectActivationToAsync(activationArgs);
            return true;
        }

        // 첫 번째 인스턴스 — 이후 리다이렉트를 수신
        mainInstance.Activated += OnActivatedFromRedirect;
        return false;
    }

    internal static event EventHandler<AppActivationArguments>? ActivationRedirected;

    private static void OnActivatedFromRedirect(object? sender, AppActivationArguments args)
    {
        ActivationRedirected?.Invoke(sender, args);
    }
}
