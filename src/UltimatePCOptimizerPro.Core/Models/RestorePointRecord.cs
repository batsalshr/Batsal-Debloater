using System.Text.Json.Serialization;

namespace UltimatePCOptimizerPro.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestorePointStatus
{
    NotRequired,
    Created,
    /// <summary>Windows accepted the request but did not create a point (frequency limit).</summary>
    Throttled,
    Disabled,
    Unsupported,
    AccessDenied,
    Failed
}

/// <summary>What happened when the app tried to create a restore point for an operation.</summary>
public sealed class RestorePointAttempt
{
    public RestorePointStatus Status { get; set; }
    public bool Verified { get; set; }
    public string Description { get; set; } = "";
    public uint? SequenceNumber { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CreationTime { get; set; }
    public int? ReturnCode { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>A restore point as enumerated from Windows (root\default:SystemRestore).</summary>
public sealed class SystemRestorePointInfo
{
    public uint SequenceNumber { get; set; }
    public string Description { get; set; } = "";
    public DateTime CreationTime { get; set; }
    public int RestorePointType { get; set; }
    public int EventType { get; set; }
    public bool CreatedByThisApp => Description.StartsWith(AppPaths.RestorePointPrefix, StringComparison.Ordinal);

    public string TypeDisplay => RestorePointType switch
    {
        0 => "Application install",
        1 => "Application uninstall",
        7 => "System checkpoint",
        10 => "Device driver install",
        12 => "Modify settings",
        13 => "Cancelled operation",
        _ => $"Type {RestorePointType}"
    };
}

public sealed class SystemProtectionStatus
{
    public bool Supported { get; set; }
    public bool? Enabled { get; set; }
    public bool DisabledByPolicy { get; set; }
    public int FrequencyMinutes { get; set; } = 1440;
    public bool FrequencyIsDefault { get; set; } = true;
    public SystemRestorePointInfo? LastRestorePoint { get; set; }
    public string Message { get; set; } = "";

    public DateTime? NextAllowedCreation => LastRestorePoint is null || FrequencyMinutes <= 0
        ? null
        : LastRestorePoint.CreationTime.AddMinutes(FrequencyMinutes);
}
