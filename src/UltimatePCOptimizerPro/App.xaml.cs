using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using UltimatePCOptimizerPro.Core.Engine;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;
using UltimatePCOptimizerPro.ViewModels;

namespace UltimatePCOptimizerPro;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Services = ConfigureServices();
        var log = Services.GetRequiredService<LoggingService>();
        log.Info("App", $"Starting {typeof(App).Assembly.GetName().Version} (elevated: {Elevation.IsElevated}, OS {Environment.OSVersion})");

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => log.Error("App", "Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.Error("App", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        var settings = Services.GetRequiredService<SettingsService>();
        if (!settings.Current.UsePurpleAccents) ApplyCyanOnlyAccents();
        var monitor = Services.GetRequiredService<HardwareMonitoringService>();
        monitor.IntervalMs = settings.Current.DashboardRefreshMs;

        var window = new MainWindow { DataContext = Services.GetRequiredService<MainViewModel>() };
        MainWindow = window;
        window.Show();

        monitor.Start();
        var state = Services.GetRequiredService<SystemState>();
        if (settings.Current.ScanOnStartup) _ = state.ScanAsync();
        if (settings.Current.HardwareRescanMinutes > 0)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(settings.Current.HardwareRescanMinutes) };
            timer.Tick += (_, _) => _ = state.ScanAsync();
            timer.Start();
        }
    }

    /// <summary>Theme customisation: replaces the purple accent with blue/cyan before any view is created.</summary>
    private void ApplyCyanOnlyAccents()
    {
        var cyan = (System.Windows.Media.Color)Resources["CyanColor"];
        var accent = (System.Windows.Media.Color)Resources["AccentColor"];
        Resources["PurpleBrush"] = new System.Windows.Media.SolidColorBrush(cyan);
        Resources["PurpleSoftBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, cyan.R, cyan.G, cyan.B));
        var gradient = new System.Windows.Media.LinearGradientBrush(accent, cyan, 0);
        Resources["AccentGradientBrush"] = gradient;
        Resources["HeroGradientBrush"] = new System.Windows.Media.LinearGradientBrush(
            System.Windows.Media.Color.FromArgb(0x1A, accent.R, accent.G, accent.B), System.Windows.Media.Color.FromArgb(0x0D, 0x11, 0x17, 0x20), 45);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Services.GetRequiredService<LoggingService>().Error("UI", "Unhandled UI exception", e.Exception);
        Services.GetRequiredService<DialogService>().Error("Unexpected error",
            $"{e.Exception.Message}\n\nNo system change is in progress unless an operation dialog said so. Details were written to the log in {Core.AppPaths.Logs}.");
        e.Handled = true;
    }

    private static ServiceProvider ConfigureServices()
    {
        var s = new ServiceCollection();

        // Core services
        s.AddSingleton<LoggingService>();
        s.AddSingleton<SettingsService>();
        s.AddSingleton<HardwareDetectionService>();
        s.AddSingleton<HardwareMonitoringService>();
        s.AddSingleton<HardwareTierService>();
        s.AddSingleton<RestorePointService>();
        s.AddSingleton<IRestorePointProvider, WindowsRestorePointProvider>();
        s.AddSingleton<BackupService>();
        s.AddSingleton<AuditLogService>();
        s.AddSingleton(sp => HandlerRegistry.CreateDefault(sp.GetRequiredService<LoggingService>()));
        s.AddSingleton(sp => new OptimizationEngine(sp.GetRequiredService<HandlerRegistry>(), sp.GetRequiredService<IRestorePointProvider>(),
            sp.GetRequiredService<BackupService>(), sp.GetRequiredService<AuditLogService>(), sp.GetRequiredService<SettingsService>(), sp.GetRequiredService<LoggingService>()));
        s.AddSingleton<TweakCatalogService>();
        s.AddSingleton<TweakStatusService>();
        s.AddSingleton<RecommendationEngine>();
        s.AddSingleton<WindowsDebloatService>();
        s.AddSingleton<StartupManagerService>();
        s.AddSingleton<WindowsServiceManager>();
        s.AddSingleton<ProcessMonitorService>();
        s.AddSingleton<CleanupService>();
        s.AddSingleton<NetworkDiagnosticsService>();
        s.AddSingleton<BenchmarkService>();
        s.AddSingleton<GameDetectionService>();

        // UI services
        s.AddSingleton<SystemState>();
        s.AddSingleton<BusyService>();
        s.AddSingleton<DialogService>();
        s.AddSingleton<OperationRunner>();

        // View models (pages keep their state while the app runs)
        s.AddSingleton<MainViewModel>();
        s.AddTransient<TweakListViewModel>();
        s.AddSingleton<DashboardViewModel>();
        s.AddSingleton<PcSpecsViewModel>();
        s.AddSingleton<ScannerViewModel>();
        s.AddSingleton<SmartOptimizeViewModel>();
        s.AddSingleton<TierAnalysisViewModel>();
        s.AddSingleton<FpsBoosterViewModel>();
        s.AddSingleton<CpuViewModel>();
        s.AddSingleton<GpuViewModel>();
        s.AddSingleton<RamViewModel>();
        s.AddSingleton<WindowsTweaksViewModel>();
        s.AddSingleton<RegistryTweaksViewModel>();
        s.AddSingleton<DebloatViewModel>();
        s.AddSingleton<ServicesViewModel>();
        s.AddSingleton<StartupViewModel>();
        s.AddSingleton<NetworkViewModel>();
        s.AddSingleton<InputLatencyViewModel>();
        s.AddSingleton<GameProfilesViewModel>();
        s.AddSingleton<BenchmarkViewModel>();
        s.AddSingleton<RestorePointsViewModel>();
        s.AddSingleton<BackupRecoveryViewModel>();
        s.AddSingleton<HistoryViewModel>();
        s.AddSingleton<SettingsViewModel>();
        return s.BuildServiceProvider();
    }
}
