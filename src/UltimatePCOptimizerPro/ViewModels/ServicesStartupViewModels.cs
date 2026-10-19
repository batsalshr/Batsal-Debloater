using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimatePCOptimizerPro.Converters;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

public sealed partial class ServicesViewModel : PageViewModel
{
    private readonly WindowsServiceManager _manager;
    private readonly OperationRunner _runner;
    private readonly DialogService _dialogs;
    private readonly BackupService _backups;

    public override string Title => "Services Optimizer";
    public override string Subtitle => "Every Windows service, categorised. Unknown is never treated as safe; essential services are locked.";

    public ObservableCollection<ServiceEntry> Services { get; } = new();
    public ICollectionView View { get; }
    public ObservableCollection<string> DependsOn { get; } = new();
    public ObservableCollection<string> Dependents { get; } = new();
    public List<string> CategoryFilters { get; } = new() { "All", "Essential", "Recommended", "Optional", "HardwareDependent", "ApplicationDependent", "Unknown", "Modifiable" };
    public List<ServiceStartMode> StartModes { get; } = Enum.GetValues<ServiceStartMode>().ToList();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _categoryFilter = "Modifiable";
    [ObservableProperty] private ServiceEntry? _selected;
    [ObservableProperty] private ServiceStartMode _newMode = ServiceStartMode.Manual;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasBackup;

    public ServicesViewModel(WindowsServiceManager manager, OperationRunner runner, DialogService dialogs, BackupService backups)
    {
        _manager = manager;
        _runner = runner;
        _dialogs = dialogs;
        _backups = backups;
        View = CollectionViewSource.GetDefaultView(Services);
        View.Filter = o => o is ServiceEntry s
                           && (Search.Length == 0 || s.DisplayName.Contains(Search, StringComparison.OrdinalIgnoreCase) || s.Name.Contains(Search, StringComparison.OrdinalIgnoreCase))
                           && (CategoryFilter == "All" || (CategoryFilter == "Modifiable" ? s.CanModify : s.Category.ToString() == CategoryFilter));
    }

    partial void OnSearchChanged(string value) => View.Refresh();
    partial void OnCategoryFilterChanged(string value) => View.Refresh();

    partial void OnSelectedChanged(ServiceEntry? value)
    {
        DependsOn.Clear();
        Dependents.Clear();
        if (value is null) return;
        NewMode = value.Catalog?.SuggestedStartMode ?? value.StartMode ?? ServiceStartMode.Manual;
        HasBackup = _backups.FindActiveChangesForTweak("service:" + value.Name) is not null;
        var (on, dep) = _manager.GetDependencies(value.Name);
        foreach (var d in on) DependsOn.Add(d);
        foreach (var d in dep) Dependents.Add(d);
        if (DependsOn.Count == 0) DependsOn.Add("(none)");
        if (Dependents.Count == 0) Dependents.Add("(none)");
    }

    protected override Task LoadAsync() => Reload();

    [RelayCommand]
    private async Task Reload()
    {
        await RunBusy("Reading services…", async () =>
        {
            var name = Selected?.Name;
            var list = await _manager.GetServicesAsync();
            Services.Clear();
            foreach (var s in list) Services.Add(s);
            Summary = $"{list.Count} services · {list.Count(s => s.State == "Running")} running · {list.Count(s => s.CanModify)} modifiable · {list.Count(s => s.IsProtected)} locked";
            Selected = name is null ? null : Services.FirstOrDefault(s => s.Name == name);
            StatusMessage = "";
        });
    }

    [RelayCommand]
    private async Task ApplyMode()
    {
        if (Selected is not { } s) return;
        if (!s.CanModify) { _dialogs.Info("Locked", "This service is essential, unknown or managed by Windows and is not modified by this app."); return; }
        if (s.StartMode == NewMode) { _dialogs.Info("No change", $"{s.DisplayName} is already set to {NewMode}."); return; }
        var running = Dependents.Where(d => d.EndsWith("Running")).ToList();
        string notes = (s.Catalog?.SideEffects ?? "No curated information: this service belongs to installed software — disabling it can break that software.") +
                       (running.Count > 0 && NewMode == ServiceStartMode.Disabled ? $"\nRunning services that depend on it: {string.Join(", ", running)}" : "") +
                       "\nThe service keeps running until it stops or the PC restarts.";
        await _runner.ApplyAsync("Services Optimization", new[]
        {
            new PlannedChange
            {
                Title = $"{s.DisplayName}: {s.StartModeDisplay} → {NewMode}", TweakId = "service:" + s.Name, Category = "Services",
                Risk = s.Catalog?.Risk ?? RiskLevel.High, Restart = RestartRequirement.Restart, Notes = notes,
                Spec = new ServiceStartSpec { ServiceName = s.Name, StartMode = NewMode }
            }
        });
        await Reload();
    }

    [RelayCommand]
    private async Task RestoreOriginal()
    {
        if (Selected is not { } s) return;
        await _runner.RestoreTweakAsync("service:" + s.Name);
        await Reload();
    }

    [RelayCommand] private void OpenServicesConsole() => Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true });
}

public sealed partial class StartupItemViewModel : ObservableObject
{
    public StartupEntry Entry { get; init; } = null!;
    public string Name => Entry.Name;
    public string Publisher => Entry.Publisher ?? "Unknown publisher";
    public string Description => Entry.Description ?? "";
    public string Command => Entry.Command;
    public string Location => Entry.LocationDisplay;
    public bool Enabled => Entry.Enabled;
    public bool Missing => !Entry.ExecutableExists;
    [ObservableProperty] private string _usage = "Not running";
}

public sealed partial class StartupViewModel : PageViewModel
{
    private readonly StartupManagerService _startup;
    private readonly WindowsDebloatService _debloat;
    private readonly ProcessMonitorService _processes;
    private readonly OperationRunner _runner;

    public override string Title => "Startup Optimizer";
    public override string Subtitle => "Disable apps that start with Windows. Uses the same reversible mechanism as Task Manager — nothing is deleted.";

    public ObservableCollection<StartupItemViewModel> Items { get; } = new();
    public ObservableCollection<LogonTaskEntry> LogonTasks { get; } = new();
    [ObservableProperty] private string _summary = "";

    public StartupViewModel(StartupManagerService startup, WindowsDebloatService debloat, ProcessMonitorService processes, OperationRunner runner)
    {
        _startup = startup;
        _debloat = debloat;
        _processes = processes;
        _runner = runner;
    }

    protected override Task LoadAsync() => Reload();

    [RelayCommand]
    private async Task Reload()
    {
        await RunBusy("Reading startup entries…", async () =>
        {
            var entries = await Task.Run(_startup.GetEntries);
            var procs = await _processes.SampleAsync(700);
            Items.Clear();
            foreach (var e in entries)
            {
                var vm = new StartupItemViewModel { Entry = e };
                var running = procs.Where(p => p.Path is not null && e.ExecutablePath is not null && p.Path.Equals(e.ExecutablePath, StringComparison.OrdinalIgnoreCase)).ToList();
                if (running.Count > 0)
                    vm.Usage = $"Running now: {running.Sum(p => p.CpuPercent):0.0}% CPU · {BytesConverter.Format(running.Sum(p => (double)p.PrivateBytes))} RAM";
                Items.Add(vm);
            }
            Summary = $"{entries.Count} entries · {entries.Count(e => e.Enabled)} enabled · {entries.Count(e => !e.ExecutableExists)} point to missing files. " +
                      "Windows does not expose Task Manager's 'startup impact' score; current resource use is shown for running apps.";
            var logon = await Task.Run(_debloat.GetLogonTasks);
            LogonTasks.Clear();
            foreach (var l in logon) LogonTasks.Add(l);
            StatusMessage = "";
        });
    }

    [RelayCommand]
    private async Task Toggle(StartupItemViewModel? item)
    {
        if (item is null) return;
        var e = item.Entry;
        await _runner.ApplyAsync("Startup Optimization", new[]
        {
            new PlannedChange
            {
                Title = $"{(e.Enabled ? "Disable" : "Enable")} startup: {e.Name}", TweakId = $"startup:{e.Location}:{e.Name}", Category = "Startup", Risk = RiskLevel.Low,
                Restart = RestartRequirement.SignOut, Notes = "Takes effect at next sign-in. The app itself is not uninstalled and can still be started manually.",
                Spec = new StartupEntrySpec { Location = e.Location, EntryName = e.Name, Enabled = !e.Enabled }
            }
        });
        await Reload();
    }

    [RelayCommand]
    private async Task ToggleTask(LogonTaskEntry? t)
    {
        if (t is null) return;
        await _runner.ApplyAsync("Startup Tasks", new[]
        {
            new PlannedChange { Title = $"{(t.Enabled ? "Disable" : "Enable")} logon task {t.TaskPath}", TweakId = "task:" + t.TaskPath, Category = "Startup", Risk = RiskLevel.Low,
                Notes = $"Action: {t.Action}", Spec = new ScheduledTaskSpec { TaskPath = t.TaskPath, Enabled = !t.Enabled } }
        });
        await Reload();
    }

    [RelayCommand] private void OpenTaskManager() => Process.Start(new ProcessStartInfo("taskmgr.exe", "/0 /startup") { UseShellExecute = true });
}
