using Microsoft.UI.Xaml;
using Serilog;

namespace Cominomi.WinUI;

public partial class App : MauiWinUIApplication
{
	public App()
	{
		this.InitializeComponent();
		this.UnhandledException += OnUnhandledException;
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		Log.Error(e.Exception, "WinUI unhandled exception: {Message}", e.Message);
		Log.CloseAndFlush();
	}
}

