using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

public sealed partial class ProfileOption : ObservableObject
{
    public OptimizationProfile Profile { get; init; } = new();
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isSuggested;
    public string Name => Profile.Name;
    public string Description => Profile.Description;
    public string RiskText => $"Max risk: {Profile.MaxRisk}";
    public int Count => Profile.Tweaks.Count;
}

public sealed partial class PlanItemViewModel : ObservableObject
{
    public PlanItem Item { get; init; } = new();
    [ObservableProperty] private bool _selected = true;
    public string Title => Item.Title;
    public string Category => Item.Category;
    public string Reason => Item.Reason;
    public string Benefit => Item.Benefit;
    public string Drawback => Item.Drawback;
    public RiskLevel Risk => Item.Risk;
    public string RestartText => DialogService.RestartText(Item.Restart);
}

public sealed partial class SmartOptimizeViewModel : PageViewModel
{
    private readonly SystemState _state;
    private readonly TweakCatalogService _catalog;
    private readonly RecommendationEngine _engine;
    private readonly OperationRunner _runner;
    private readonly DialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly IServiceProvider _services;

    public override string Title => "Smart Optimize";
    public override string Subtitle => "Scan → detect tier and bottlenecks → hardware-specific plan → review → restore point → apply → verify";

    public ObservableCollection<ProfileOption> Profiles { get; } = new();
    public ObservableCollection<PlanItemViewModel> Plan { get; } = new();
    public ObservableCollection<ExcludedItem> Excluded { get; } = new();
    public ObservableCollection<string> Notes { get; } = new();

    [ObservableProperty] private ProfileOption? _selectedProfile;
    [ObservableProperty] private bool _extremeAcknowledged;
    [ObservableProperty] private bool _hasPlan;
    [ObservableProperty] private string _planSummary = "";
    [ObservableProperty] private string _tierSummary = "";
    [ObservableProperty] private string _resultSummary = "";
    [ObservableProperty] private bool _showBenchmarkOffer;

    public bool RequiresAck => SelectedProfile?.Profile.RequiresExplicitOptIn == true;

    public SmartOptimizeViewModel(SystemState state, TweakCatalogService catalog, RecommendationEngine engine, OperationRunner runner, DialogService dialogs,
        SettingsService settings, IServiceProvider services)
    {
        _state = state;
        _catalog = catalog;
        _engine = engine;
        _runner = runner;
        _dialogs = dialogs;
        _settings = settings;
        _services = services;
        foreach (var p in catalog.Profiles) Profiles.Add(new ProfileOption { Profile = p });
        Select(Profiles.FirstOrDefault(p => p.Profile.Id == settings.Current.DefaultProfile) ?? Profiles.FirstOrDefault());
    }

    protected override async Task LoadAsync()
    {
        if (_state.Snapshot is null) await _state.ScanAsync();
        SuggestProfile();
    }

    private void SuggestProfile()
    {
        _state.Reclassify();
        var tier = _state.Tier;
        if (tier is null) return;
        var id = tier.Tier switch { PcTier.LowEnd => "lowend", PcTier.HighEnd => "highend", _ => "balanced" };
        foreach (var p in Profiles) p.IsSuggested = p.Profile.Id == id;
        TierSummary = $"Detected: {tier.TierDisplay} (score {tier.OverallScore:0}). Suggested profile: {Profiles.First(p => p.IsSuggested).Name}. {tier.LikelyLimiter}.";
    }

    [RelayCommand]
    private void Select(ProfileOption? option)
    {
        if (option is null) return;
        foreach (var p in Profiles) p.IsSelected = p == option;
        SelectedProfile = option;
        ExtremeAcknowledged = false;
        OnPropertyChanged(nameof(RequiresAck));
        HasPlan = false;
        Plan.Clear();
    }

    [RelayCommand]
    private async Task Generate()
    {
        if (SelectedProfile is null) return;
        if (RequiresAck && !ExtremeAcknowledged)
        {
            _dialogs.Info("Explicit opt-in required", "Extreme Advanced contains experimental settings with limited evidence. Tick the acknowledgement checkbox to continue.");
            return;
        }
        await RunBusy("Scanning hardware and evaluating current settings…", async () =>
        {
            await _state.ScanAsync();
            SuggestProfile();
            var plan = await Task.Run(() => _engine.BuildPlan(SelectedProfile.Profile, _state.Snapshot!, _state.Tier!));
            Plan.Clear();
            foreach (var i in plan.Items) Plan.Add(new PlanItemViewModel { Item = i, Selected = i.Risk <= RiskLevel.Medium || SelectedProfile.Profile.Id == "extreme" });
            Excluded.Clear();
            foreach (var e in plan.Excluded) Excluded.Add(e);
            Notes.Clear();
            foreach (var n in plan.Notes) Notes.Add(n);
            HasPlan = true;
            ResultSummary = "";
            ShowBenchmarkOffer = false;
            PlanSummary = Plan.Count == 0
                ? "Everything in this profile is already applied or not applicable to this PC."
                : $"{Plan.Count} recommended change(s) for this {_state.Tier!.TierDisplay}. {Excluded.Count} item(s) excluded (see below for the reason of each).";
            StatusMessage = "";
        });
    }

    [RelayCommand]
    private async Task Apply()
    {
        var selected = Plan.Where(p => p.Selected).ToList();
        var changes = selected.SelectMany(p => p.Item.Changes).ToList();
        var label = SelectedProfile?.Profile.Id switch
        {
            "extreme" => "Extreme Optimization",
            "lowend" => "Low-End FPS Boost",
            "highend" => "High-End Competitive",
            "balanced" => "Gaming Optimization",
            _ => "Safe Optimization"
        };
        var op = await _runner.ApplyAsync(label, changes);
        if (op is null) return;
        ResultSummary = $"{op.Status}: {op.AppliedCount} applied, {op.FailedCount} failed. Restore point: {op.RestorePoint?.Status}. Backup: {op.FolderPath}";
        ShowBenchmarkOffer = op.Status == OperationStatus.Completed;
        await Generate();
        ShowBenchmarkOffer = op.Status == OperationStatus.Completed;
    }

    [RelayCommand] private void SelectAll() { foreach (var p in Plan) p.Selected = true; }
    [RelayCommand] private void SelectNone() { foreach (var p in Plan) p.Selected = false; }
    [RelayCommand] private void OpenBenchmark() => ((MainViewModel)_services.GetService(typeof(MainViewModel))!).NavigateTo<BenchmarkViewModel>();
}

public sealed partial class TierAnalysisViewModel : PageViewModel
{
    private readonly SystemState _state;
    private readonly SettingsService _settings;
    private readonly IServiceProvider _services;

    public override string Title => "Hardware Tier Analysis";
    public override string Subtitle => "Detected PC tier → detected bottlenecks → recommended optimizations → expected tradeoffs";

    public ObservableCollection<ComponentScore> Scores { get; } = new();
    public ObservableCollection<string> Reasons { get; } = new();
    public ObservableCollection<Bottleneck> Bottlenecks { get; } = new();
    public ObservableCollection<string> Priorities { get; } = new();
    public ObservableCollection<string> Tradeoffs { get; } = new();
    public ObservableCollection<string> LaptopPluggedIn { get; } = new();
    public ObservableCollection<string> LaptopBattery { get; } = new();

    public List<string> OverrideOptions { get; } = new() { "Automatic", "LowEnd", "MidRange", "HighEnd" };
    public List<int> Resolutions { get; } = new() { 720, 1080, 1440, 2160 };
    public List<int> FpsTargets { get; } = new() { 60, 75, 100, 120, 144, 165, 200, 240, 360 };

    [ObservableProperty] private string _tierText = "–";
    [ObservableProperty] private double _score;
    [ObservableProperty] private string _limiter = "";
    [ObservableProperty] private string _overrideChoice = "Automatic";
    [ObservableProperty] private int _resolution = 1080;
    [ObservableProperty] private int _targetFps = 144;
    [ObservableProperty] private bool _isLaptop;
    [ObservableProperty] private string _powerText = "";

    public TierAnalysisViewModel(SystemState state, SettingsService settings, IServiceProvider services)
    {
        _state = state;
        _settings = settings;
        _services = services;
        _overrideChoice = settings.Current.TierOverride?.ToString() ?? "Automatic";
        _state.SnapshotChanged += Recompute;
    }

    protected override async Task LoadAsync()
    {
        if (_state.Snapshot is null) await _state.ScanAsync();
        if (_state.Snapshot?.PrimaryDisplay is { } d)
        {
            Resolution = Resolutions.OrderBy(r => Math.Abs(r - d.Height)).First();
            TargetFps = FpsTargets.OrderBy(f => Math.Abs(f - d.MaxRefreshAtCurrentResolution)).First();
        }
        Recompute();
    }

    partial void OnOverrideChoiceChanged(string value)
    {
        _settings.Current.TierOverride = Enum.TryParse<PcTier>(value, out var t) ? t : null;
        _settings.Save();
        Recompute();
        _state.NotifySystemChanged();
    }

    partial void OnResolutionChanged(int value) => Recompute();
    partial void OnTargetFpsChanged(int value) => Recompute();

    private void Recompute()
    {
        if (_state.Snapshot is null) return;
        _state.Workload = new TargetWorkload { ResolutionHeight = Resolution, TargetFps = TargetFps };
        _state.Reclassify();
        var t = _state.Tier!;
        TierText = t.TierDisplay + (t.IsOverridden ? $"  (override — computed {t.ComputedTier})" : "");
        Score = t.OverallScore;
        Limiter = t.LikelyLimiter;
        IsLaptop = t.IsLaptop;
        Fill(Scores, t.Scores);
        Fill(Reasons, t.Reasons.Concat(t.Caps));
        Fill(Bottlenecks, t.Bottlenecks);
        Fill(Priorities, t.Priorities);
        Fill(Tradeoffs, t.Tradeoffs);
        var p = _state.Snapshot.Power;
        PowerText = p.HasBattery ? $"{(p.OnAcPower ? "Charger connected" : "On battery")} · {p.BatteryPercent}% · plan: {p.ActivePlanName}" : $"Desktop · plan: {p.ActivePlanName}";
        Fill(LaptopPluggedIn, new[]
        {
            "Select the manufacturer's Performance/Turbo mode (Armoury Crate, Lenovo Vantage, OMEN Gaming Hub, Alienware Command Center…).",
            "Set the GPU preference of games to High performance (Game Profiles) so the dedicated GPU renders them" + (_state.Snapshot.IsHybridGraphics ? " — hybrid graphics detected." : "."),
            "If available, enable the MUX switch / 'dGPU only' mode in the OEM tool for lower latency (requires restart).",
            "Use High performance power plan or the 'Best performance' power mode while plugged in.",
            "Keep vents clear and use a stand; sustained thermal limits show up as 'CPU performance limited' on the Dashboard."
        });
        Fill(LaptopBattery, new[]
        {
            "Expect substantially lower CPU/GPU power limits on battery — this is by design.",
            "Cap FPS (e.g. 60) and enable Battery Boost / Radeon Chill to extend battery life.",
            "Switch back to the Balanced plan or the 'Best power efficiency' mode.",
            "Background recording and overlays cost more battery than performance."
        });
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    [RelayCommand] private void Optimize() => ((MainViewModel)_services.GetService(typeof(MainViewModel))!).NavigateTo<SmartOptimizeViewModel>();
}
