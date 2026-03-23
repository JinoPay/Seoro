using Cominomi.Services;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services;
using Cominomi.Shared.Services.StreamEventHandlers;
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

        // Replace MudBlazor's ISnackbar with a deferred version to avoid
        // NavigationManager.AssertInitialized() crash during MAUI Hybrid startup
        var snackbarDescriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(ISnackbar));
        if (snackbarDescriptor != null)
            builder.Services.Remove(snackbarDescriptor);
        builder.Services.AddScoped<ISnackbar, DeferredSnackbarService>();

        // Options pattern for AppSettings (IOptionsMonitor<AppSettings>)
        builder.Services.AddSingleton<AppSettingsChangeNotifier>();
        builder.Services.AddSingleton<IOptionsChangeTokenSource<AppSettings>>(sp =>
            sp.GetRequiredService<AppSettingsChangeNotifier>());
        builder.Services.AddOptions<AppSettings>();
        builder.Services.AddSingleton<IOptionsFactory<AppSettings>, AppSettingsFactory>();

        // App Services
        builder.Services.AddSingleton<IShellService, ShellService>();
        builder.Services.AddSingleton<IActiveSessionRegistry, ActiveSessionRegistry>();
        builder.Services.AddSingleton<IChatEventBus, ChatEventBus>();
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
        builder.Services.AddSingleton<IWorkspaceService, WorkspaceService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IDependencyCheckService, DependencyCheckService>();
        builder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<IAttachmentService, AttachmentService>();
        builder.Services.AddSingleton<IPluginService, PluginService>();
        builder.Services.AddSingleton<IPluginExecutionEngine, PluginExecutionEngine>();
        builder.Services.AddSingleton<IUsageService, UsageService>();
        builder.Services.AddSingleton<IMcpService, McpService>();
        builder.Services.AddSingleton<INotificationService, NotificationService>();
        builder.Services.AddSingleton<INotificationHistoryService, NotificationHistoryService>();
        builder.Services.AddSingleton<IActivityService, ActivityService>();
        builder.Services.AddSingleton<IStreamEventHandler, SystemInitHandler>();
        builder.Services.AddSingleton<IStreamEventHandler, ContentBlockStartHandler>();
        builder.Services.AddSingleton<IStreamEventHandler, ContentBlockDeltaHandler>();
        builder.Services.AddSingleton<IStreamEventHandler, ContentBlockStopHandler>();
        builder.Services.AddSingleton<IStreamEventHandler, AssistantMessageHandler>();
        builder.Services.AddSingleton<IStreamEventHandler, UserMessageHandler>();
        builder.Services.AddSingleton<IStreamEventHandler, MessageStartHandler>();
        builder.Services.AddSingleton<IStreamEventHandler, MessageDeltaHandler>();
        builder.Services.AddSingleton<IStreamEventHandler, ResultHandler>();
        builder.Services.AddSingleton<IStreamEventHandler, ErrorHandler>();
        builder.Services.AddSingleton<IStreamEventProcessor, StreamEventProcessor>();
        builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
        builder.Services.AddSingleton<ISystemPromptBuilder, SystemPromptBuilder>();
        builder.Services.AddSingleton<ISessionInitializer, SessionInitializer>();
        builder.Services.AddSingleton<IChatMessageOrchestrator, ChatMessageOrchestrator>();
        builder.Services.AddSingleton<SessionListDataService>();
        builder.Services.AddScoped<ISessionListFacade, SessionListFacade>();
        builder.Services.AddSingleton<IThemeService, ThemeService>();

        // Load external model definitions (pricing, model names) if present
        var modelsJsonPath = Path.Combine(AppPaths.Settings, "models.json");
        ModelDefinitions.LoadFromFileAsync(modelsJsonPath).GetAwaiter().GetResult();


#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        var app = builder.Build();

        // Wire plugin execution engine and load enabled plugins
        try
        {
            var pluginService = app.Services.GetRequiredService<IPluginService>();
            var pluginEngine = app.Services.GetRequiredService<IPluginExecutionEngine>();
            if (pluginService is PluginService ps)
                ps.SetExecutionEngine(pluginEngine);
            pluginEngine.LoadAllAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Plugin engine initialization failed during startup");
        }

        return app;
    }
}
