using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Engine.Handlers;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

public sealed partial class GameProfilesViewModel : PageViewModel
{
    private readonly GameDetectionService _games;
    private readonly OperationRunner _runner;
    private readonly DialogService _dialogs;
    private readonly SystemState _state;
    private readonly BackupService _backups;
    private readonly IServiceProvider _services;

    public override string Title => "Game Profiles";
    public override string Subtitle => "Per-game settings applied through supported Windows mechanisms only. No injection, no game-file edits, no anti-cheat interference.";

    public ObservableCollection<DetectedGame> Games { get; } = new();
    public ObservableCollection<string> Recommendations { get; } = new();
    public ObservableCollection<string> TierSettings { get; } = new();
    public ObservableCollection<InfoRow> CurrentState { get; } = new();

    [ObservableProperty] private DetectedGame? _selected;
    [ObservableProperty] private string _tierSettingsTitle = "";

    public GameProfilesViewModel(GameDetectionService games, OperationRunner runner, DialogService dialogs, SystemState state, BackupService backups, IServiceProvider services)
    {
        _games = games;
        _runner = runner;
        _dialogs = dialogs;
        _state = state;
        _backups = backups;
        _services = services;
        _state.SystemChanged += () => System.Windows.Application.Current.Dispatcher.InvokeAsync(FillDetails);
    }

    protected override Task LoadAsync() => Detect();

    [RelayCommand]
    private async Task Detect()
    {
        await RunBusy("Looking for installed games…", async () =>
        {
            var id = Selected?.Id;
            var list = await _games.DetectAsync();
            Games.Clear();
            foreach (var g in list) Games.Add(g);
            Selected = Games.FirstOrDefault(g => g.Id == id) ?? Games.FirstOrDefault();
            StatusMessage = Games.Count == 0 ? "No supported games detected. Add a game manually with 'Add custom game'." : "";
        });
    }

    partial void OnSelectedChanged(DetectedGame? value) => FillDetails();

    private void FillDetails()
    {
        Recommendations.Clear();
        TierSettings.Clear();
        CurrentState.Clear();
        var g = Selected;
        if (g is null) return;
        var def = g.Definition;
        if (def is not null) foreach (var r in def.Recommendations) Recommendations.Add(r);
        var tier = _state.Tier?.Tier ?? PcTier.MidRange;
        TierSettingsTitle = $"Suggested in-game settings for your {(_state.Tier?.TierDisplay ?? "PC")}";
        foreach (var s in (tier == PcTier.LowEnd ? def?.LowEndSettings : tier == PcTier.HighEnd ? def?.HighEndSettings : def?.LowEndSettings.Concat(def.HighEndSettings).Take(4)) ?? Enumerable.Empty<string>())
            TierSettings.Add(s);

        if (g.ExecutablePath is { } exe)
        {
            var pref = ReadValue(Registry.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", exe);
            var layers = ReadValue(Registry.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", exe);
            var prio = ReadValue(Registry.LocalMachine, $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{Path.GetFileName(exe)}\PerfOptions", "CpuPriorityClass");
            CurrentState.Add(new InfoRow("Executable", exe));
            CurrentState.Add(new InfoRow("GPU preference", RegistryTokenHandler.GetToken(pref, "GpuPreference") switch { "2" => "High performance", "1" => "Power saving", _ => "Let Windows decide (default)" }));
            CurrentState.Add(new InfoRow("Fullscreen optimizations", layers?.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE") == true ? "Disabled for this game" : "Enabled (Windows default)"));
            CurrentState.Add(new InfoRow("CPU priority at launch", prio switch { "3" => "High", "6" => "Above normal", "2" => "Normal", _ => "Normal (default)" }));
        }
        else CurrentState.Add(new InfoRow("Executable", "Not found — per-game settings need the game's .exe", true));
        CurrentState.Add(new InfoRow("Detected via", g.DetectedVia));
        CurrentState.Add(new InfoRow("Running now", g.IsRunning ? "Yes" : "No"));
        if (def is not null)
        {
            CurrentState.Add(new InfoRow("Anti-cheat", def.AntiCheat));
            CurrentState.Add(new InfoRow("Engine / typical limit", $"{def.Engine} · {def.Bound}"));
        }
    }

    private static string? ReadValue(RegistryKey root, string path, string name)
    {
        try { using var k = root.OpenSubKey(path); return k?.GetValue(name)?.ToString(); } catch { return null; }
    }

    private bool RequireExe(out DetectedGame g)
    {
        g = Selected!;
        if (Selected?.ExecutablePath is null || !File.Exists(Selected.ExecutablePath))
        {
            _dialogs.Info("Executable not found", "This setting needs the game's executable. Start the game once and press Detect, or add it as a custom game.");
            return false;
        }
        return true;
    }

    [RelayCommand]
    private async Task GpuHighPerformance()
    {
        if (!RequireExe(out var g)) return;
        await _runner.ApplyAsync("Game Profile", new[] { GameDetectionService.GpuPreferenceChange(g, 2) });
    }

    [RelayCommand]
    private async Task DisableFso()
    {
        if (!RequireExe(out var g)) return;
        await _runner.ApplyAsync("Game Profile", new[] { GameDetectionService.DisableFullscreenOptimizationsChange(g) });
    }

    [RelayCommand]
    private async Task Priority(string? level)
    {
        if (!RequireExe(out var g)) return;
        int cls = level == "High" ? 3 : 6;
        await _runner.ApplyAsync("Game Profile", new[] { GameDetectionService.CpuPriorityChange(g, cls) });
    }

    [RelayCommand]
    private async Task RestoreGameSettings()
    {
        if (Selected?.ExecutablePath is not { } exe) return;
        var ids = new[] { $"game.gpupref:{exe}", $"game.fso:{exe}", $"game.priority:{Path.GetFileName(exe)}" };
        bool any = false;
        foreach (var id in ids)
        {
            if (_backups.FindActiveChangesForTweak(id) is null) continue;
            any = true;
            await _runner.RestoreTweakAsync(id);
        }
        if (!any) _dialogs.Info("Nothing to restore", "No per-game setting for this game was changed by this app.");
    }

    [RelayCommand]
    private void Launch()
    {
        if (Selected is null) return;
        try { GameDetectionService.Launch(Selected); }
        catch (Exception ex) { _dialogs.Error("Cannot launch", ex.Message); }
    }

    [RelayCommand]
    private async Task AddCustom()
    {
        var path = _dialogs.OpenFile("Select the game's executable", "Programs (*.exe)|*.exe");
        if (path is null) return;
        _games.AddCustom(Path.GetFileNameWithoutExtension(path), path);
        await Detect();
    }

    [RelayCommand]
    private async Task RemoveCustom()
    {
        if (Selected is not { IsCustom: true, ExecutablePath: { } exe }) return;
        _games.RemoveCustom(exe);
        await Detect();
    }

    [RelayCommand]
    private void Benchmark()
    {
        var bench = (BenchmarkViewModel)_services.GetService(typeof(BenchmarkViewModel))!;
        if (Selected is not null) bench.ProcessName = Selected.ProcessName;
        ((MainViewModel)_services.GetService(typeof(MainViewModel))!).NavigateTo<BenchmarkViewModel>();
    }
}

public sealed partial class BenchmarkViewModel : PageViewModel
{
    private readonly BenchmarkService _bench;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly SystemState _state;
    private CancellationTokenSource? _cts;

    public override string Title => "Benchmark Center";
    public override string Subtitle => "Real frame-time capture (Intel PresentMon), system telemetry and before/after comparison. FPS numbers are never estimated.";

    public ObservableCollection<BenchmarkRun> Runs { get; } = new();
    public ObservableCollection<ComparisonRow> ComparisonRows { get; } = new();
    public ObservableCollection<string> RunningProcesses { get; } = new();
    public List<int> Durations { get; } = new() { 15, 30, 60, 90, 120, 180 };
    public List<string> Tags { get; } = new() { "Before", "After", "Baseline", "Test" };
    public string PresentMonInfo => BenchmarkService.PresentMonInfo;

    [ObservableProperty] private string? _presentMonPath;
    [ObservableProperty] private string _processName = "";
    [ObservableProperty] private int _duration = 60;
    [ObservableProperty] private string _runName = "";
    [ObservableProperty] private string _tag = "Before";
    [ObservableProperty] private bool _recordPing = true;
    [ObservableProperty] private BenchmarkRun? _selectedRun;
    [ObservableProperty] private BenchmarkRun? _compareBefore;
    [ObservableProperty] private BenchmarkRun? _compareAfter;
    [ObservableProperty] private double[] _frameTimes = Array.Empty<double>();
    [ObservableProperty] private double[] _cpuSeries = Array.Empty<double>();
    [ObservableProperty] private double[] _gpuSeries = Array.Empty<double>();
    [ObservableProperty] private double[] _ramSeries = Array.Empty<double>();
    [ObservableProperty] private double[] _pingSeries = Array.Empty<double>();
    [ObservableProperty] private string _runDetails = "";
    [ObservableProperty] private bool _isCapturing;

    public BenchmarkViewModel(BenchmarkService bench, SettingsService settings, DialogService dialogs, SystemState state)
    {
        _bench = bench;
        _settings = settings;
        _dialogs = dialogs;
        _state = state;
    }

    protected override Task LoadAsync()
    {
        PresentMonPath = _bench.FindPresentMon();
        LoadRuns();
        RefreshProcesses();
        return Task.CompletedTask;
    }

    private void LoadRuns()
    {
        Runs.Clear();
        foreach (var r in _bench.LoadAll()) Runs.Add(r);
        SelectedRun ??= Runs.FirstOrDefault();
    }

    [RelayCommand]
    private void RefreshProcesses()
    {
        RunningProcesses.Clear();
        foreach (var p in Process.GetProcesses().Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.SessionId != 0; } catch { return false; } })
                     .Select(p => p.ProcessName + ".exe").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n))
            RunningProcesses.Add(p);
    }

    [RelayCommand]
    private void BrowsePresentMon()
    {
        var path = _dialogs.OpenFile("Select PresentMon console executable", "PresentMon (PresentMon*.exe)|PresentMon*.exe|Programs (*.exe)|*.exe");
        if (path is null) return;
        PresentMonPath = path;
        _settings.Current.PresentMonPath = path;
        _settings.Save();
    }

    [RelayCommand] private void OpenPresentMonPage() => Process.Start(new ProcessStartInfo("https://github.com/GameTechDev/PresentMon/releases") { UseShellExecute = true });

    [RelayCommand]
    private async Task Capture()
    {
        if (PresentMonPath is not { } presentMon || !File.Exists(presentMon)) { _dialogs.Info("PresentMon not found", PresentMonInfo); return; }
        if (string.IsNullOrWhiteSpace(ProcessName)) { _dialogs.Info("Select the game", "Choose the game's process (it must be running)."); return; }
        var proc = ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ProcessName : ProcessName + ".exe";
        _cts = new CancellationTokenSource();
        IsCapturing = true;
        try
        {
            await RunBusy($"Capturing {Duration} s from {proc}… keep playing the same scene.", async () =>
            {
                var progress = new Progress<string>(m => StatusMessage = m);
                var run = await _bench.CapturePresentMonAsync(presentMon, proc, Duration, string.IsNullOrWhiteSpace(RunName) ? $"{proc} {Tag}" : RunName, Tag,
                    RecordPing ? _settings.Current.PingTarget : null, progress, _cts.Token);
                run.SystemSummary = SystemSummary();
                _bench.Save(run);
                LoadRuns();
                SelectedRun = Runs.FirstOrDefault(r => r.Id == run.Id);
                StatusMessage = $"Captured {run.Frames!.FrameCount:N0} frames: {run.Frames.AverageFps:0.0} avg FPS, {run.Frames.OnePercentLowFps:0.0} 1% low.";
            });
        }
        finally { IsCapturing = false; }
    }

    [RelayCommand]
    private async Task RecordTelemetry()
    {
        _cts = new CancellationTokenSource();
        IsCapturing = true;
        try
        {
            await RunBusy($"Recording system telemetry for {Duration} s…", async () =>
            {
                var run = await _bench.RecordTelemetryOnlyAsync(Duration, string.IsNullOrWhiteSpace(RunName) ? $"Telemetry {Tag}" : RunName, Tag,
                    RecordPing ? _settings.Current.PingTarget : null, _cts.Token);
                run.SystemSummary = SystemSummary();
                _bench.Save(run);
                LoadRuns();
                SelectedRun = Runs.FirstOrDefault(r => r.Id == run.Id);
                StatusMessage = "Telemetry recorded (no frame data — use PresentMon capture for FPS).";
            });
        }
        finally { IsCapturing = false; }
    }

    [RelayCommand] private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private void Import()
    {
        var path = _dialogs.OpenFile("Import frame-time CSV (PresentMon, CapFrameX, OCAT)", "CSV files (*.csv)|*.csv");
        if (path is null) return;
        try
        {
            var run = _bench.ImportCsv(path, Path.GetFileNameWithoutExtension(path), Tag);
            LoadRuns();
            SelectedRun = Runs.FirstOrDefault(r => r.Id == run.Id);
        }
        catch (Exception ex) { _dialogs.Error("Import failed", ex.Message); }
    }

    partial void OnSelectedRunChanged(BenchmarkRun? value)
    {
        if (value is null) { FrameTimes = Array.Empty<double>(); RunDetails = ""; return; }
        FrameTimes = value.FrameTimesMs.Select(f => (double)f).ToArray();
        CpuSeries = value.Telemetry.Select(t => t.Cpu).ToArray();
        GpuSeries = value.Telemetry.Select(t => t.Gpu ?? 0).ToArray();
        RamSeries = value.Telemetry.Select(t => t.Ram).ToArray();
        PingSeries = value.Telemetry.Select(t => t.PingMs ?? 0).ToArray();
        var f = value.Frames;
        RunDetails = (f is null ? "No frame data (telemetry-only run)." :
                         $"Frames {f.FrameCount:N0} over {f.DurationSeconds:0.0} s · Avg {f.AverageFps:0.0} FPS · 1% low {f.OnePercentLowFps:0.0} · 0.1% low {f.PointOnePercentLowFps:0.0} · " +
                         $"1% low (avg method) {f.OnePercentLowAvgMethodFps:0.0}\nFrame time avg {f.AverageFrameTimeMs:0.00} ms · median {f.MedianFrameTimeMs:0.00} · 99th {f.P99FrameTimeMs:0.00} · max {f.MaxFrameTimeMs:0.0} · std dev {f.FrameTimeStdDevMs:0.00} · stutters {f.StutterCount}") +
                     (value.Ping is { } p ? $"\nPing {p.Target}: avg {p.AverageMs:0.0} ms · jitter {p.JitterMs:0.0} ms · loss {p.LossPercent:0.#}%" : "") +
                     $"\nSource: {value.Source}{(value.SourceFile is null ? "" : " · " + value.SourceFile)}";
    }

    [RelayCommand]
    private void Compare()
    {
        if (CompareBefore is null || CompareAfter is null || CompareBefore == CompareAfter) { _dialogs.Info("Select two runs", "Choose a 'before' and an 'after' run."); return; }
        var c = BenchmarkService.Compare(CompareBefore, CompareAfter);
        ComparisonRows.Clear();
        foreach (var r in c.Rows) ComparisonRows.Add(r);
    }

    [RelayCommand]
    private void ExportReport()
    {
        var path = _dialogs.SaveFile("Export benchmark report", "HTML report (*.html)|*.html", $"benchmark-report-{DateTime.Now:yyyyMMdd-HHmm}.html");
        if (path is null) return;
        var cmp = CompareBefore is not null && CompareAfter is not null && CompareBefore != CompareAfter ? BenchmarkService.Compare(CompareBefore, CompareAfter) : null;
        _bench.ExportHtmlReport(Runs, cmp, path, SystemSummary());
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void DeleteRun()
    {
        if (SelectedRun is null) return;
        if (!_dialogs.Confirm("Delete run?", $"Delete '{SelectedRun.Name}' from benchmark history?", "Delete", "Cancel", danger: true)) return;
        _bench.Delete(SelectedRun);
        SelectedRun = null;
        LoadRuns();
    }

    private string SystemSummary()
    {
        var s = _state.Snapshot;
        return s is null ? "" : $"{s.Cpu.Name} | {s.PrimaryGpu?.Name} (driver {s.PrimaryGpu?.DriverVersion}) | {s.Memory.TotalGB:0} GB {s.Memory.PrimaryType} | {s.Os.ProductName} {s.Os.BuildDisplay} | {s.Power.ActivePlanName}";
    }
}
