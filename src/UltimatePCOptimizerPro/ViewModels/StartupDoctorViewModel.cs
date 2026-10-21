using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

public sealed partial class CauseItem : ObservableObject
{
    public StartupCause Cause { get; init; } = null!;
    [ObservableProperty] private bool _selected;
    public string Title => Cause.Title;
    public string Detail => Cause.Detail;
    public string Source => Cause.Source;
    public string Fix => Cause.Fix;
    public bool Fixable => Cause.Fixable;
    public string TimeText => Cause.Seconds is { } s ? $"{s:0.0} s" : "not measured";
    public bool IsFirmware => Cause.Source == "Firmware";
}

public sealed partial class StartupDoctorViewModel : PageViewModel
{
    private readonly StartupDoctorService _doctor;
    private readonly OperationRunner _runner;
    private readonly SystemState _state;
    private readonly DialogService _dialogs;

    public override string Title => "Fix Slow Startup";
    public override string Subtitle => "Measures where your boot time goes (firmware vs Windows vs startup apps), then fixes what software can fix";

    public ObservableCollection<CauseItem> Causes { get; } = new();
    public ObservableCollection<BootRecord> Boots { get; } = new();
    public ObservableCollection<string> Notes { get; } = new();

    [ObservableProperty] private string _firmwareText = "–";
    [ObservableProperty] private string _windowsText = "–";
    [ObservableProperty] private string _verdict = "Press “Scan startup” to analyse the last boots of this PC.";
    [ObservableProperty] private string _eventLogNote = "";
    [ObservableProperty] private int _fixableCount;
    [ObservableProperty] private double _firmwareShare;
    [ObservableProperty] private bool _hasReport;

    public StartupDoctorViewModel(StartupDoctorService doctor, OperationRunner runner, SystemState state, DialogService dialogs)
    {
        _doctor = doctor;
        _runner = runner;
        _state = state;
        _dialogs = dialogs;
        _state.SystemChanged += () => { if (HasReport) _ = Scan(); };
    }

    protected override Task LoadAsync() => Scan();

    [RelayCommand]
    private async Task Scan()
    {
        await RunBusy("Reading Windows boot diagnostics and startup items…", async () =>
        {
            var snapshot = _state.Snapshot ?? await _state.ScanAsync();
            var r = await _doctor.AnalyzeAsync(snapshot);
            Causes.Clear();
            foreach (var c in r.Causes) Causes.Add(new CauseItem { Cause = c, Selected = c.PreSelected });
            Boots.Clear();
            foreach (var b in r.Boots) Boots.Add(b);
            Notes.Clear();
            foreach (var n in r.Notes) Notes.Add(n);
            EventLogNote = r.EventLogNote ?? "";
            FirmwareText = r.FirmwareSec is { } f ? $"{f:0.0} s" : "not reported";
            WindowsText = r.LatestBoot is { } b0 ? $"{b0.TotalSec:0.0} s" : "not measured";
            double fw = r.FirmwareSec ?? 0, win = r.LatestBoot?.TotalSec ?? 0;
            FirmwareShare = fw > 0 && win > 0 ? 100 * fw / (fw + win) : 0; // only meaningful when both parts were measured
            FixableCount = r.Causes.Count(c => c.Fixable);
            HasReport = true;
            Verdict = BuildVerdict(r.FirmwareSec, r.LatestBoot?.TotalSec, r.LatestBoot?.PostBootSec);
            StatusMessage = "";
        });
    }

    private static string BuildVerdict(double? fw, double? win, double? post)
    {
        if (fw is null && win is null) return "No timing data available. See the notes below.";
        var parts = new List<string>();
        if (fw is { } f) parts.Add($"firmware {f:0.0} s");
        if (win is { } w) parts.Add($"Windows {w:0.0} s");
        string head = $"Last boot: {string.Join(" + ", parts)}. ";
        if (fw is >= 10 && (win is null || fw > win))
            return head + "Most of your wait is the motherboard firmware, which Windows cannot speed up — see the first item for UEFI settings that usually cut it. Startup-app fixes below only affect the Windows part.";
        if (win is >= 40) return head + "Windows itself is slow to start. Disabling unneeded startup items is likely to help.";
        return head + "Boot time is reasonable; the items below are optional clean-up.";
    }

    [RelayCommand]
    private async Task ApplySelected()
    {
        var changes = Causes.Where(c => c.Selected && c.Fixable).SelectMany(c => c.Cause.Changes).ToList();
        if (changes.Count == 0) { _dialogs.Info("Nothing selected", "Tick the startup items you want to disable first."); return; }
        var op = await _runner.ApplyAsync("Startup Optimization", changes);
        if (op is not null) StatusMessage = "Sign out or restart, then scan again — Windows records the new boot time after the next boot.";
    }

    [RelayCommand] private void SelectAll() { foreach (var c in Causes.Where(c => c.Fixable)) c.Selected = true; }
    [RelayCommand] private void SelectNone() { foreach (var c in Causes) c.Selected = false; }

    [RelayCommand]
    private void RebootToFirmware()
    {
        if (!_dialogs.Confirm("Restart into UEFI/BIOS settings?",
                "The PC will restart in 10 seconds directly into your motherboard firmware setup so you can enable Fast Boot / memory context restore. Save your work first. " +
                "Nothing is changed automatically — and be careful in firmware: only change the options you understand.", "Restart now", "Cancel", danger: true)) return;
        try
        {
            Process.Start(new ProcessStartInfo(ProcessRunner.System32("shutdown.exe"), "/r /fw /t 10 /c \"ULTIMATE PC OPTIMIZER PRO: restarting to UEFI settings\"") { CreateNoWindow = true, UseShellExecute = false });
        }
        catch (Exception ex) { _dialogs.Error("Could not restart", ex.Message); }
    }

    [RelayCommand] private void OpenTaskManager() => Process.Start(new ProcessStartInfo("taskmgr.exe", "/0 /startup") { UseShellExecute = true });
}
