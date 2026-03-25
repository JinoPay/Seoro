using System.Diagnostics;
using Cominomi.Desktop.Components;
using Cominomi.Desktop.Services;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services;
using Cominomi.Shared.Services.StreamEventHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using Photino.Blazor;
using Serilog;

using NotificationService = Cominomi.Desktop.Services.NotificationService;

namespace Cominomi.Desktop;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Velopack auto-update hook (must run before anything else)
        Velopack.VelopackApp.Build().Run();

        // Single instance guard
        using var mutex = new Mutex(true, "CominomiSingleInstance", out bool isNew);
        if (!isNew) return;

        // Serilog
        var logPath = Path.Combine(AppPaths.Settings, "logs", "cominomi-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
#if DEBUG
            .MinimumLevel.Debug()
            .WriteTo.Debug()
#endif
            .MinimumLevel.Override("Microsoft.AspNetCore.Components", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            RunApp(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void RunApp(string[] args)
    {
        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        // Logging
        appBuilder.Services.AddLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddSerilog(Log.Logger);
        });

        // MudBlazor
        appBuilder.Services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            config.SnackbarConfiguration.VisibleStateDuration = 3000;
            config.SnackbarConfiguration.ShowTransitionDuration = 200;
            config.SnackbarConfiguration.HideTransitionDuration = 200;
            config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
            config.SnackbarConfiguration.MaxDisplayedSnackbars = 3;
            config.SnackbarConfiguration.PreventDuplicates = true;
        });

        // Replace MudBlazor's ISnackbar with a deferred version
        var snackbarDescriptor = appBuilder.Services.FirstOrDefault(d => d.ServiceType == typeof(ISnackbar));
        if (snackbarDescriptor != null)
            appBuilder.Services.Remove(snackbarDescriptor);
        appBuilder.Services.AddScoped<ISnackbar, DeferredSnackbarService>();

        // Options pattern for AppSettings (IOptionsMonitor<AppSettings>)
        appBuilder.Services.AddSingleton<AppSettingsChangeNotifier>();
        appBuilder.Services.AddSingleton<IOptionsChangeTokenSource<AppSettings>>(sp =>
            sp.GetRequiredService<AppSettingsChangeNotifier>());
        appBuilder.Services.AddOptions<AppSettings>();
        appBuilder.Services.AddSingleton<IOptionsFactory<AppSettings>, AppSettingsFactory>();

        // Photino window accessor (set after Build)
        var windowHolder = new PhotinoWindowHolder();
        appBuilder.Services.AddSingleton(windowHolder);

        // App Services
        appBuilder.Services.AddSingleton<IShellService, ShellService>();
        appBuilder.Services.AddSingleton<IActiveSessionRegistry, ActiveSessionRegistry>();
        appBuilder.Services.AddSingleton<IChatEventBus, ChatEventBus>();
        appBuilder.Services.AddSingleton<IChatState, ChatState>();
        appBuilder.Services.AddSingleton<LightboxService>();
        appBuilder.Services.AddSingleton<IGitService, GitService>();
        appBuilder.Services.AddSingleton<IClaudeService, ClaudeService>();
        appBuilder.Services.AddSingleton<IContextService, ContextService>();
        appBuilder.Services.AddSingleton<IMemoryService, MemoryService>();
        appBuilder.Services.AddSingleton<IHooksEngine, HooksEngine>();
        appBuilder.Services.AddSingleton<ISkillRegistry, SkillRegistry>();
        appBuilder.Services.AddSingleton<ITaskService, TaskService>();
        appBuilder.Services.AddSingleton<ISessionService, SessionService>();
        appBuilder.Services.AddSingleton<IWorkspaceService, WorkspaceService>();
        appBuilder.Services.AddSingleton<ISettingsService, SettingsService>();
        appBuilder.Services.AddSingleton<IDependencyCheckService, DependencyCheckService>();
        appBuilder.Services.AddSingleton<ILauncherService, LauncherService>();
        appBuilder.Services.AddSingleton<IUpdateService, UpdateService>();
        appBuilder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();
        appBuilder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        appBuilder.Services.AddSingleton<IAttachmentService, AttachmentService>();
        appBuilder.Services.AddSingleton<IPluginService, PluginService>();
        appBuilder.Services.AddSingleton<IPluginExecutionEngine, PluginExecutionEngine>();
        appBuilder.Services.AddSingleton<IUsageService, UsageService>();
        appBuilder.Services.AddSingleton<IMcpService, McpService>();
        appBuilder.Services.AddSingleton<INotificationService, NotificationService>();
        appBuilder.Services.AddSingleton<INotificationHistoryService, NotificationHistoryService>();
        appBuilder.Services.AddSingleton<IActivityService, ActivityService>();
        appBuilder.Services.AddSingleton<IStreamEventHandler, SystemInitHandler>();
        appBuilder.Services.AddSingleton<IStreamEventHandler, ContentBlockStartHandler>();
        appBuilder.Services.AddSingleton<IStreamEventHandler, ContentBlockDeltaHandler>();
        appBuilder.Services.AddSingleton<IStreamEventHandler, ContentBlockStopHandler>();
        appBuilder.Services.AddSingleton<IStreamEventHandler, AssistantMessageHandler>();
        appBuilder.Services.AddSingleton<IStreamEventHandler, UserMessageHandler>();
        appBuilder.Services.AddSingleton<IStreamEventHandler, MessageStartHandler>();
        appBuilder.Services.AddSingleton<IStreamEventHandler, MessageDeltaHandler>();
        appBuilder.Services.AddSingleton<IStreamEventHandler, ResultHandler>();
        appBuilder.Services.AddSingleton<IStreamEventHandler, ErrorHandler>();
        appBuilder.Services.AddSingleton<IStreamEventProcessor, StreamEventProcessor>();
        appBuilder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
        appBuilder.Services.AddSingleton<ISystemPromptBuilder, SystemPromptBuilder>();
        appBuilder.Services.AddSingleton<ISessionInitializer, SessionInitializer>();
        appBuilder.Services.AddSingleton<IChatMessageOrchestrator, ChatMessageOrchestrator>();
        appBuilder.Services.AddSingleton<SessionListDataService>();
        appBuilder.Services.AddScoped<ISessionListFacade, SessionListFacade>();
        appBuilder.Services.AddSingleton<IThemeService, ThemeService>();

        // Load external model definitions
        var modelsJsonPath = Path.Combine(AppPaths.Settings, "models.json");
        ModelDefinitions.LoadFromFileAsync(modelsJsonPath).GetAwaiter().GetResult();

        // Root component
        appBuilder.RootComponents.Add<Routes>("#app");

        var app = appBuilder.Build();
        windowHolder.Window = app.MainWindow;

        // Window configuration
        app.MainWindow
            .SetTitle("Cominomi")
            .SetSize(1400, 900)
            .SetMinSize(1400, 900)
#if DEBUG
            .SetDevToolsEnabled(true)
#endif
            .SetResizable(true);

        // Open external links in the default browser
        app.MainWindow.RegisterWebMessageReceivedHandler((_, message) =>
        {
            const string prefix = "OPEN_URL:";
            if (message != null && message.StartsWith(prefix))
            {
                var url = message[prefix.Length..];
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to open external URL: {Url}", url);
                }
            }
        });

        // Window closing handler — confirm if streaming sessions exist
        app.MainWindow.WindowClosingHandler = (sender, _) =>
        {
            try
            {
                var chatState = app.Services.GetService<IChatState>();
                if (chatState?.HasAnyStreaming() != true) return false; // allow close

                // Show native confirmation dialog
                app.MainWindow.ShowMessage("Cominomi",
                    "진행 중인 세션이 있습니다. 종료하시겠습니까?");
                // ShowMessage is synchronous and blocking.
                // Since there's no Yes/No variant, we allow close after user dismisses.
                return false;
            }
            catch
            {
                return false;
            }
        };

        // Unhandled exception handlers
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Fatal(ex, "AppDomain unhandled exception (IsTerminating={IsTerminating})", e.IsTerminating);
            else
                Log.Fatal("AppDomain unhandled exception: {Error}", e.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
        };

        // Plugin initialization
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

        app.Run();

        // Cleanup after window closes
        CleanUp(app.Services);

        // Force exit — Photino may leave native threads alive on macOS
        Environment.Exit(0);
    }

    private static void CleanUp(IServiceProvider services)
    {
        try
        {
            services.GetService<IClaudeService>()?.Dispose();
            (services.GetService<ChatState>() as IDisposable)?.Dispose();
            (services.GetService<SessionListDataService>() as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during service cleanup");
        }
    }
}
