using Cominomi.Services;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using Serilog;

using NotificationService = Cominomi.Services.NotificationService;

namespace Cominomi;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var logPath = Path.Combine(FileSystem.AppDataDirectory, "logs", "cominomi-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
#if DEBUG
            .MinimumLevel.Debug()
            .WriteTo.Debug()
#endif
            .MinimumLevel.Override("Microsoft.AspNetCore.Components", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Maui", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

        // Logging
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger);

        // MudBlazor
        builder.Services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            config.SnackbarConfiguration.VisibleStateDuration = 3000;
            config.SnackbarConfiguration.ShowTransitionDuration = 200;
            config.SnackbarConfiguration.HideTransitionDuration = 200;
            config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
            config.SnackbarConfiguration.MaxDisplayedSnackbars = 3;
            config.SnackbarConfiguration.PreventDuplicates = true;
        });

        // Options pattern for AppSettings (IOptionsMonitor<AppSettings>)
        builder.Services.AddSingleton<AppSettingsChangeNotifier>();
        builder.Services.AddSingleton<IOptionsChangeTokenSource<AppSettings>>(sp =>
            sp.GetRequiredService<AppSettingsChangeNotifier>());
        builder.Services.AddOptions<AppSettings>();
        builder.Services.AddSingleton<IOptionsFactory<AppSettings>, AppSettingsFactory>();

        // App Services
        builder.Services.AddSingleton<IShellService, ShellService>();
        builder.Services.AddSingleton<IChatState, ChatState>();
        builder.Services.AddSingleton<IGitService, GitService>();
        builder.Services.AddSingleton<IGhService, GhService>();
        builder.Services.AddSingleton<IClaudeService, ClaudeService>();
        builder.Services.AddSingleton<IContextService, ContextService>();
        builder.Services.AddSingleton<IMemoryService, MemoryService>();
        builder.Services.AddSingleton<IHooksEngine, HooksEngine>();
        builder.Services.AddSingleton<ISkillRegistry, SkillRegistry>();
        builder.Services.AddSingleton<ITaskService, TaskService>();
        builder.Services.AddSingleton<ISessionService, SessionService>();
        builder.Services.AddSingleton<ISessionGitWorkflowService, SessionGitWorkflowService>();
        builder.Services.AddSingleton<IWorkspaceService, WorkspaceService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IDependencyCheckService, DependencyCheckService>();
        builder.Services.AddSingleton<ISpotlightService, SpotlightService>();
        builder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<IAttachmentService, AttachmentService>();
        builder.Services.AddSingleton<IPluginService, PluginService>();
        builder.Services.AddSingleton<IUsageService, UsageService>();
        builder.Services.AddSingleton<IMcpService, McpService>();
        builder.Services.AddSingleton<INotificationService, NotificationService>();
        builder.Services.AddSingleton<IActivityService, ActivityService>();
        builder.Services.AddSingleton<IStreamEventProcessor, StreamEventProcessor>();
        builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
        builder.Services.AddSingleton<ISystemPromptBuilder, SystemPromptBuilder>();
        builder.Services.AddSingleton<ISessionInitializer, SessionInitializer>();
        builder.Services.AddSingleton<IChatPrWorkflowService, ChatPrWorkflowService>();
        builder.Services.AddSingleton<SessionListDataService>();

        // Load external model definitions (pricing, model names) if present
        var modelsJsonPath = Path.Combine(AppPaths.Settings, "models.json");
        ModelDefinitions.LoadFromFileAsync(modelsJsonPath).GetAwaiter().GetResult();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        var app = builder.Build();

        // Recover from Spotlight crash if the app was terminated while Spotlight was active
        try
        {
            var spotlight = app.Services.GetRequiredService<ISpotlightService>();
            spotlight.RecoverAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Spotlight crash recovery failed during startup");
        }

        return app;
    }
}
