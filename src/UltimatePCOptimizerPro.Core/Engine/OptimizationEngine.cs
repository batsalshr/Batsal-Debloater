using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Core.Engine;

public sealed class OperationRequest
{
    /// <summary>Used in the restore point name, e.g. "Registry Tweaks".</summary>
    public string Label { get; init; } = "Optimization";
    public List<PlannedChange> Changes { get; init; } = new();
}

/// <summary>Abstraction over restore point creation so the engine can be tested without touching System Restore.</summary>
public interface IRestorePointProvider
{
    Task<RestorePointAttempt> CreateAsync(string label, CancellationToken ct);
    SystemProtectionStatus GetProtectionStatus();
}

public sealed class WindowsRestorePointProvider : IRestorePointProvider
{
    private readonly RestorePointService _service;
    public WindowsRestorePointProvider(RestorePointService service) => _service = service;
    public Task<RestorePointAttempt> CreateAsync(string label, CancellationToken ct) => _service.CreateAsync(label, ct);
    public SystemProtectionStatus GetProtectionStatus() => _service.GetProtectionStatus();
}

/// <summary>
/// The single entry point for every persistent system modification.
/// Order of operations: preflight → user approval → capture originals → save backup → restore point (verified) →
/// apply → verify each change → automatic rollback on failure → audit.
/// </summary>
public sealed class OptimizationEngine
{
    private readonly HandlerRegistry _handlers;
    private readonly IRestorePointProvider _restorePoints;
    private readonly BackupService _backups;
    private readonly AuditLogService _audit;
    private readonly SettingsService _settings;
    private readonly LoggingService _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<bool> _isElevated;

    public event Action<OperationRecord>? OperationCompleted;
    public bool IsBusy => _gate.CurrentCount == 0;

    public OptimizationEngine(HandlerRegistry handlers, IRestorePointProvider restorePoints, BackupService backups, AuditLogService audit,
        SettingsService settings, LoggingService log, Func<bool>? isElevated = null)
    {
        _handlers = handlers;
        _restorePoints = restorePoints;
        _backups = backups;
        _audit = audit;
        _settings = settings;
        _log = log;
        _isElevated = isElevated ?? (() => Elevation.IsElevated);
    }

    public HandlerRegistry Handlers => _handlers;

    public Task<OperationRecord> ExecuteAsync(OperationRequest request, IApprovalProvider approval, IProgress<string>? progress = null, CancellationToken ct = default)
        => RunExclusive(() => ExecuteCore(request, approval, progress, ct), ct);

    public Task<OperationRecord> RollbackAsync(OperationRecord source, IReadOnlyCollection<Guid>? changeIds, IApprovalProvider approval, IProgress<string>? progress = null, CancellationToken ct = default)
        => RunExclusive(() => RollbackCore(source, changeIds, approval, progress, ct), ct);

    private async Task<OperationRecord> RunExclusive(Func<Task<OperationRecord>> work, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct))
            throw new InvalidOperationException("Another optimization is already running. Wait for it to finish.");
        try { return await Task.Run(work, ct); }
        finally { _gate.Release(); }
    }

    // ------------------------------------------------------------------------------------------------ Apply

    private async Task<OperationRecord> ExecuteCore(OperationRequest request, IApprovalProvider approval, IProgress<string>? progress, CancellationToken ct)
    {
        var settings = _settings.Current;
        var op = new OperationRecord { Label = request.Label, Kind = OperationKind.Apply, StartedAt = DateTimeOffset.Now };
        op.AddLog($"Operation '{request.Label}' started ({request.Changes.Count} requested changes). Time zone: {TimeZoneInfo.Local.DisplayName}");

        // 1. Preflight: identify what will actually change.
        progress?.Report("Checking compatibility and current settings…");
        var reviewItems = new List<ReviewItem>();
        foreach (var pc in request.Changes)
        {
            var rec = new ChangeRecord
            {
                Title = pc.Title, TweakId = pc.TweakId, Category = pc.Category, Risk = pc.Risk, Restart = pc.Restart,
                Spec = pc.Spec, AppliedDisplay = pc.Spec.Desired
            };
            IChangeHandler handler;
            Preflight pre;
            try
            {
                handler = _handlers.Get(pc.Spec);
                pre = handler.Preflight(pc.Spec);
                rec.HasVerifiedRollback = handler.HasVerifiedRollback(pc.Spec);
                pre.RequiresAdmin |= handler.RequiresAdmin(pc.Spec);
            }
            catch (Exception ex)
            {
                pre = Preflight.Unsupported("Could not read the current state: " + ex.Message);
            }
            rec.OriginalDisplay = pre.Current;
            string? skip = null;
            if (!pre.Supported) { rec.Status = ChangeStatus.SkippedUnsupported; rec.Error = pre.Message; skip = pre.Message; }
            else if (pre.AlreadyApplied) { rec.Status = ChangeStatus.SkippedAlreadyApplied; skip = "Already set to the target value"; }
            op.Changes.Add(rec);
            reviewItems.Add(new ReviewItem(pc.Title, pc.Category, pc.Risk, pc.Spec.Target, pre.Current, pc.Spec.Desired, pc.Restart,
                rec.HasVerifiedRollback, pre.RequiresAdmin, pc.Notes ?? (string.IsNullOrEmpty(pre.Message) ? null : pre.Message), skip));
        }

        var active = op.Changes.Where(c => c.Status == ChangeStatus.Planned).ToList();
        if (active.Count == 0)
        {
            op.Status = OperationStatus.NothingToDo;
            op.FinishedAt = DateTimeOffset.Now;
            op.AddLog("Nothing to change: every item is already applied or unsupported.");
            return op;
        }

        bool needsRestorePoint = active.Any(c => _handlers.Get(c.Spec).RequiresRestorePoint(c.Spec));
        bool needsAdmin = needsRestorePoint || reviewItems.Any(i => i.SkipReason is null && i.RequiresAdmin);
        if (needsAdmin && !_isElevated())
        {
            return Block(op, "Administrator rights are required to create a restore point and apply these changes. Restart the application as administrator.");
        }

        // 2. User approval of the exact plan.
        var review = new PlanReview
        {
            Label = request.Label,
            Items = reviewItems,
            RestorePointRequired = needsRestorePoint,
            ProposedRestorePointName = RestorePointService.BuildName(request.Label, DateTimeOffset.Now),
            Protection = needsRestorePoint ? SafeProtectionStatus() : null
        };
        if (!await approval.ConfirmPlanAsync(review, ct))
        {
            op.Status = OperationStatus.Cancelled;
            op.FinishedAt = DateTimeOffset.Now;
            op.AddLog("Cancelled by the user at plan review. Nothing was changed.");
            _audit.Append(op);
            return op;
        }

        // 3. Capture original values and persist the backup before anything else happens.
        progress?.Report("Backing up original settings…");
        _backups.CreateFolder(op);
        foreach (var rec in active)
        {
            try
            {
                rec.Backup = _handlers.Get(rec.Spec).Capture(rec.Spec);
                rec.OriginalDisplay = rec.Backup.Display;
                rec.Status = ChangeStatus.BackedUp;
            }
            catch (Exception ex)
            {
                rec.Status = ChangeStatus.Failed;
                rec.Error = "Backup failed, change skipped: " + ex.Message;
                op.AddLog($"[{rec.Title}] backup failed: {ex.Message}");
            }
        }
        active = active.Where(c => c.Status == ChangeStatus.BackedUp).ToList();
        if (settings.ExportRegistryKeysWithBackups)
        {
            try { _backups.ExportRegistryKeys(op); }
            catch (Exception ex) { op.AddLog("Supplementary .reg export failed: " + ex.Message); }
        }
        _backups.Save(op);
        op.AddLog($"Original state of {active.Count} item(s) saved to {op.FolderPath}");
        if (active.Count == 0) return Block(op, "No original values could be backed up, so nothing was changed.");

        // 4. Restore point.
        if (needsRestorePoint)
        {
            progress?.Report("Creating a System Restore point (this can take a minute)…");
            var attempt = await _restorePoints.CreateAsync(request.Label, ct);
            op.RestorePoint = attempt;
            op.AddLog($"Restore point '{attempt.Description}': {attempt.Status}. {attempt.Message}");
            _backups.Save(op);
            if (!attempt.Verified)
            {
                var withoutRollback = active.Where(c => !c.HasVerifiedRollback).Select(c => c.Title).ToList();
                var failure = new RestorePointFailureReview
                {
                    Label = request.Label,
                    Attempt = attempt,
                    OverrideAllowedBySettings = settings.RestorePointPolicy == RestorePointPolicy.AllowBackupOnlyOverride,
                    AllChangesHaveVerifiedRollback = withoutRollback.Count == 0,
                    ChangesWithoutVerifiedRollback = withoutRollback
                };
                bool proceed = await approval.ConfirmBackupOnlyOverrideAsync(failure, ct) && failure.OverridePossible;
                if (!proceed)
                {
                    foreach (var c in active) c.Status = ChangeStatus.Planned;
                    return Block(op, $"No verified restore point: {attempt.Message} Nothing was changed.");
                }
                op.BackupOnlyOverrideUsed = true;
                op.AddLog("User explicitly chose to continue with application backups only (advanced override).");
            }
        }
        else
        {
            op.RestorePoint = new RestorePointAttempt { Status = RestorePointStatus.NotRequired, Message = "Not required for these changes." };
        }

        // 5. Apply + verify, with automatic rollback on failure.
        op.Status = OperationStatus.Running;
        _backups.Save(op);
        var attempted = new List<ChangeRecord>();
        bool failed = false;
        int index = 0;
        foreach (var rec in active)
        {
            if (ct.IsCancellationRequested) { op.AddLog("Cancellation requested; stopping before the next change."); failed = true; break; }
            progress?.Report($"Applying {++index}/{active.Count}: {rec.Title}");
            var handler = _handlers.Get(rec.Spec);
            attempted.Add(rec);
            try
            {
                handler.Apply(rec.Spec, rec.Backup!);
                rec.AppliedAt = DateTimeOffset.Now;
                var v = handler.Verify(rec.Spec, rec.Backup!);
                rec.AppliedDisplay = v.Actual;
                rec.Status = v.Outcome switch
                {
                    VerifyOutcome.Verified => ChangeStatus.Applied,
                    VerifyOutcome.PendingRestart => ChangeStatus.PendingRestart,
                    _ => ChangeStatus.Failed
                };
                if (rec.Status == ChangeStatus.Failed)
                {
                    rec.Error = $"Verification failed: expected {rec.Spec.Desired}, found {v.Actual}.";
                    failed = true;
                }
                op.AddLog($"[{rec.Title}] {rec.Status}: {rec.Spec.Target} = {v.Actual}");
            }
            catch (Exception ex)
            {
                rec.Status = ChangeStatus.Failed;
                rec.Error = ex.Message;
                failed = true;
                op.AddLog($"[{rec.Title}] FAILED: {ex.Message}");
                _log.Error("Engine", $"Apply failed for {rec.Title}", ex);
            }
            _backups.Save(op);
            if (failed && settings.AutoRollbackOnFailure) break;
        }

        if (failed && settings.AutoRollbackOnFailure)
        {
            progress?.Report("A change failed — rolling back this batch…");
            op.AddLog("Automatic rollback of this batch started.");
            foreach (var rec in Enumerable.Reverse(attempted))
            {
                try
                {
                    var handler = _handlers.Get(rec.Spec);
                    handler.Restore(rec.Spec, rec.Backup!);
                    var v = handler.VerifyRestored(rec.Spec, rec.Backup!);
                    var wasFailed = rec.Status == ChangeStatus.Failed;
                    rec.Status = v.Ok ? ChangeStatus.RolledBack : ChangeStatus.RollbackFailed;
                    if (wasFailed && v.Ok) rec.Error = (rec.Error ?? "") + " (original value restored)";
                    op.AddLog($"[{rec.Title}] rollback {(v.Ok ? "verified" : "FAILED")}: {v.Actual}");
                }
                catch (Exception ex)
                {
                    rec.Status = ChangeStatus.RollbackFailed;
                    rec.Error = (rec.Error ?? "") + " | Rollback failed: " + ex.Message;
                    op.AddLog($"[{rec.Title}] rollback FAILED: {ex.Message}");
                }
            }
            op.Status = OperationStatus.RolledBack;
        }
        else
        {
            op.Status = failed ? OperationStatus.CompletedWithErrors : OperationStatus.Completed;
        }

        op.FinishedAt = DateTimeOffset.Now;
        op.AddLog($"Finished: {op.Status}. Applied {op.AppliedCount}, failed {op.FailedCount}.");
        _backups.Save(op);
        _audit.Append(op);
        OperationCompleted?.Invoke(op);
        return op;
    }

    private SystemProtectionStatus? SafeProtectionStatus()
    {
        try { return _restorePoints.GetProtectionStatus(); }
        catch (Exception ex) { _log.Warn("Engine", "Protection status unavailable", ex); return null; }
    }

    private OperationRecord Block(OperationRecord op, string reason)
    {
        op.Status = OperationStatus.Blocked;
        op.BlockReason = reason;
        op.FinishedAt = DateTimeOffset.Now;
        op.AddLog("BLOCKED: " + reason);
        if (op.FolderPath is not null) _backups.Save(op);
        _audit.Append(op);
        OperationCompleted?.Invoke(op);
        return op;
    }

    // ------------------------------------------------------------------------------------------------ Rollback

    private async Task<OperationRecord> RollbackCore(OperationRecord source, IReadOnlyCollection<Guid>? changeIds, IApprovalProvider approval, IProgress<string>? progress, CancellationToken ct)
    {
        var targets = source.Changes.Where(c => c.IsActive && c.Backup is not null && (changeIds is null || changeIds.Contains(c.Id))).ToList();
        var op = new OperationRecord { Kind = OperationKind.Rollback, Label = $"Rollback - {source.Label}", RollbackOf = source.Id };
        if (targets.Count == 0)
        {
            op.Status = OperationStatus.NothingToDo;
            op.AddLog("No active changes with backups were selected.");
            return op;
        }

        var items = targets.Select(t =>
        {
            var h = _handlers.Get(t.Spec);
            string current;
            try { current = h.Preflight(t.Spec).Current; } catch (Exception ex) { current = "unreadable: " + ex.Message; }
            return new ReviewItem(t.Title, t.Category, t.Risk, t.Spec.Target, current, t.Backup!.Display, t.Restart, h.HasVerifiedRollback(t.Spec), h.RequiresAdmin(t.Spec), null, null);
        }).ToList();

        if (items.Any(i => i.RequiresAdmin) && !_isElevated())
            return Block(op, "Administrator rights are required to restore these settings.");

        var review = new PlanReview { Label = op.Label, IsRollback = true, Items = items, RestorePointRequired = false };
        if (!await approval.ConfirmPlanAsync(review, ct))
        {
            op.Status = OperationStatus.Cancelled;
            op.AddLog("Rollback cancelled by the user.");
            return op;
        }

        _backups.CreateFolder(op);
        // Restoring returns the PC to a previously recorded state, so it is never blocked by restore point throttling,
        // but a restore point is still attempted when possible.
        if (_isElevated())
        {
            progress?.Report("Attempting a restore point before rollback…");
            op.RestorePoint = await _restorePoints.CreateAsync(op.Label, ct);
            op.AddLog($"Restore point: {op.RestorePoint.Status}. {op.RestorePoint.Message} (rollback proceeds regardless)");
        }

        op.Status = OperationStatus.Running;
        int i = 0;
        foreach (var t in Enumerable.Reverse(targets))
        {
            progress?.Report($"Restoring {++i}/{targets.Count}: {t.Title}");
            var copy = new ChangeRecord
            {
                Title = t.Title, TweakId = t.TweakId, Category = t.Category, Risk = t.Risk, Restart = t.Restart, Spec = t.Spec,
                Backup = t.Backup, OriginalDisplay = t.AppliedDisplay, AppliedDisplay = t.Backup!.Display
            };
            op.Changes.Add(copy);
            try
            {
                var h = _handlers.Get(t.Spec);
                h.Restore(t.Spec, t.Backup!);
                var v = h.VerifyRestored(t.Spec, t.Backup!);
                t.Status = copy.Status = v.Ok ? ChangeStatus.Restored : ChangeStatus.RestoreFailed;
                if (!v.Ok) t.Error = copy.Error = $"Restore verification failed: found {v.Actual}";
                t.RestoredAt = copy.AppliedAt = DateTimeOffset.Now;
                op.AddLog($"[{t.Title}] {t.Status}: {v.Actual}");
            }
            catch (Exception ex)
            {
                t.Status = copy.Status = ChangeStatus.RestoreFailed;
                t.Error = copy.Error = ex.Message;
                op.AddLog($"[{t.Title}] restore FAILED: {ex.Message}");
            }
        }
        op.Status = op.Changes.Any(c => c.Status == ChangeStatus.RestoreFailed) ? OperationStatus.CompletedWithErrors : OperationStatus.Completed;
        op.FinishedAt = DateTimeOffset.Now;
        source.AddLog($"Rollback operation {op.Id} restored {op.Changes.Count(c => c.Status == ChangeStatus.Restored)} change(s).");
        _backups.Save(source);
        _backups.Save(op);
        _audit.Append(op);
        OperationCompleted?.Invoke(op);
        return op;
    }
}
