using Cominomi.Shared.Services;
using Serilog;

namespace Cominomi;

public partial class App : Application
{
	private readonly IServiceProvider _services;

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
		return new Window(new MainPage()) { Title = "Cominomi" };
	}

	protected override void CleanUp()
	{
		try
		{
			// Kill active Claude CLI processes to prevent orphans
			(_services.GetService<IClaudeService>() as IDisposable)?.Dispose();

			// Dispose other services that manage resources
			(_services.GetService<ISpotlightService>() as IDisposable)?.Dispose();
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
