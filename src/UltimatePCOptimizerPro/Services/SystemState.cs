using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Services;

/// <summary>Shared, observable application state: the latest scan, tier classification and live monitor sample.</summary>
public sealed partial class SystemState : ObservableObject
{
    private readonly HardwareDetectionService _detector;
    private readonly HardwareTierService _tiers;
    private readonly HardwareMonitoringService _monitor;
    private readonly SettingsService _settings;
    private readonly LoggingService _log;
    private Task<SystemSnapshot>? _scanTask;

    [ObservableProperty] private SystemSnapshot? _snapshot;
    [ObservableProperty] private TierResult? _tier;
    [ObservableProperty] private MonitorSample? _latest;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanStep = "";
    [ObservableProperty] private double _scanProgress;
    [ObservableProperty] private TargetWorkload _workload = new();

    public event Action? SnapshotChanged;
    public event Action<MonitorSample>? SampleReceived;
    /// <summary>Raised after any engine operation so pages refresh their live status.</summary>
    public event Action? SystemChanged;

    public bool IsElevated { get; } = Elevation.IsElevated;

    public SystemState(HardwareDetectionService detector, HardwareTierService tiers, HardwareMonitoringService monitor, SettingsService settings, LoggingService log)
    {
        _detector = detector;
        _tiers = tiers;
        _monitor = monitor;
        _settings = settings;
        _log = log;
        _monitor.SampleReady += s => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Latest = s;
            SampleReceived?.Invoke(s);
        });
    }

    /// <summary>Runs (or joins) a hardware scan. Concurrent callers share the same scan.</summary>
    public Task<SystemSnapshot> ScanAsync()
    {
        if (_scanTask is { IsCompleted: false }) return _scanTask;
        _scanTask = RunScan();
        return _scanTask;
    }

    private async Task<SystemSnapshot> RunScan()
    {
        IsScanning = true;
        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanStep = p.Step;
                ScanProgress = p.Total == 0 ? 0 : 100.0 * p.Completed / p.Total;
            });
            var snap = await _detector.ScanAsync(progress);
            Snapshot = snap;
            _monitor.UpdateHardware(snap);
            Reclassify();
            SnapshotChanged?.Invoke();
            return snap;
        }
        catch (Exception ex)
        {
            _log.Error("Scan", "Scan failed", ex);
            throw;
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void Reclassify()
    {
        if (Snapshot is null) return;
        Tier = _tiers.Classify(Snapshot, Workload, _settings.Current.TierOverride, Latest);
    }

    public void NotifySystemChanged() => SystemChanged?.Invoke();
}
