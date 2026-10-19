using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimatePCOptimizerPro.Converters;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

public sealed partial class AppItem : ObservableObject
{
    public InstalledApp App { get; init; } = null!;
    [ObservableProperty] private bool _selected;
    public string Name => App.DisplayName;
    public string Package => App.Name;
    public string Category => App.Category;
    public string Notes => App.IsProtected ? App.ProtectionReason : App.Catalog?.Notes ?? "Not in the curated list — may have been installed by you. Not recommended automatically.";
    public RiskLevel Risk => App.Catalog?.Risk ?? RiskLevel.Medium;
    public bool CanRemove => !App.IsProtected;
    public string Version => App.Version;
}

public sealed partial class TaskItem : ObservableObject
{
    public ScheduledTaskState State { get; init; } = null!;
    public string Name => State.Entry.DisplayName;
    public string Path => State.Entry.TaskPath;
    public string Purpose => State.Entry.Purpose;
    public string Effect => State.Entry.Effect;
    public RiskLevel Risk => State.Entry.Risk;
    public string StateText => !State.Exists ? "Not present" : State.Enabled == true ? "Enabled" : "Disabled";
    public bool IsEnabled => State.Enabled == true;
    public bool Exists => State.Exists;
}

public sealed partial class FeatureItem : ObservableObject
{
    public OptionalFeatureState State { get; init; } = null!;
    public string Name => State.Catalog?.DisplayName ?? State.Caption;
    public string FeatureName => State.FeatureName;
    public string Purpose => State.Catalog?.Purpose ?? "";
    public string Recommendation => State.Catalog?.Recommendation ?? "No curated recommendation — change only if you know you need it.";
    public string StateText => State.StateDisplay;
    public bool IsEnabled => State.InstallState == 1;
    public bool IsProtected => SafetyPolicy.ProtectedFeatures.Contains(State.FeatureName);
}

public sealed partial class CleanupItem : ObservableObject
{
    public CleanupCategory Category { get; init; } = null!;
    [ObservableProperty] private bool _selected;
    public string Name => Category.Name;
    public string Description => Category.Description;
    public string Warning => Category.Warning;
    public string SizeText => $"{Category.Files.Count:N0} files · {BytesConverter.Format(Category.TotalBytes)}" +
                              (Category.SkippedRecent > 0 ? $" · {Category.SkippedRecent:N0} recent skipped" : "") + (Category.InaccessibleCount > 0 ? $" · {Category.InaccessibleCount} inaccessible" : "");
    public bool RequiresAdmin => Category.RequiresAdmin;
}

public sealed partial class DebloatViewModel : PageViewModel
{
    private readonly WindowsDebloatService _debloat;
    private readonly ProcessMonitorService _processes;
    private readonly CleanupService _cleanup;
    private readonly OperationRunner _runner;
    private readonly DialogService _dialogs;
    private readonly SystemState _state;
    private readonly StartupManagerService _startup;

    public override string Title => "Windows Debloater";
    public override string Subtitle => "Detects what is actually installed on this PC. Essential Windows components, frameworks, codecs and drivers are protected.";

    public ObservableCollection<AppItem> Apps { get; } = new();
    public ICollectionView AppsView { get; }
    public ObservableCollection<TaskItem> Tasks { get; } = new();
    public ObservableCollection<LogonTaskEntry> LogonTasks { get; } = new();
    public ObservableCollection<FeatureItem> Features { get; } = new();
    public ObservableCollection<ProcessUsage> Processes { get; } = new();
    public ObservableCollection<CleanupItem> Cleanup { get; } = new();
    public ObservableCollection<CleanupFile> PreviewFiles { get; } = new();

    [ObservableProperty] private string _appSearch = "";
    [ObservableProperty] private bool _showProtected;
    [ObservableProperty] private string _appSummary = "";
    [ObservableProperty] private ProcessUsage? _selectedProcess;
    [ObservableProperty] private CleanupItem? _selectedCleanup;
    [ObservableProperty] private string _recycleText = "";
    [ObservableProperty] private string _cleanupSummary = "";
    [ObservableProperty] private bool _showAllFeatures;
    [ObservableProperty] private string _presetDescription = "Choose a preset to pre-select items for review, or select items manually (Custom). Nothing is removed until you confirm.";

    public DebloatViewModel(WindowsDebloatService debloat, ProcessMonitorService processes, CleanupService cleanup, OperationRunner runner, DialogService dialogs,
        SystemState state, StartupManagerService startup)
    {
        _debloat = debloat;
        _processes = processes;
        _cleanup = cleanup;
        _runner = runner;
        _dialogs = dialogs;
        _state = state;
        _startup = startup;
        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = o => o is AppItem a && (ShowProtected || !a.App.IsProtected)
                                                && (AppSearch.Length == 0 || a.Name.Contains(AppSearch, StringComparison.OrdinalIgnoreCase) || a.Package.Contains(AppSearch, StringComparison.OrdinalIgnoreCase));
        AppsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AppItem.Category)));
    }

    partial void OnAppSearchChanged(string value) => AppsView.Refresh();
    partial void OnShowProtectedChanged(bool value) => AppsView.Refresh();
    partial void OnShowAllFeaturesChanged(bool value) => _ = LoadFeatures();
    partial void OnSelectedCleanupChanged(CleanupItem? value)
    {
        PreviewFiles.Clear();
        if (value is null) return;
        foreach (var f in value.Category.Files.OrderByDescending(f => f.SizeBytes).Take(300)) PreviewFiles.Add(f);
    }

    protected override async Task LoadAsync()
    {
        await LoadApps();
        _ = LoadTasks();
        _ = LoadFeatures();
        _ = RefreshProcesses();
        UpdateRecycle();
    }

    [RelayCommand]
    private async Task LoadApps()
    {
        await RunBusy("Reading installed app packages…", async () =>
        {
            var apps = await _debloat.GetInstalledAppsAsync();
            Apps.Clear();
            foreach (var a in apps) Apps.Add(new AppItem { App = a });
            AppSummary = $"{apps.Count} packages installed for this user · {apps.Count(a => a.Catalog is not null && !a.IsProtected)} known optional · {apps.Count(a => a.IsProtected)} protected";
            StatusMessage = "";
        });
    }

    [RelayCommand]
    private void Preset(string? preset)
    {
        if (!Enum.TryParse<DebloatPreset>(preset, out var p))
        {
            foreach (var a in Apps) a.Selected = false;
            PresetDescription = "Custom: select individual apps manually.";
            return;
        }
        foreach (var a in Apps) a.Selected = WindowsDebloatService.InPreset(a.App, p);
        PresetDescription = p switch
        {
            DebloatPreset.Light => "Light: removes low-risk promotional and unused consumer apps only. Also review Startup Optimizer and the Safe profile.",
            DebloatPreset.Gaming => "Gaming: Light plus apps that run background tasks or overlays you don't need for gaming. Game Bar and Xbox app are kept (Game Pass/anti-cheat dependencies). Pair with the Balanced Gaming profile.",
            _ => "Advanced: more extensive optional apps (Mail, Phone Link, Media Player…). Read each note — some of these you may use. Also review the Services Optimizer and scheduled tasks."
        };
        StatusMessage = $"{Apps.Count(a => a.Selected)} app(s) pre-selected for review.";
    }

    [RelayCommand]
    private async Task RemoveSelected()
    {
        var selected = Apps.Where(a => a.Selected && a.CanRemove).ToList();
        if (selected.Count == 0) { _dialogs.Info("Nothing selected", "Select the apps to remove first."); return; }
        var changes = selected.Select(a => new PlannedChange
        {
            Title = $"Remove {a.Name}", TweakId = "appx:" + a.App.FamilyName, Category = "Debloat", Risk = a.Risk, Restart = RestartRequirement.None,
            Notes = a.App.Catalog?.Notes,
            Spec = new AppxRemoveSpec { PackageFullName = a.App.FullName, PackageFamilyName = a.App.FamilyName, DisplayName = a.Name }
        }).ToList();
        await _runner.ApplyAsync("Windows Debloat", changes);
        await LoadApps();
    }

    // ---------------------------------------------------------------- tasks

    [RelayCommand]
    private async Task LoadTasks()
    {
        var states = await Task.Run(_debloat.GetTaskStates);
        Tasks.Clear();
        foreach (var s in states) Tasks.Add(new TaskItem { State = s });
        var logon = await Task.Run(_debloat.GetLogonTasks);
        LogonTasks.Clear();
        foreach (var l in logon) LogonTasks.Add(l);
    }

    [RelayCommand]
    private async Task ToggleTask(TaskItem? t)
    {
        if (t is null || !t.Exists) return;
        await _runner.ApplyAsync("Scheduled Tasks", new[]
        {
            new PlannedChange { Title = $"{(t.IsEnabled ? "Disable" : "Enable")} task: {t.Name}", TweakId = "task:" + t.Path, Category = "Tasks", Risk = t.Risk,
                Notes = t.Effect, Spec = new ScheduledTaskSpec { TaskPath = t.Path, Enabled = !t.IsEnabled } }
        });
        await LoadTasks();
    }

    [RelayCommand]
    private async Task DisableLogonTask(LogonTaskEntry? t)
    {
        if (t is null) return;
        await _runner.ApplyAsync("Startup Tasks", new[]
        {
            new PlannedChange { Title = $"{(t.Enabled ? "Disable" : "Enable")} logon task {t.TaskPath}", TweakId = "task:" + t.TaskPath, Category = "Startup", Risk = RiskLevel.Low,
                Notes = $"Action: {t.Action}", Spec = new ScheduledTaskSpec { TaskPath = t.TaskPath, Enabled = !t.Enabled } }
        });
        await LoadTasks();
    }

    // ---------------------------------------------------------------- features

    private async Task LoadFeatures()
    {
        var list = await Task.Run(() => _debloat.GetOptionalFeatures(ShowAllFeatures));
        Features.Clear();
        foreach (var f in list) Features.Add(new FeatureItem { State = f });
    }

    [RelayCommand]
    private async Task ToggleFeature(FeatureItem? f)
    {
        if (f is null) return;
        if (f.IsEnabled && f.IsProtected) { _dialogs.Info("Protected feature", "This feature is used by virtualization, security, or common applications and is not disabled by this app."); return; }
        await _runner.ApplyAsync("Optional Features", new[]
        {
            new PlannedChange { Title = $"{(f.IsEnabled ? "Disable" : "Enable")} {f.Name}", TweakId = "feature:" + f.FeatureName, Category = "Features",
                Risk = f.State.Catalog?.Risk ?? RiskLevel.Medium, Restart = RestartRequirement.Restart, Notes = f.Recommendation,
                Spec = new OptionalFeatureSpec { FeatureName = f.FeatureName, Enabled = !f.IsEnabled } }
        });
        await LoadFeatures();
    }

    // ---------------------------------------------------------------- processes

    [RelayCommand]
    private async Task RefreshProcesses()
    {
        var list = await _processes.SampleAsync(1000);
        var startupExes = _startup.GetEntries().Where(e => e.ExecutablePath is not null)
            .GroupBy(e => e.ExecutablePath!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
        foreach (var p in list)
            if (p.Path is not null && startupExes.TryGetValue(p.Path, out var name)) p.StartupEntryName = name;
        Processes.Clear();
        foreach (var p in list.Take(80)) Processes.Add(p);
    }

    [RelayCommand]
    private async Task EndProcess(ProcessUsage? p)
    {
        if (p is null) return;
        if (!ProcessMonitorService.CanTerminate(p)) { _dialogs.Info("Protected process", "This process belongs to Windows or cannot be accessed and will not be ended."); return; }
        if (!_dialogs.Confirm($"End {p.Name}?", $"{p.Name} (PID {p.Pid}){(p.Publisher is null ? "" : " by " + p.Publisher)} will be closed. Unsaved work in it will be lost.\n\n" +
                                                "This is temporary — to stop it starting again, disable its startup entry on the Startup Optimizer page.", "End process", "Cancel", danger: true)) return;
        try { ProcessMonitorService.Terminate(p); }
        catch (Exception ex) { _dialogs.Error("Could not end process", ex.Message); }
        await RefreshProcesses();
    }

    // ---------------------------------------------------------------- cleanup

    [RelayCommand]
    private async Task ScanCleanup()
    {
        await RunBusy("Scanning temporary and cache folders…", async () =>
        {
            var cats = await _cleanup.ScanAsync();
            Cleanup.Clear();
            foreach (var c in cats) Cleanup.Add(new CleanupItem { Category = c, Selected = c.SelectedByDefault && c.Files.Count > 0 && (!c.RequiresAdmin || _state.IsElevated) });
            CleanupSummary = $"Found {BytesConverter.Format(cats.Sum(c => c.TotalBytes))} in {cats.Sum(c => c.Files.Count):N0} files. Shader caches are not selected by default.";
            SelectedCleanup = Cleanup.FirstOrDefault(c => c.Category.Files.Count > 0);
            StatusMessage = "";
        });
        UpdateRecycle();
    }

    [RelayCommand]
    private async Task DeleteCleanup()
    {
        var selected = Cleanup.Where(c => c.Selected && c.Category.Files.Count > 0).ToList();
        if (selected.Count == 0) { _dialogs.Info("Nothing selected", "Scan first, then select categories to clean."); return; }
        var rows = selected.Select(c => DialogService.Row(c.Name, c.SizeText, c.Warning, c.RequiresAdmin ? "Admin" : null, "Warning"));
        if (!_dialogs.Confirm("Delete these files?",
                $"{selected.Sum(c => c.Category.Files.Count):N0} files ({BytesConverter.Format(selected.Sum(c => c.Category.TotalBytes))}) will be permanently deleted. " +
                "Deleted files cannot be restored by this app or by System Restore. Files that are in use are skipped.", "Delete files", "Cancel", danger: true, rows: rows)) return;
        CleanupResult result = new();
        await RunBusy("Deleting files…", async () => result = await _cleanup.DeleteAsync(selected.Select(c => c.Category)));
        _dialogs.Show("Cleanup finished", $"Deleted {result.Deleted:N0} files and freed {BytesConverter.Format(result.BytesFreed)}. {result.Failed:N0} file(s) were in use or inaccessible and were left in place.",
            result.Failed == 0 ? Dialogs.DialogTone.Success : Dialogs.DialogTone.Info);
        await ScanCleanup();
    }

    private void UpdateRecycle()
    {
        var (bytes, items) = CleanupService.QueryRecycleBin();
        RecycleText = $"Recycle Bin: {items:N0} item(s), {BytesConverter.Format(bytes)}";
    }

    [RelayCommand]
    private void EmptyRecycle()
    {
        var (bytes, items) = CleanupService.QueryRecycleBin();
        if (items == 0) { _dialogs.Info("Recycle Bin is empty", "There is nothing to delete."); return; }
        if (!_dialogs.Confirm("Empty the Recycle Bin?", $"{items:N0} item(s), {BytesConverter.Format(bytes)} will be permanently deleted.", "Empty", "Cancel", danger: true)) return;
        CleanupService.EmptyRecycleBin();
        UpdateRecycle();
    }

    [RelayCommand] private void OpenDiskCleanup() => CleanupService.OpenDiskCleanup();
    [RelayCommand] private void OpenStorage() => CleanupService.OpenStorageSettings();
}
