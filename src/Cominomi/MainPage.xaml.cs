using Serilog;

namespace Cominomi;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();

		blazorWebView.BlazorWebViewInitialized += (_, args) =>
		{
#if WINDOWS
			args.WebView.CoreWebView2.ProcessFailed += (_, e) =>
			{
				Log.Error("WebView2 process failed: Kind={Kind}, Reason={Reason}",
					e.ProcessFailedKind, e.Reason);
			};
#endif
		};
	}
}
