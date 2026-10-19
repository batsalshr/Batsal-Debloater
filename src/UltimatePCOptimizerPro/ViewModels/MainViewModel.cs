using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    public SystemState State { get; }
    public BusyService Busy { get; }

    public List<NavItem> NavItems { get; } = new()
    {
        new() { IsHeader = true, Title = "OVERVIEW" },
        new() { Title = "Dashboard", Icon = "", PageType = typeof(DashboardViewModel) },
        new() { Title = "My PC Specs", Icon = "", PageType = typeof(PcSpecsViewModel) },
        new() { Title = "Full PC Scanner", Icon = "", PageType = typeof(ScannerViewModel) },
        new() { IsHeader = true, Title = "OPTIMIZE" },
        new() { Title = "Smart Optimize", Icon = "", PageType = typeof(SmartOptimizeViewModel) },
        new() { Title = "Hardware Tier Analysis", Icon = "", PageType = typeof(TierAnalysisViewModel) },
        new() { Title = "FPS Booster", Icon = "", PageType = typeof(FpsBoosterViewModel) },
        new() { Title = "CPU Tweaks", Icon = "", PageType = typeof(CpuViewModel) },
        new() { Title = "GPU Tweaks", Icon = "", PageType = typeof(GpuViewModel) },
        new() { Title = "RAM Tweaks", Icon = "", PageType = typeof(RamViewModel) },
        new() { Title = "Network Optimizer", Icon = "", PageType = typeof(NetworkViewModel) },
        new() { Title = "Input Latency", Icon = "", PageType = typeof(InputLatencyViewModel) },
        new() { Title = "Game Profiles", Icon = "", PageType = typeof(GameProfilesViewModel) },
        new() { Title = "Benchmark Center", Icon = "", PageType = typeof(BenchmarkViewModel) },
        new() { IsHeader = true, Title = "TWEAKS & DEBLOAT" },
        new() { Title = "Windows Tweaks", Icon = "", PageType = typeof(WindowsTweaksViewModel) },
        new() { Title = "Registry Tweaks", Icon = "", PageType = typeof(RegistryTweaksViewModel) },
        new() { Title = "Windows Debloater", Icon = "", PageType = typeof(DebloatViewModel) },
        new() { Title = "Services Optimizer", Icon = "", PageType = typeof(ServicesViewModel) },
        new() { Title = "Startup Optimizer", Icon = "", PageType = typeof(StartupViewModel) },
        new() { IsHeader = true, Title = "RECOVERY" },
        new() { Title = "Restore Points", Icon = "", PageType = typeof(RestorePointsViewModel) },
        new() { Title = "Backup & Recovery", Icon = "", PageType = typeof(BackupRecoveryViewModel) },
        new() { Title = "Optimization History", Icon = "", PageType = typeof(HistoryViewModel) },
        new() { Title = "Settings", Icon = "", PageType = typeof(SettingsViewModel) },
    };

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private PageViewModel? _currentPage;

    public bool IsElevated => State.IsElevated;
    public string ElevationText => IsElevated ? "Administrator" : "Standard user (read-only)";

    public MainViewModel(IServiceProvider services, SystemState state, BusyService busy)
    {
        _services = services;
        State = state;
        Busy = busy;
        SelectedNav = NavItems[1];
    }

    partial void OnSelectedNavChanged(NavItem? oldValue, NavItem? newValue)
    {
        if (newValue is null) return;
        if (newValue.IsHeader || newValue.PageType is null)
        {
            SelectedNav = oldValue;
            return;
        }
        var page = (PageViewModel)_services.GetRequiredService(newValue.PageType);
        CurrentPage = page;
        _ = page.OnNavigatedToAsync();
    }

    public void NavigateTo<T>() where T : PageViewModel
    {
        var item = NavItems.FirstOrDefault(n => n.PageType == typeof(T));
        if (item is not null) SelectedNav = item;
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        if (Elevation.RelaunchElevated()) System.Windows.Application.Current.Shutdown();
    }
}
