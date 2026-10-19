using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

/// <summary>One tweak information card. All values shown come from the live system via TweakStatusService.</summary>
public sealed partial class TweakCardViewModel : ObservableObject
{
    private readonly TweakListViewModel _owner;
    public TweakDefinition Definition { get; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsApplied), nameof(StateText), nameof(CurrentText), nameof(CanApply), nameof(CanRestore), nameof(StateValue))]
    private TweakStatus _status = new() { State = TweakState.Unknown, Message = "Reading current value…" };
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _isRecommended;

    public TweakCardViewModel(TweakDefinition def, TweakListViewModel owner)
    {
        Definition = def;
        _owner = owner;
    }

    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string CategoryText => Definition.Category.ToString();
    public RiskLevel Risk => Definition.Risk;
    public string RestartText => DialogService.RestartText(Definition.Restart);
    public string ImpactText => string.Join(" · ", Definition.Impacts);
    public string AdminText => Definition.RequiresAdmin ? "Administrator" : "Current user";
    public string TypeText => Definition.IsRegistryBacked ? "Registry" : string.Join(", ", Definition.Actions.Select(a => a.GetType().Name.Replace("Spec", "")).Distinct());
    public string TiersText => Definition.Tiers.Count == 0 ? "All tiers" : string.Join(", ", Definition.Tiers);
    public string LocationsText => string.Join("\n", Status.Locations.Count > 0 ? Status.Locations : Definition.Actions.Select(a => a.Target));
    public string TargetText => string.Join("\n", Definition.Actions.Select(a => a.Desired));

    public string CompatibilityText
    {
        get
        {
            var c = Definition.Conditions;
            var parts = new List<string>();
            if (c.MinBuild > 0) parts.Add($"Windows build ≥ {c.MinBuild}");
            if (c.MaxBuild > 0) parts.Add($"Windows build ≤ {c.MaxBuild}");
            if (c.DesktopOnly) parts.Add("Desktops only");
            if (c.LaptopOnly) parts.Add("Laptops only");
            if (c.RequiresDedicatedGpu) parts.Add("Dedicated GPU");
            if (c.RequiresSsdSystemDrive) parts.Add("Windows on SSD");
            if (c.RequiresHybridCpu) parts.Add("Hybrid CPU");
            if (c.RequiresActiveNetworkAdapter) parts.Add("Active network adapter");
            if (c.RequiresSoftware is { } s) parts.Add($"{s} installed");
            return parts.Count == 0 ? "Windows 10 and 11" : string.Join(" · ", parts);
        }
    }

    public bool IsApplied => Status.State == TweakState.Applied;
    public TweakState StateValue => Status.State;
    public string StateText => Status.State switch
    {
        TweakState.Applied => "Applied",
        TweakState.NotApplied => "Not applied",
        TweakState.Partial => "Partially applied",
        TweakState.Unsupported => "Unsupported",
        TweakState.Error => "Error",
        _ => "Checking…"
    };
    public string CurrentText => Status.CurrentValues.Count == 0 ? Status.Message : string.Join("\n", Status.CurrentValues);
    public bool CanApply => Status.State is TweakState.NotApplied or TweakState.Partial;
    public bool CanRestore => Status.HasAppBackup;

    [RelayCommand] private Task Toggle() => IsApplied ? _owner.RestoreAsync(this) : _owner.ApplyAsync(new[] { this });
    [RelayCommand] private Task Apply() => _owner.ApplyAsync(new[] { this });
    [RelayCommand] private Task Restore() => _owner.RestoreAsync(this);
    [RelayCommand] private void Expand() => IsExpanded = !IsExpanded;
    [RelayCommand] private void Favorite() => _owner.ToggleFavorite(this);

    partial void OnIsSelectedChanged(bool value) => _owner.SelectionChanged();
}

public sealed partial class TweakListViewModel : ObservableObject
{
    private readonly TweakCatalogService _catalog;
    private readonly TweakStatusService _status;
    private readonly OperationRunner _runner;
    private readonly SystemState _state;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private Func<TweakDefinition, bool> _scope = _ => true;
    private string _label = "Tweaks";
    private bool _refreshing;

    public ObservableCollection<TweakCardViewModel> Cards { get; } = new();
    public ICollectionView View { get; }

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _categoryFilter = "All";
    [ObservableProperty] private string _riskFilter = "All";
    [ObservableProperty] private string _stateFilter = "All";
    [ObservableProperty] private string _tierFilter = "All";
    [ObservableProperty] private bool _showFilters = true;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private int _visibleCount;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isRefreshing;

    public List<string> Categories { get; private set; } = new() { "All" };
    public List<string> Risks { get; } = new() { "All", "Low", "Medium", "High", "Expert" };
    public List<string> States { get; } = new() { "All", "Recommended", "Applied", "Not applied", "Unsupported", "Has app backup", "Favorites" };
    public List<string> TierOptions { get; } = new() { "All", "LowEnd", "MidRange", "HighEnd" };
    public ObservableCollection<string> CustomProfiles { get; } = new();

    public TweakListViewModel(TweakCatalogService catalog, TweakStatusService status, OperationRunner runner, SystemState state, SettingsService settings, DialogService dialogs)
    {
        _catalog = catalog;
        _status = status;
        _runner = runner;
        _state = state;
        _settings = settings;
        _dialogs = dialogs;
        View = CollectionViewSource.GetDefaultView(Cards);
        View.Filter = Filter;
        _state.SystemChanged += () => _ = RefreshAsync();
        _state.SnapshotChanged += () => _ = RefreshAsync();
    }

    public TweakListViewModel Configure(string label, Func<TweakDefinition, bool> scope, bool showFilters = true)
    {
        _label = label;
        _scope = scope;
        ShowFilters = showFilters;
        Cards.Clear();
        var favorites = _settings.Current.FavoriteTweaks.ToHashSet();
        foreach (var t in _catalog.Tweaks.Where(scope))
            Cards.Add(new TweakCardViewModel(t, this) { IsFavorite = favorites.Contains(t.Id) });
        Categories = new[] { "All" }.Concat(Cards.Select(c => c.CategoryText).Distinct().OrderBy(c => c)).ToList();
        OnPropertyChanged(nameof(Categories));
        foreach (var p in _catalog.LoadCustomProfiles()) CustomProfiles.Add(p.Name);
        UpdateCounts();
        return this;
    }

    partial void OnSearchChanged(string value) => RefreshView();
    partial void OnCategoryFilterChanged(string value) => RefreshView();
    partial void OnRiskFilterChanged(string value) => RefreshView();
    partial void OnStateFilterChanged(string value) => RefreshView();
    partial void OnTierFilterChanged(string value) => RefreshView();

    private void RefreshView()
    {
        View.Refresh();
        UpdateCounts();
    }

    private bool Filter(object o)
    {
        if (o is not TweakCardViewModel c) return false;
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var s = Search.Trim();
            if (!(c.Name.Contains(s, StringComparison.OrdinalIgnoreCase) || c.Description.Contains(s, StringComparison.OrdinalIgnoreCase)
                  || c.Definition.Id.Contains(s, StringComparison.OrdinalIgnoreCase) || c.LocationsText.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        if (CategoryFilter != "All" && c.CategoryText != CategoryFilter) return false;
        if (RiskFilter != "All" && c.Risk.ToString() != RiskFilter) return false;
        if (TierFilter != "All" && c.Definition.Tiers.Count > 0 && !c.Definition.Tiers.Select(t => t.ToString()).Contains(TierFilter)) return false;
        return StateFilter switch
        {
            "Recommended" => c.IsRecommended && !c.IsApplied,
            "Applied" => c.IsApplied,
            "Not applied" => c.Status.State is TweakState.NotApplied or TweakState.Partial,
            "Unsupported" => c.Status.State == TweakState.Unsupported,
            "Has app backup" => c.Status.HasAppBackup,
            "Favorites" => c.IsFavorite,
            _ => true
        };
    }

    private void UpdateCounts()
    {
        VisibleCount = Cards.Count(Filter);
        SelectedCount = Cards.Count(c => c.IsSelected);
        int applied = Cards.Count(c => c.IsApplied), unsupported = Cards.Count(c => c.Status.State == TweakState.Unsupported);
        Summary = $"{Cards.Count} tweaks · {applied} applied · {Cards.Count - applied - unsupported} available · {unsupported} unsupported on this PC";
    }

    public void SelectionChanged() => SelectedCount = Cards.Count(c => c.IsSelected);

    /// <summary>Re-reads the real current values of every card (background thread).</summary>
    public async Task RefreshAsync()
    {
        if (_refreshing || Cards.Count == 0) return;
        _refreshing = true;
        IsRefreshing = true;
        try
        {
            var snapshot = _state.Snapshot;
            var defs = Cards.Select(c => c.Definition).ToList();
            var results = await Task.Run(() => _status.EvaluateAll(defs, snapshot));
            var recommended = RecommendedIds();
            foreach (var c in Cards)
            {
                if (results.TryGetValue(c.Definition.Id, out var st)) c.Status = st;
                c.IsRecommended = recommended.Contains(c.Definition.Id);
            }
            RefreshView();
        }
        finally
        {
            _refreshing = false;
            IsRefreshing = false;
        }
    }

    /// <summary>Tweaks of the profile that matches the detected tier (Low → lowend, Mid → balanced, High → highend).</summary>
    private HashSet<string> RecommendedIds()
    {
        var tier = _state.Tier?.Tier ?? PcTier.MidRange;
        var profileId = tier switch { PcTier.LowEnd => "lowend", PcTier.HighEnd => "highend", _ => "balanced" };
        var profile = _catalog.GetProfile(profileId);
        if (profile is null) return new();
        return profile.Tweaks.Where(id => _catalog.Get(id) is { } t && (t.Tiers.Count == 0 || t.Tiers.Contains(tier))).ToHashSet();
    }

    public async Task ApplyAsync(IEnumerable<TweakCardViewModel> cards)
    {
        var list = cards.Where(c => c.Status.State != TweakState.Unsupported).ToList();
        if (list.Count == 0)
        {
            _dialogs.Info("Not available", "The selected tweaks are not supported on this PC. Open the card details to see why.");
            return;
        }
        var changes = new List<PlannedChange>();
        foreach (var c in list)
        {
            var planned = TweakResolver.ToPlannedChanges(c.Definition, _state.Snapshot, out var err);
            if (planned is null) { _dialogs.Error(c.Name, err ?? "Could not resolve this tweak."); return; }
            changes.AddRange(planned);
        }
        await _runner.ApplyAsync(_label, changes);
    }

    public Task RestoreAsync(TweakCardViewModel card) => _runner.RestoreTweakAsync(card.Definition.Id);

    public void ToggleFavorite(TweakCardViewModel card)
    {
        card.IsFavorite = !card.IsFavorite;
        var favs = _settings.Current.FavoriteTweaks;
        favs.Remove(card.Definition.Id);
        if (card.IsFavorite) favs.Add(card.Definition.Id);
        _settings.Save();
        if (StateFilter == "Favorites") RefreshView();
    }

    [RelayCommand] private Task Refresh() => RefreshAsync();
    [RelayCommand] private Task ApplySelected() => ApplyAsync(Cards.Where(c => c.IsSelected));

    [RelayCommand]
    private async Task RestoreSelected()
    {
        var selected = Cards.Where(c => c.IsSelected && c.Status.HasAppBackup).ToList();
        if (selected.Count == 0)
        {
            _dialogs.Info("Nothing to restore", "None of the selected tweaks has an active backup created by this app.");
            return;
        }
        foreach (var c in selected) await _runner.RestoreTweakAsync(c.Definition.Id);
    }

    [RelayCommand]
    private void SelectVisible()
    {
        foreach (var c in Cards.Where(Filter)) c.IsSelected = c.Status.State is TweakState.NotApplied or TweakState.Partial || c.IsSelected;
    }

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var c in Cards) c.IsSelected = c.IsRecommended && c.CanApply;
    }

    [RelayCommand] private void ClearSelection() { foreach (var c in Cards) c.IsSelected = false; }

    [RelayCommand]
    private void SaveProfile()
    {
        var selected = Cards.Where(c => c.IsSelected).Select(c => c.Definition.Id).ToList();
        if (selected.Count == 0) { _dialogs.Info("Select tweaks first", "Select the tweaks to include in the profile."); return; }
        var name = $"Custom {DateTime.Now:yyyy-MM-dd HH-mm}";
        _catalog.SaveCustomProfile(name, selected);
        if (!CustomProfiles.Contains(name)) CustomProfiles.Add(name);
        _dialogs.Success("Profile saved", $"Saved {selected.Count} tweaks as “{name}” in {Core.AppPaths.UserProfiles}.");
    }

    [RelayCommand]
    private void LoadProfile(string? name)
    {
        var p = _catalog.LoadCustomProfiles().FirstOrDefault(x => x.Name == name);
        if (p is null) return;
        foreach (var c in Cards) c.IsSelected = p.Tweaks.Contains(c.Definition.Id);
    }
}
