using System.Text.Json.Serialization;

namespace UltimatePCOptimizerPro.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeStatus
{
    Planned,
    SkippedUnsupported,
    SkippedAlreadyApplied,
    BackedUp,
    Applied,
    PendingRestart,
    Failed,
    RolledBack,
    RollbackFailed,
    Restored,
    RestoreFailed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperationStatus
{
    Planned,
    Cancelled,
    Blocked,
    Running,
    Completed,
    CompletedWithErrors,
    RolledBack,
    NothingToDo
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperationKind { Apply, Rollback }

/// <summary>Original state captured before a change. Values are handler specific.</summary>
public sealed class BackupState
{
    public Dictionary<string, string?> Values { get; set; } = new();
    /// <summary>Human readable original state.</summary>
    public string Display { get; set; } = "";

    public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
    public bool GetBool(string key) => bool.TryParse(Get(key), out var b) && b;
    public void Set(string key, string? value) => Values[key] = value;
}

/// <summary>A change requested by a module before it enters the engine.</summary>
public sealed class PlannedChange
{
    public string Title { get; set; } = "";
    public string? TweakId { get; set; }
    public string Category { get; set; } = "";
    public RiskLevel Risk { get; set; } = RiskLevel.Low;
    public RestartRequirement Restart { get; set; } = RestartRequirement.None;
    public ChangeSpec Spec { get; set; } = null!;
    public string? Notes { get; set; }
}

public sealed class ChangeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? TweakId { get; set; }
    public string Category { get; set; } = "";
    public RiskLevel Risk { get; set; }
    public RestartRequirement Restart { get; set; }
    public ChangeSpec Spec { get; set; } = null!;
    public BackupState? Backup { get; set; }
    public ChangeStatus Status { get; set; } = ChangeStatus.Planned;
    public string OriginalDisplay { get; set; } = "";
    public string AppliedDisplay { get; set; } = "";
    public string? Error { get; set; }
    public bool HasVerifiedRollback { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? RestoredAt { get; set; }

    [JsonIgnore]
    public bool IsActive => Status is ChangeStatus.Applied or ChangeStatus.PendingRestart or ChangeStatus.RestoreFailed;
}

/// <summary>Everything the engine did for one user action. Persisted as operation.json.</summary>
public sealed class OperationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public OperationKind Kind { get; set; } = OperationKind.Apply;
    public string Label { get; set; } = "";
    public Guid? RollbackOf { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; set; }
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public string MachineName { get; set; } = Environment.MachineName;
    public string UserName { get; set; } = Environment.UserName;
    public string AppVersion { get; set; } = typeof(OperationRecord).Assembly.GetName().Version?.ToString() ?? "";
    public OperationStatus Status { get; set; } = OperationStatus.Planned;
    public RestorePointAttempt? RestorePoint { get; set; }
    public bool BackupOnlyOverrideUsed { get; set; }
    public string? BlockReason { get; set; }
    public string? FolderPath { get; set; }
    public List<ChangeRecord> Changes { get; set; } = new();
    public List<string> Log { get; set; } = new();

    [JsonIgnore] public int AppliedCount => Changes.Count(c => c.Status is ChangeStatus.Applied or ChangeStatus.PendingRestart);
    [JsonIgnore] public int FailedCount => Changes.Count(c => c.Status is ChangeStatus.Failed or ChangeStatus.RollbackFailed or ChangeStatus.RestoreFailed);
    [JsonIgnore] public bool HasActiveChanges => Changes.Any(c => c.IsActive);
    [JsonIgnore] public RestartRequirement MaxRestart => Changes.Where(c => c.Status is ChangeStatus.Applied or ChangeStatus.PendingRestart or ChangeStatus.Restored)
        .Select(c => c.Restart).DefaultIfEmpty(RestartRequirement.None).Max();

    public void AddLog(string message) => Log.Add($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}  {message}");
}

public sealed class AuditEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public Guid OperationId { get; set; }
    public OperationKind Kind { get; set; }
    public string Label { get; set; } = "";
    public OperationStatus Status { get; set; }
    public int Planned { get; set; }
    public int Applied { get; set; }
    public int Failed { get; set; }
    public string? RestorePoint { get; set; }
    public RestorePointStatus? RestorePointStatus { get; set; }
    public string Summary { get; set; } = "";
}
