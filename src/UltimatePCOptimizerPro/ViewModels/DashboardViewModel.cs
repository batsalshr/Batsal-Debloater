using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimatePCOptimizerPro.Converters;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly SystemState _state;
    private readonly AuditLogService _audit;
    private readonly RestorePointService _restorePoints;
    private readonly TweakCatalogService _catalog;
    private readonly TweakStatusService _status;
    private readonly SettingsService _settings;
    private readonly IServiceProvider _services;
    private readonly Queue<double> _cpu = new(), _gpu = new(), _ram = new(), _disk = new(), _net = new();
    private const int HistoryLength = 60;

    public override string Title => "Dashboard";
    public override string Subtitle => "Live system overview, detected bottlenecks and recommended optimizations";

    [ObservableProperty] private double[] _cpuHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _gpuHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _ramHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _diskHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _netHistory = Array.Empty<double>();

    [ObservableProperty] private string _cpuValue = "–";
    [ObservableProperty] private string _cpuDetail = "";
    [ObservableProperty] private string _gpuValue = "–";
    [ObservableProperty] private string _gpuDetail = "";
    [ObservableProperty] private string _ramValue = "–";
    [ObservableProperty] private string _ramDetail = "";
    [ObservableProperty] private string _diskValue = "–";
    [ObservableProperty] private string _netValue = "–";
    [ObservableProperty] private string _netDetail = "";

    [ObservableProperty] private string _cpuName = "Scanning…";
    [ObservableProperty] private string _gpuName = "";
    [ObservableProperty] private string _ramText = "";
    [ObservableProperty] private string _windowsText = "";
    [ObservableProperty] private string _displayText = "";
    [ObservableProperty] private string _tierText = "–";
    [ObservableProperty] private string _tierScore = "";
    [ObservableProperty] private string _profileText = "";
    [ObservableProperty] private string _restorePointText = "Checking…";
    [ObservableProperty] private string _restorePointDetail = "";
    [ObservableProperty] private bool _restorePointOk;
    [ObservableProperty] private int _recommendedCount;

    public ObservableCollection<Bottleneck> Bottlenecks { get; } = new();
    public ObservableCollection<string> Recommendations { get; } = new();
    public ObservableCollection<AuditEntry> RecentOperations { get; } = new();

    public DashboardViewModel(SystemState state, AuditLogService audit, RestorePointService restorePoints, TweakCatalogService catalog,
        TweakStatusService status, SettingsService settings, IServiceProvider services)
    {
        _state = state;
        _audit = audit;
        _restorePoints = restorePoints;
        _catalog = catalog;
        _status = status;
        _settings = settings;
        _services = services;
        _state.SampleReceived += OnSample;
        _state.SnapshotChanged += () => _ = UpdateSummaryAsync();
        _state.SystemChanged += () => _ = UpdateSummaryAsync();
        _audit.Appended += _ => System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadRecent);
    }

    protected override async Task LoadAsync()
    {
        LoadRecent();
        if (_state.Snapshot is not null) await UpdateSummaryAsync();
    }

    protected override Task OnRevisitedAsync() => UpdateSummaryAsync();

    private static double[] Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > HistoryLength) q.Dequeue();
        return q.ToArray();
    }

    private void OnSample(MonitorSample s)
    {
        CpuHistory = Push(_cpu, s.CpuPercent);
        CpuValue = $"{s.CpuPercent:0}%";
        CpuDetail = (s.CpuEffectiveMHz is { } mhz ? $"{mhz / 1000:0.00} GHz effective" : "Clock: n/a") +
                    (s.CpuPerformanceLimitPercent is < 100 and var lim ? $" · limited to {lim:0}%" : "");
        if (s.GpuPercent is { } g)
        {
            GpuHistory = Push(_gpu, g);
            GpuValue = $"{g:0}%";
        }
        else GpuValue = "n/a";
        GpuDetail = string.Join(" · ", new[]
        {
            s.GpuTemperatureC is { } t ? $"{t:0} °C" : "Temp n/a",
            s.GpuDedicatedUsedBytes is { } v ? $"VRAM {BytesConverter.Format(v)}" : null,
            s.GpuCoreClockMHz is { } c ? $"{c:0} MHz" : null
        }.Where(x => x is not null));
        RamHistory = Push(_ram, s.RamUsedPercent);
        RamValue = $"{s.RamUsedPercent:0}%";
        RamDetail = $"{BytesConverter.Format(s.RamAvailableBytes)} available · commit {BytesConverter.Format(s.CommitBytes)} / {BytesConverter.Format(s.CommitLimitBytes)}";
        if (s.DiskActivePercent is { } d) DiskHistory = Push(_disk, d);
        DiskValue = s.DiskActivePercent is { } dd ? $"{dd:0}%" : "n/a";
        var mbps = (s.NetReceiveBps + s.NetSendBps) * 8 / 1_000_000;
        NetHistory = Push(_net, mbps);
        NetValue = $"{mbps:0.0} Mb/s";
        NetDetail = $"↓ {BytesConverter.Format(s.NetReceiveBps)}/s · ↑ {BytesConverter.Format(s.NetSendBps)}/s";
    }

    private async Task UpdateSummaryAsync()
    {
        var snap = _state.Snapshot;
        if (snap is null) return;
        CpuName = $"{snap.Cpu.Name} · {snap.Cpu.PhysicalCores}C/{snap.Cpu.LogicalProcessors}T" + (snap.Cpu.IsHybrid ? $" ({snap.Cpu.PerformanceCores}P+{snap.Cpu.EfficiencyCores}E)" : "");
        GpuName = string.Join(" + ", snap.Gpus.Select(g => $"{g.Name} ({BytesConverter.Format(g.DedicatedVideoMemoryBytes)})"));
        RamText = $"{snap.Memory.TotalGB:0.#} GB {snap.Memory.PrimaryType} {(snap.Memory.ReportedSpeedMTs is { } sp ? $"@ {sp} MT/s" : "")} · {snap.Memory.Modules.Count} module(s)";
        WindowsText = $"{snap.Os.ProductName} {snap.Os.DisplayVersion} (build {snap.Os.BuildDisplay})";
        DisplayText = string.Join(" · ", snap.Displays.Select(d => $"{d.FriendlyName} {d.Width}×{d.Height} @ {d.RefreshHz} Hz"));
        _state.Reclassify();
        var tier = _state.Tier;
        if (tier is not null)
        {
            TierText = tier.TierDisplay + (tier.IsOverridden ? " (override)" : "");
            TierScore = $"Score {tier.OverallScore:0}/100";
            Bottlenecks.Clear();
            foreach (var b in tier.Bottlenecks.Take(6)) Bottlenecks.Add(b);
        }
        var profileId = _settings.Current.DefaultProfile;
        var profile = _catalog.GetProfile(profileId) ?? _catalog.Profiles.First();
        ProfileText = profile.Name;

        var tierValue = tier?.Tier ?? PcTier.MidRange;
        var suggestedProfile = _catalog.GetProfile(tierValue switch { PcTier.LowEnd => "lowend", PcTier.HighEnd => "highend", _ => "balanced" })!;
        var defs = suggestedProfile.Tweaks.Select(_catalog.Get).Where(t => t is not null && (t.Tiers.Count == 0 || t.Tiers.Contains(tierValue))).Cast<TweakDefinition>().ToList();
        var statuses = await Task.Run(() => _status.EvaluateAll(defs, snap));
        var pending = defs.Where(d => statuses[d.Id].State is TweakState.NotApplied or TweakState.Partial).ToList();
        RecommendedCount = pending.Count + snap.Displays.Count(d => d.RunningBelowMaxRefresh);
        Recommendations.Clear();
        foreach (var d in snap.Displays.Where(d => d.RunningBelowMaxRefresh)) Recommendations.Add($"Set {d.FriendlyName} to {d.MaxRefreshAtCurrentResolution} Hz");
        foreach (var t in pending.Take(6)) Recommendations.Add(t.Name);

        await Task.Run(() =>
        {
            var st = _restorePoints.GetProtectionStatus();
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                RestorePointOk = st.Enabled == true;
                RestorePointText = st.Enabled switch { true => "System Protection on", false => "System Protection OFF", _ => "Status unknown" };
                RestorePointDetail = st.LastRestorePoint is { } last
                    ? $"Last: {last.CreationTime:yyyy-MM-dd HH:mm} — {last.Description}"
                    : _state.IsElevated ? "No restore points found" : "Run as administrator to list restore points";
            });
        });
    }

    private void LoadRecent()
    {
        RecentOperations.Clear();
        foreach (var e in _audit.ReadAll().Take(5)) RecentOperations.Add(e);
    }

    [RelayCommand]
    private void SmartOptimize()
    {
        var main = (MainViewModel)_services.GetService(typeof(MainViewModel))!;
        main.NavigateTo<SmartOptimizeViewModel>();
    }

    [RelayCommand]
    private void OpenTier() => ((MainViewModel)_services.GetService(typeof(MainViewModel))!).NavigateTo<TierAnalysisViewModel>();

    [RelayCommand]
    private void OpenRecovery() => ((MainViewModel)_services.GetService(typeof(MainViewModel))!).NavigateTo<RestorePointsViewModel>();

    [RelayCommand]
    private async Task Rescan()
    {
        await _state.ScanAsync();
    }
}
