using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimatePCOptimizerPro.Core;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

public sealed record AppRestorePointRow(string Name, string Date, string Time, string Operation, RestorePointStatus Status, bool Verified, string Backup, bool RollbackAvailable, string Message);

public sealed partial class RestorePointsViewModel : PageViewModel
{
    private readonly RestorePointService _rp;
    private readonly BackupService _backups;
    private readonly DialogService _dialogs;
    private readonly SystemState _state;
    private readonly BusyService _busy;
    private readonly LoggingService _log;

    public override string Title => "Restore Points";
    public override string Subtitle => "Windows System Restore points (including those created automatically before every optimization) and their verification status";

    public ObservableCollection<SystemRestorePointInfo> WindowsPoints { get; } = new();
    public ObservableCollection<AppRestorePointRow> AppPoints { get; } = new();
    public string Instructions => RestorePointService.EnableInstructions;

    [ObservableProperty] private string _protectionText = "";
    [ObservableProperty] private string _protectionTone = "Warning";
    [ObservableProperty] private string _frequencyText = "";
    [ObservableProperty] private string _manualName = "Before manual changes";
    [ObservableProperty] private string _listNote = "";

    public RestorePointsViewModel(RestorePointService rp, BackupService backups, DialogService dialogs, SystemState state, BusyService busy, LoggingService log)
    {
        _rp = rp;
        _backups = backups;
        _dialogs = dialogs;
        _state = state;
        _busy = busy;
        _log = log;
        _state.SystemChanged += () => _ = Reload();
    }

    protected override Task LoadAsync() => Reload();
    protected override Task OnRevisitedAsync() => Reload();

    [RelayCommand]
    private async Task Reload()
    {
        var status = await Task.Run(_rp.GetProtectionStatus);
        ProtectionText = status.Message.Length > 0 ? status.Message : "Status unknown";
        ProtectionTone = status.Enabled == true ? "Success" : status.Enabled == false ? "Danger" : "Warning";
        FrequencyText = (status.FrequencyMinutes <= 0
                            ? "Creation frequency limit: none (custom setting). "
                            : $"Creation frequency limit: {status.FrequencyMinutes} minutes{(status.FrequencyIsDefault ? " (Windows default)" : " (custom)")}. ") +
                        (status.NextAllowedCreation is { } next && next > DateTime.Now ? $"Next new restore point allowed after {next:yyyy-MM-dd HH:mm}." : "A new restore point can be created now.");
        WindowsPoints.Clear();
        if (_state.IsElevated)
        {
            try
            {
                foreach (var p in await Task.Run(_rp.ListRestorePoints)) WindowsPoints.Add(p);
                ListNote = WindowsPoints.Count == 0 ? "No restore points exist." : $"{WindowsPoints.Count} restore point(s). Points created by this app start with “{AppPaths.RestorePointPrefix}”.";
            }
            catch (Exception ex) { ListNote = "Could not list restore points: " + ex.Message; }
        }
        else ListNote = "Listing Windows restore points requires administrator rights.";

        AppPoints.Clear();
        foreach (var op in _backups.LoadAll().Where(o => o.RestorePoint is not null && o.RestorePoint.Status != RestorePointStatus.NotRequired))
        {
            var r = op.RestorePoint!;
            var when = r.CreationTime ?? r.RequestedAt;
            AppPoints.Add(new AppRestorePointRow(r.Description, when.ToString("yyyy-MM-dd"), when.ToString("HH:mm:ss zzz"), op.Label, r.Status, r.Verified,
                op.FolderPath ?? "", op.HasActiveChanges, r.Message));
        }
    }

    [RelayCommand]
    private async Task CreateManual()
    {
        if (!_state.IsElevated) { _dialogs.Info("Administrator required", "Restart the app as administrator to create restore points."); return; }
        var label = "Manual - " + (string.IsNullOrWhiteSpace(ManualName) ? "Restore Point" : ManualName.Trim());
        RestorePointAttempt attempt;
        using (_busy.Begin("Creating restore point (this can take a minute)…"))
            attempt = await _rp.CreateAsync(label);
        _log.Info("RestorePoint", $"Manual restore point: {attempt.Status} {attempt.Message}");
        if (attempt.Verified) _dialogs.Success("Restore point created", $"“{attempt.Description}”\n{attempt.Message}");
        else _dialogs.Show("Restore point not created", $"{attempt.Status}: {attempt.Message}", Dialogs.DialogTone.Warning);
        await Reload();
    }

    [RelayCommand] private void OpenSystemRestore() => RestorePointService.OpenSystemRestore();
    [RelayCommand] private void OpenProtection() => RestorePointService.OpenSystemProtectionSettings();
}

public sealed partial class ChangeRowViewModel : ObservableObject
{
    public ChangeRecord Record { get; init; } = null!;
    [ObservableProperty] private bool _selected;
    public string Title => Record.Title;
    public string Target => Record.Spec.Target;
    public string Original => Record.OriginalDisplay;
    public string Applied => Record.AppliedDisplay;
    public ChangeStatus Status => Record.Status;
    public bool CanRestore => Record.IsActive && Record.Backup is not null;
    public string RollbackText => !CanRestore ? "–" : Record.HasVerifiedRollback ? "Verified rollback" : "Best effort";
    public string? Error => Record.Error;
}

public sealed partial class BackupRecoveryViewModel : PageViewModel
{
    private readonly BackupService _backups;
    private readonly OperationRunner _runner;
    private readonly DialogService _dialogs;
    private readonly SystemState _state;

    public override string Title => "Backup & Recovery";
    public override string Subtitle => "Every change made by this app with its original value. Restore individual tweaks, whole operations, or everything.";

    public ObservableCollection<OperationRecord> Operations { get; } = new();
    public ObservableCollection<ChangeRowViewModel> Changes { get; } = new();
    [ObservableProperty] private OperationRecord? _selected;
    [ObservableProperty] private string _details = "";
    [ObservableProperty] private string _summary = "";

    public BackupRecoveryViewModel(BackupService backups, OperationRunner runner, DialogService dialogs, SystemState state)
    {
        _backups = backups;
        _runner = runner;
        _dialogs = dialogs;
        _state = state;
        _state.SystemChanged += () => System.Windows.Application.Current.Dispatcher.InvokeAsync(Reload);
    }

    protected override Task LoadAsync() { Reload(); return Task.CompletedTask; }
    protected override Task OnRevisitedAsync() { Reload(); return Task.CompletedTask; }

    [RelayCommand]
    private void Reload()
    {
        var id = Selected?.Id;
        Operations.Clear();
        foreach (var op in _backups.LoadAll().Where(o => o.Kind == OperationKind.Apply && o.Changes.Any(c => c.Backup is not null))) Operations.Add(op);
        Selected = Operations.FirstOrDefault(o => o.Id == id) ?? Operations.FirstOrDefault();
        int active = Operations.Sum(o => o.Changes.Count(c => c.IsActive));
        Summary = $"{Operations.Count} operation(s) with backups · {active} change(s) currently active and restorable · stored in {AppPaths.Operations}";
    }

    partial void OnSelectedChanged(OperationRecord? value)
    {
        Changes.Clear();
        if (value is null) { Details = ""; return; }
        foreach (var c in value.Changes.Where(c => c.Backup is not null)) Changes.Add(new ChangeRowViewModel { Record = c, Selected = false });
        var rp = value.RestorePoint;
        Details = $"Operation {value.Id}\nStarted {value.StartedAt:yyyy-MM-dd HH:mm:ss zzz} ({value.TimeZoneId}) · status {value.Status}\n" +
                  $"Restore point: {(rp is null ? "n/a" : $"{rp.Status}{(rp.Verified ? " (verified)" : "")} — {rp.Description}")}" +
                  (value.BackupOnlyOverrideUsed ? "\n⚠ Applied with application backup only (user override)." : "") + $"\nBackup folder: {value.FolderPath}";
    }

    [RelayCommand]
    private async Task RestoreSelected()
    {
        if (Selected is null) return;
        var ids = Changes.Where(c => c.Selected && c.CanRestore).Select(c => c.Record.Id).ToList();
        if (ids.Count == 0) { _dialogs.Info("Nothing selected", "Tick the changes you want to restore (only active changes can be restored)."); return; }
        await _runner.RollbackAsync(Selected, ids);
        Reload();
    }

    [RelayCommand]
    private async Task RestoreOperation()
    {
        if (Selected is null) return;
        if (!Selected.HasActiveChanges) { _dialogs.Info("Already restored", "All changes of this operation are already restored."); return; }
        await _runner.RollbackAsync(Selected, null);
        Reload();
    }

    [RelayCommand]
    private async Task UndoLast()
    {
        var last = _backups.LoadAll().FirstOrDefault(o => o.Kind == OperationKind.Apply && o.HasActiveChanges);
        if (last is null) { _dialogs.Info("Nothing to undo", "There are no active changes made by this app."); return; }
        await _runner.RollbackAsync(last, null);
        Reload();
    }

    [RelayCommand]
    private async Task RestoreAllRegistry()
    {
        var ops = _backups.LoadAll().Where(o => o.Kind == OperationKind.Apply && o.Changes.Any(c => c.IsActive && c.Spec is RegistryValueSpec or RegistryTokenSpec or RegistryFlagsSpec or AppCompatLayerSpec)).ToList();
        if (ops.Count == 0) { _dialogs.Info("Nothing to restore", "No registry values changed by this app are currently active."); return; }
        if (!_dialogs.Confirm("Restore all original registry values?", $"{ops.Sum(o => o.Changes.Count(c => c.IsActive))} active change(s) across {ops.Count} operation(s) will be reviewed and restored, newest first.", "Continue")) return;
        foreach (var op in ops)
        {
            var ids = op.Changes.Where(c => c.IsActive && c.Spec is RegistryValueSpec or RegistryTokenSpec or RegistryFlagsSpec or AppCompatLayerSpec).Select(c => c.Id).ToList();
            await _runner.RollbackAsync(op, ids);
        }
        Reload();
    }

    [RelayCommand]
    private void ExportSelected()
    {
        if (Selected is null) return;
        var path = _dialogs.SaveFile("Export backup", "ZIP archive (*.zip)|*.zip", $"backup-{Selected.StartedAt:yyyyMMdd-HHmmss}.zip");
        if (path is null) return;
        try { _backups.ExportZip(Selected, path); _dialogs.Success("Exported", path); }
        catch (Exception ex) { _dialogs.Error("Export failed", ex.Message); }
    }

    [RelayCommand]
    private void ExportAll()
    {
        var path = _dialogs.SaveFile("Export all backups", "ZIP archive (*.zip)|*.zip", $"all-backups-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        if (path is null) return;
        try { _backups.ExportAll(path); _dialogs.Success("Exported", path); }
        catch (Exception ex) { _dialogs.Error("Export failed", ex.Message); }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = Selected?.FolderPath ?? AppPaths.Operations;
        if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    [RelayCommand] private void OpenSystemRestore() => RestorePointService.OpenSystemRestore();
}

public sealed partial class HistoryViewModel : PageViewModel
{
    private readonly AuditLogService _audit;
    private readonly LoggingService _log;
    private readonly DialogService _dialogs;

    public override string Title => "Optimization History";
    public override string Subtitle => "Audit trail of every operation this app has performed on this PC (locally retained)";

    public ObservableCollection<AuditEntry> Entries { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public HistoryViewModel(AuditLogService audit, LoggingService log, DialogService dialogs)
    {
        _audit = audit;
        _log = log;
        _dialogs = dialogs;
        _audit.Appended += _ => System.Windows.Application.Current.Dispatcher.InvokeAsync(Reload);
    }

    protected override Task LoadAsync() { Reload(); return Task.CompletedTask; }
    protected override Task OnRevisitedAsync() { Reload(); return Task.CompletedTask; }

    [RelayCommand]
    private void Reload()
    {
        Entries.Clear();
        foreach (var e in _audit.ReadAll()) Entries.Add(e);
        LogEntries.Clear();
        foreach (var l in _log.Recent.Reverse().Take(200)) LogEntries.Add(l);
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var path = _dialogs.SaveFile("Export history", "CSV (*.csv)|*.csv", $"optimization-history-{DateTime.Now:yyyyMMdd}.csv");
        if (path is null) return;
        var sb = new StringBuilder("Timestamp,Kind,Label,Status,Planned,Applied,Failed,RestorePoint,RestorePointStatus,Summary\n");
        string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        foreach (var e in Entries)
            sb.AppendLine(string.Join(",", Q(e.Timestamp.ToString("o")), e.Kind, Q(e.Label), e.Status, e.Planned, e.Applied, e.Failed, Q(e.RestorePoint), e.RestorePointStatus, Q(e.Summary)));
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    [RelayCommand] private void OpenLogs() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Logs}\"") { UseShellExecute = true });
}

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly HardwareMonitoringService _monitor;
    private readonly TweakCatalogService _catalog;

    public override string Title => "Settings";
    public override string Subtitle => "The safest configuration is the default.";

    public AppSettings S => _settings.Current;
    public List<int> RefreshRates { get; } = new() { 1000, 1500, 2000, 3000, 5000 };
    public List<int> RescanIntervals { get; } = new() { 0, 15, 30, 60 };
    public List<RestorePointPolicy> Policies { get; } = Enum.GetValues<RestorePointPolicy>().ToList();
    public List<OptimizationProfile> Profiles => _catalog.Profiles.ToList();
    public string DataFolder => AppPaths.Root;
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "";

    [ObservableProperty] private bool _scanOnStartup;
    [ObservableProperty] private int _refreshMs;
    [ObservableProperty] private int _rescanMinutes;
    [ObservableProperty] private RestorePointPolicy _policy;
    [ObservableProperty] private bool _autoRollback;
    [ObservableProperty] private bool _exportReg;
    [ObservableProperty] private string _defaultProfile = "safe";
    [ObservableProperty] private bool _confirmLowRisk;
    [ObservableProperty] private bool _detailedLogging;
    [ObservableProperty] private int _benchmarkLimit;
    [ObservableProperty] private bool _notifications;
    [ObservableProperty] private bool _purpleAccents;
    [ObservableProperty] private string _pingTarget = "";

    public SettingsViewModel(SettingsService settings, DialogService dialogs, HardwareMonitoringService monitor, TweakCatalogService catalog)
    {
        _settings = settings;
        _dialogs = dialogs;
        _monitor = monitor;
        _catalog = catalog;
        Load();
    }

    private void Load()
    {
        var s = _settings.Current;
        ScanOnStartup = s.ScanOnStartup; RefreshMs = s.DashboardRefreshMs; RescanMinutes = s.HardwareRescanMinutes; Policy = s.RestorePointPolicy;
        AutoRollback = s.AutoRollbackOnFailure; ExportReg = s.ExportRegistryKeysWithBackups; DefaultProfile = s.DefaultProfile; ConfirmLowRisk = s.ConfirmLowRiskChanges;
        DetailedLogging = s.DetailedLogging; BenchmarkLimit = s.BenchmarkHistoryLimit; Notifications = s.NotificationsEnabled; PurpleAccents = s.UsePurpleAccents; PingTarget = s.PingTarget;
    }

    partial void OnPolicyChanged(RestorePointPolicy oldValue, RestorePointPolicy newValue)
    {
        if (newValue == RestorePointPolicy.AllowBackupOnlyOverride && oldValue != newValue &&
            !_dialogs.Confirm("Allow backup-only override?",
                "When Windows cannot create a new restore point (for example because of its 24-hour limit), you will be offered an explicit, per-operation option to continue " +
                "using only this app's verified backups — and only if every change in that operation has a verified rollback.\n\nAn application backup is NOT equivalent to a System Restore point.",
                "Allow override option", "Keep blocking", danger: true))
        {
            Policy = oldValue;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var s = _settings.Current;
        s.ScanOnStartup = ScanOnStartup; s.DashboardRefreshMs = RefreshMs; s.HardwareRescanMinutes = RescanMinutes; s.RestorePointPolicy = Policy;
        s.AutoRollbackOnFailure = AutoRollback; s.ExportRegistryKeysWithBackups = ExportReg; s.DefaultProfile = DefaultProfile; s.ConfirmLowRiskChanges = ConfirmLowRisk;
        s.DetailedLogging = DetailedLogging; s.BenchmarkHistoryLimit = BenchmarkLimit; s.NotificationsEnabled = Notifications; s.UsePurpleAccents = PurpleAccents; s.PingTarget = PingTarget;
        _settings.Save();
        _monitor.IntervalMs = RefreshMs;
        StatusMessage = $"Settings saved {DateTime.Now:HH:mm:ss}.";
    }

    [RelayCommand]
    private void Reset()
    {
        if (!_dialogs.Confirm("Reset settings?", "All settings return to their safe defaults. Backups and history are kept.", "Reset")) return;
        _settings.Reset();
        Load();
        StatusMessage = "Defaults restored.";
    }

    [RelayCommand]
    private void Export()
    {
        var path = _dialogs.SaveFile("Export settings", "JSON (*.json)|*.json", "upco-settings.json");
        if (path is not null) { _settings.Export(path); StatusMessage = "Exported to " + path; }
    }

    [RelayCommand]
    private void Import()
    {
        var path = _dialogs.OpenFile("Import settings", "JSON (*.json)|*.json");
        if (path is null) return;
        try { _settings.Import(path); Load(); StatusMessage = "Settings imported."; }
        catch (Exception ex) { _dialogs.Error("Import failed", ex.Message); }
    }

    [RelayCommand] private void OpenDataFolder() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });
}
