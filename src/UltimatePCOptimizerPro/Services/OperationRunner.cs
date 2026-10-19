using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using UltimatePCOptimizerPro.Core.Engine;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Dialogs;

namespace UltimatePCOptimizerPro.Services;

/// <summary>Global busy overlay state.</summary>
public sealed partial class BusyService : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _message = "";

    public IDisposable Begin(string message)
    {
        Message = message;
        IsBusy = true;
        return new Scope(this);
    }

    private sealed class Scope(BusyService owner) : IDisposable
    {
        public void Dispose() => owner.IsBusy = false;
    }
}

/// <summary>UI-side wrapper around the OptimizationEngine. Every page applies and restores changes through this class.</summary>
public sealed class OperationRunner
{
    private readonly OptimizationEngine _engine;
    private readonly DialogService _dialogs;
    private readonly BusyService _busy;
    private readonly SystemState _state;
    private readonly BackupService _backups;
    private readonly LoggingService _log;
    private readonly SettingsService _settings;

    public OperationRunner(OptimizationEngine engine, DialogService dialogs, BusyService busy, SystemState state, BackupService backups, LoggingService log, SettingsService settings)
    {
        _settings = settings;
        _engine = engine;
        _dialogs = dialogs;
        _busy = busy;
        _state = state;
        _backups = backups;
        _log = log;
    }

    private bool EnsureAdmin(string action)
    {
        if (_state.IsElevated) return true;
        var r = _dialogs.Show("Administrator rights required",
            $"{action} requires administrator rights because a System Restore point must be created first (and many settings are machine-wide).\n\n" +
            "Restart ULTIMATE PC OPTIMIZER PRO as administrator?", DialogTone.Warning, "Restart as administrator", "Cancel");
        if (r == DialogResultKind.Primary && Elevation.RelaunchElevated()) System.Windows.Application.Current.Shutdown();
        return false;
    }

    public async Task<OperationRecord?> ApplyAsync(string label, IReadOnlyCollection<PlannedChange> changes)
    {
        if (changes.Count == 0)
        {
            _dialogs.Info("Nothing selected", "Select at least one change to apply.");
            return null;
        }
        if (!EnsureAdmin("Applying optimizations")) return null;
        if (_engine.IsBusy) { _dialogs.Info("Please wait", "Another operation is still running."); return null; }

        OperationRecord op;
        using (_busy.Begin("Preparing…"))
        {
            var progress = new Progress<string>(m => _busy.Message = m);
            try { op = await _engine.ExecuteAsync(new OperationRequest { Label = label, Changes = changes.ToList() }, _dialogs, progress); }
            catch (Exception ex)
            {
                _log.Error("UI", "Operation failed", ex);
                _dialogs.Error("Operation failed", ex.Message);
                return null;
            }
        }

        if (op.Status is OperationStatus.Completed or OperationStatus.CompletedWithErrors)
            await ConfirmDisplayChangesAsync(op);

        ShowSummary(op);
        _state.NotifySystemChanged();
        return op;
    }

    public async Task<OperationRecord?> RollbackAsync(OperationRecord source, IReadOnlyCollection<Guid>? changeIds, bool silentApproval = false)
    {
        if (_engine.IsBusy) { _dialogs.Info("Please wait", "Another operation is still running."); return null; }
        OperationRecord op;
        using (_busy.Begin("Restoring original settings…"))
        {
            var progress = new Progress<string>(m => _busy.Message = m);
            try
            {
                IApprovalProvider approval = silentApproval ? new AutoApproveProvider() : _dialogs;
                op = await _engine.RollbackAsync(source, changeIds, approval, progress);
            }
            catch (Exception ex)
            {
                _dialogs.Error("Restore failed", ex.Message);
                return null;
            }
        }
        if (!silentApproval) ShowSummary(op);
        _state.NotifySystemChanged();
        return op;
    }

    /// <summary>Restores the most recent active changes recorded for a tweak id.</summary>
    public async Task RestoreTweakAsync(string tweakId)
    {
        var found = _backups.FindActiveChangesForTweak(tweakId);
        if (found is null)
        {
            _dialogs.Info("No backup from this app",
                "This setting was not changed by ULTIMATE PC OPTIMIZER PRO (or it was already restored), so there is no recorded original value to restore.");
            return;
        }
        await RollbackAsync(found.Value.Op, found.Value.Changes.Select(c => c.Id).ToList());
    }

    /// <summary>Display changes must be confirmed by the user within 15 s, otherwise they are reverted (protects against blank screens).</summary>
    private async Task ConfirmDisplayChangesAsync(OperationRecord op)
    {
        var displayChanges = op.Changes.Where(c => c.Spec is DisplayModeSpec && c.Status == ChangeStatus.Applied).ToList();
        if (displayChanges.Count == 0) return;
        var result = _dialogs.Show("Keep these display settings?",
            string.Join("\n", displayChanges.Select(c => $"{c.Title}: {c.AppliedDisplay}")) + "\n\nIf the screen looks wrong or stays blank, the previous mode is restored automatically.",
            DialogTone.Question, "Keep changes", "Revert", configure: d => d.StartCountdown(15, DialogResultKind.Secondary));
        if (result != DialogResultKind.Primary)
        {
            var saved = _backups.Load(op.Id) ?? op;
            await RollbackAsync(saved, displayChanges.Select(c => c.Id).ToList(), silentApproval: true);
            op.AddLog("Display change reverted (not confirmed by the user).");
        }
    }

    private void ShowSummary(OperationRecord op)
    {
        if (op.Status == OperationStatus.Cancelled) return;
        if (_settings.Current.NotificationsEnabled && System.Windows.Application.Current.MainWindow is MainWindow mw) mw.FlashIfInactive();
        if (op.Status == OperationStatus.NothingToDo)
        {
            var skipped = op.Changes.Select(c => DialogService.Row(c.Title, c.Spec.Target, c.Error ?? "Already at the target value",
                c.Status == ChangeStatus.SkippedAlreadyApplied ? "Already set" : "Unsupported", c.Status == ChangeStatus.SkippedAlreadyApplied ? "Success" : "Neutral"));
            _dialogs.Show("Nothing to change", "Every selected item is already applied or not supported on this PC.", DialogTone.Info, rows: skipped);
            return;
        }
        if (op.Status == OperationStatus.Blocked && op.RestorePoint is null)
        {
            _dialogs.Show("Operation blocked", op.BlockReason ?? "", DialogTone.Danger);
            return;
        }
        if (op.Status == OperationStatus.Blocked) return; // the restore-point dialog already explained it

        var rows = op.Changes.Select(c => DialogService.Row(c.Title, c.Spec.Target,
            c.Error ?? (c.Status is ChangeStatus.Applied or ChangeStatus.Restored or ChangeStatus.PendingRestart ? $"{c.OriginalDisplay}  →  {c.AppliedDisplay}" : null),
            c.Status.ToString(), c.Status switch
            {
                ChangeStatus.Applied or ChangeStatus.Restored or ChangeStatus.RolledBack => "Success",
                ChangeStatus.PendingRestart => "Warning",
                ChangeStatus.Failed or ChangeStatus.RollbackFailed or ChangeStatus.RestoreFailed => "Danger",
                _ => "Neutral"
            }, dim: c.Status is ChangeStatus.SkippedAlreadyApplied or ChangeStatus.SkippedUnsupported)).ToList();

        var rp = op.RestorePoint;
        string rpText = rp is null ? "" : rp.Status switch
        {
            RestorePointStatus.Created => $"Restore point verified: “{rp.Description}” (#{rp.SequenceNumber}).",
            RestorePointStatus.NotRequired => "",
            _ => $"Restore point: {rp.Status} — {rp.Message}" + (op.BackupOnlyOverrideUsed ? " (continued with application backup only, as you chose)" : "")
        };
        var restart = op.MaxRestart;
        string msg = op.Status switch
        {
            OperationStatus.Completed => $"{(op.Kind == OperationKind.Apply ? op.AppliedCount : op.Changes.Count(c => c.Status == ChangeStatus.Restored))} change(s) applied and verified.",
            OperationStatus.CompletedWithErrors => $"Completed with errors: {op.FailedCount} change(s) failed. Successful changes were kept (automatic rollback is off).",
            OperationStatus.RolledBack => "A change failed, so every change in this batch was rolled back to its original value.",
            _ => op.Status.ToString()
        };
        msg += string.IsNullOrEmpty(rpText) ? "" : "\n\n" + rpText;
        if (restart > RestartRequirement.None) msg += $"\n\nRequired to take full effect: {DialogService.RestartText(restart)}.";
        msg += $"\n\nBackup: {op.FolderPath}";

        string? tertiary = restart switch
        {
            RestartRequirement.ExplorerRestart => "Restart Explorer now",
            RestartRequirement.Restart => "Restart PC now",
            _ => null
        };
        var tone = op.Status == OperationStatus.Completed ? DialogTone.Success : DialogTone.Warning;
        var res = _dialogs.Show(op.Kind == OperationKind.Apply ? "Optimization finished" : "Restore finished", msg, tone, "Done", tertiary: tertiary, rows: rows);
        if (res == DialogResultKind.Tertiary)
        {
            if (restart == RestartRequirement.ExplorerRestart) _ = ExplorerService.RestartAsync();
            else if (restart == RestartRequirement.Restart && _dialogs.Confirm("Restart now?", "Save your work first. The PC will restart in 10 seconds.", "Restart", "Cancel", danger: true))
                Process.Start(new ProcessStartInfo(ProcessRunner.System32("shutdown.exe"), "/r /t 10 /c \"ULTIMATE PC OPTIMIZER PRO: restarting to finish applying settings\"") { CreateNoWindow = true, UseShellExecute = false });
        }
    }
}
