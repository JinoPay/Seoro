using System.Diagnostics;
using System.Runtime.InteropServices;
using Seoro.Desktop.Components;
using Seoro.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using Photino.Blazor;
using Serilog;
using Serilog.Events;
using Velopack;
using Seoro.Shared.Services.Cli;
using NotificationService = Seoro.Desktop.Services.NotificationService;

namespace Seoro.Desktop;

public static class Program
{
    private static volatile bool _flushed;

    private static void FlushLogs()
    {
        if (_flushed) return;
        _flushed = true;
        Log.CloseAndFlush();
    }

    private static string GetIconPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var iconFile = OperatingSystem.IsWindows() ? "icon.ico" : "icon.png";
        return Path.Combine(baseDir, iconFile);
    }

    private static void CleanUp(IServiceProvider services)
    {
        try
        {
            services.GetService<IWorktreeSyncService>()?.Dispose();
            services.GetService<IClaudeService>()?.Dispose();
            services.GetService<IGitBranchWatcherService>()?.Dispose();
            services.GetService<IConflictWatcherService>()?.Dispose();
            services.GetService<IMergeStatusService>()?.Dispose();
            (services.GetService<ChatState>() as IDisposable)?.Dispose();
            (services.GetService<SessionListDataService>() as IDisposable)?.Dispose();

            if (services.GetService<ITerminalService>() is IAsyncDisposable termDisposable)
                termDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during service cleanup");
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        // macOS: prevent SIGSEGV when Process.Start() falls back to fork() in a
        // multi-threaded process (Photino/AppKit/WebKit threads).  The Objective-C
        // runtime's +initialize dispatch in the forked child can hit corrupted state;
        // this env var disables that dispatch (safe because exec() follows immediately).
        if (OperatingSystem.IsMacOS())
            Environment.SetEnvironmentVariable("OBJC_DISABLE_INITIALIZE_FORK_SAFETY", "YES");

        // Velopack auto-update hook (must run before anything else)
        VelopackApp.Build().Run();

        // Single instance guard
        using var mutex = new Mutex(true, "SeoroSingleInstance", out var isNew);
        if (!isNew) return;

        // Serilog
        var logPath = Path.Combine(AppPaths.Settings, "logs", "seoro-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
#if DEBUG
            .MinimumLevel.Debug()
            .WriteTo.Debug()
#endif
            .MinimumLevel.Override("Microsoft.AspNetCore.Components", LogEventLevel.Warning)
            .WriteTo.Console(
                outputTemplate:
                "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Unhandled exception handlers — registered here (before RunApp) so they're active
        // even if Build() or early initialization crashes on a background thread.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Fatal(ex, "AppDomain unhandled exception (IsTerminating={IsTerminating})", e.IsTerminating);
            else
                Log.Fatal("AppDomain unhandled exception: {Error}", e.ExceptionObject);

            // Main's finally block does NOT run for background-thread crashes,
            // so we must flush here before the CLR kills the process.
            if (e.IsTerminating)
                FlushLogs();
        };

        // Belt-and-suspenders: also flush on normal process exit (including Environment.Exit).
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { FlushLogs(); };

        // POSIX signals (SIGTERM from OS shutdown / kill, SIGINT from Ctrl+C in dev terminal).
        if (!OperatingSystem.IsWindows())
        {
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ =>
            {
                Log.Information("Received SIGTERM, shutting down");
                FlushLogs();
            });
            PosixSignalRegistration.Create(PosixSignal.SIGINT, _ =>
            {
                Log.Information("Received SIGINT, shutting down");
                FlushLogs();
            });
        }

        // Mark unobserved task exceptions as observed after logging to prevent a secondary crash.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

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
            FlushLogs();
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
        appBuilder.Services.AddSingleton<IGitBranchWatcherService, GitBranchWatcherService>();
        appBuilder.Services.AddSingleton<IConflictWatcherService, ConflictWatcherService>();
        appBuilder.Services.AddSingleton<IMergeStatusService, MergeStatusService>();
        appBuilder.Services.AddSingleton<IWorktreeSyncService, WorktreeSyncService>();
        appBuilder.Services.AddSingleton<ClaudeService>();
        appBuilder.Services.AddSingleton<IClaudeService>(sp => sp.GetRequiredService<ClaudeService>());
        appBuilder.Services.AddSingleton<Seoro.Shared.Services.Codex.CodexService>();
        // ICliProvider로 두 구현체 모두 등록 (CliProviderFactory가 IEnumerable<ICliProvider> 주입받음)
        appBuilder.Services.AddSingleton<ICliProvider>(sp => sp.GetRequiredService<ClaudeService>());
        appBuilder.Services.AddSingleton<ICliProvider>(sp => sp.GetRequiredService<Seoro.Shared.Services.Codex.CodexService>());
        appBuilder.Services.AddSingleton<ICliProviderFactory, CliProviderFactory>();
        appBuilder.Services.AddSingleton<ICliAvailabilityService, CliAvailabilityService>();
        appBuilder.Services.AddSingleton<IContextService, ContextService>();
        appBuilder.Services.AddSingleton<IMemoryService, MemoryService>();
        appBuilder.Services.AddSingleton<IHooksEngine, HooksEngine>();
        appBuilder.Services.AddSingleton<ISkillRegistry, SkillRegistry>();
        appBuilder.Services.AddSingleton<ITaskService, TaskService>();
        appBuilder.Services.AddSingleton<ISessionService, SessionService>();
        appBuilder.Services.AddSingleton<IWorkspaceService, WorkspaceService>();
        appBuilder.Services.AddSingleton<ISettingsService, SettingsService>();
        appBuilder.Services.AddSingleton<CominomiMigrationService>();
        appBuilder.Services.AddSingleton<IDependencyCheckService, DependencyCheckService>();
        appBuilder.Services.AddSingleton<ILauncherService, LauncherService>();
        appBuilder.Services.AddSingleton<IUpdateService, UpdateService>();
        appBuilder.Services.AddSingleton<IReleaseNotesService, ReleaseNotesService>();
        appBuilder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();
        appBuilder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        appBuilder.Services.AddSingleton<IAttachmentService, AttachmentService>();
        appBuilder.Services.AddSingleton<IPluginService, PluginService>();
        appBuilder.Services.AddSingleton<IPluginExecutionEngine, PluginExecutionEngine>();
        appBuilder.Services.AddSingleton<IStatsCacheService, StatsCacheService>();
        appBuilder.Services.AddSingleton<IMcpService, McpService>();
        appBuilder.Services.AddSingleton<INotificationService, NotificationService>();
        appBuilder.Services.AddSingleton<INotificationHistoryService, NotificationHistoryService>();
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
        appBuilder.Services.AddSingleton<ITerminalService, TerminalService>();
        appBuilder.Services.AddSingleton<ISystemPromptBuilder, SystemPromptBuilder>();
        appBuilder.Services.AddSingleton<ISessionInitializer, SessionInitializer>();
        appBuilder.Services.AddSingleton<IChatMessageOrchestrator, ChatMessageOrchestrator>();
        appBuilder.Services.AddSingleton<SessionListDataService>();
        appBuilder.Services.AddScoped<ISessionListFacade, SessionListFacade>();
        appBuilder.Services.AddSingleton<IThemeService, ThemeService>();
        appBuilder.Services.AddSingleton<HttpClient>();
        appBuilder.Services.AddSingleton<IClaudeCredentialService, ClaudeCredentialService>();
        appBuilder.Services.AddSingleton<IClaudeAccountService, ClaudeAccountService>();
        appBuilder.Services.AddSingleton<ISaveFilePickerService, SaveFilePickerService>();
        appBuilder.Services.AddSingleton<IClaudeSettingsService, ClaudeSettingsService>();
        appBuilder.Services.AddSingleton<IRulesService, RulesService>();
        appBuilder.Services.AddSingleton<IInstructionsService, InstructionsService>();
        appBuilder.Services.AddSingleton<IGamificationService, GamificationService>();
        appBuilder.Services.AddSingleton<ISessionReplayService, SessionReplayService>();
        appBuilder.Services.AddSingleton<IWindowCloseGuardService, WindowCloseGuardService>();

        // Load external model definitions
        var modelsJsonPath = Path.Combine(AppPaths.Settings, "models.json");
        ModelDefinitions.LoadFromFileAsync(modelsJsonPath).GetAwaiter().GetResult();

        // Root component
        appBuilder.RootComponents.Add<Routes>("#app");

        var app = appBuilder.Build();
        windowHolder.Window = app.MainWindow;

        // Window configuration
        app.MainWindow
            .SetLogVerbosity(0)
            .SetTitle("Seoro")
            .SetIconFile(GetIconPath())
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

        // Window closing handler — block close and show in-app confirm dialog when streaming
        app.MainWindow.WindowClosingHandler = (sender, _) =>
        {
            try
            {
                var chatState = app.Services.GetService<IChatState>();
                if (chatState?.HasAnyStreaming() != true) return false; // allow close

                // Block the OS close and let the Blazor UI handle the confirmation dialog
                var eventBus = app.Services.GetService<IChatEventBus>();
                eventBus?.Publish(new WindowCloseRequestedEvent());
                return true; // cancel close
            }
            catch
            {
                return false;
            }
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

        // Recover from any orphaned sync state (crash recovery)
        try
        {
            app.Services.GetRequiredService<IWorktreeSyncService>().RecoverFromCrashAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Worktree sync crash recovery failed");
        }

        app.Run();

        // Cleanup after window closes
        CleanUp(app.Services);

        // Force exit — Photino may leave native threads alive on macOS
        Environment.Exit(0);
    }
}