using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Serilog;

namespace Cominomi.WinUI;

public partial class App : MauiWinUIApplication
{
	public App()
	{
		this.InitializeComponent();
		this.UnhandledException += OnUnhandledException;

		// 두 번째 인스턴스가 리다이렉트한 활성화를 수신
		Program.ActivationRedirected += OnActivationRedirected;
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	private void OnActivationRedirected(object? sender, AppActivationArguments args)
	{
		// 백그라운드 스레드에서 호출됨 → UI 스레드로 디스패치
		MainThread.BeginInvokeOnMainThread(BringExistingWindowToFront);
	}

	private static void BringExistingWindowToFront()
	{
		try
		{
			var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
			if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
				WindowHelper.BringToForeground(nativeWindow);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Failed to bring window to foreground on activation redirect");
		}
	}

	private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		Log.Error(e.Exception, "WinUI unhandled exception: {Message}", e.Message);
		Log.CloseAndFlush();
	}
}

