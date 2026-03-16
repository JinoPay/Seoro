using Serilog;

namespace Cominomi;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage()) { Title = "Cominomi" };
	}

	protected override void CleanUp()
	{
		Log.CloseAndFlush();
		base.CleanUp();
	}
}
