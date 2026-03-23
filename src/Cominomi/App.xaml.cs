using Cominomi.Shared.Services;
using Serilog;

namespace Cominomi;

public partial class App : Application
{
	private readonly IServiceProvider _services;
	private bool _closeConfirmed;

	public App(IServiceProvider services)
	{
		_services = services;
		InitializeComponent();

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
		{
			if (e.ExceptionObject is Exception ex)
				Log.Fatal(ex, "AppDomain unhandled exception (IsTerminating={IsTerminating})", e.IsTerminating);
			else
				Log.Fatal("AppDomain unhandled exception: {Error}", e.ExceptionObject);
			Log.CloseAndFlush();
		};

		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			Log.Error(e.Exception, "Unobserved task exception");
		};
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "Cominomi" };

#if WINDOWS
		window.HandlerChanged += (s, e) =>
		{
			if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
			{
				nativeWindow.AppWindow.Closing += (sender, args) =>
				{
					if (_closeConfirmed) return;

					var chatState = _services.GetService<IChatState>();
					if (chatState?.HasAnyStreaming() != true) return;

					args.Cancel = true;
					_ = ShowCloseConfirmationAsync(nativeWindow, chatState);
				};
			}
		};
#endif

		return window;
	}

#if WINDOWS
	private async Task ShowCloseConfirmationAsync(
		Microsoft.UI.Xaml.Window nativeWindow, IChatState chatState)
	{
		var streamingIds = chatState.GetStreamingSessionIds();
		var registry = _services.GetService<IActiveSessionRegistry>();
		var names = streamingIds
			.Select(id => registry?.Get(id)?.CityName ?? id)
			.ToList();

		var content = names.Count > 0
			? $"진행 중인 세션이 있습니다:\n{string.Join("\n", names.Select(n => $"• {n}"))}\n\n종료하시겠습니까?"
			: "진행 중인 세션이 있습니다. 종료하시겠습니까?";

		var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
		{
			Title = "프로그램 종료",
			Content = content,
			PrimaryButtonText = "종료",
			CloseButtonText = "취소",
			XamlRoot = nativeWindow.Content.XamlRoot
		};

		if (await dialog.ShowAsync() == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
		{
			_closeConfirmed = true;
			Current?.Quit();
		}
	}
#endif

	protected override void CleanUp()
	{
		try
		{
			// Kill active Claude CLI processes to prevent orphans
			(_services.GetService<IClaudeService>() as IDisposable)?.Dispose();

			// Dispose other services that manage resources
			(_services.GetService<ChatState>() as IDisposable)?.Dispose();
			(_services.GetService<SessionListDataService>() as IDisposable)?.Dispose();
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Error during service cleanup");
		}

		Log.CloseAndFlush();
		base.CleanUp();
	}
}
