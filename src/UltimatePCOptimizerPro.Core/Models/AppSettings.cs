using System.Text.Json.Serialization;

namespace UltimatePCOptimizerPro.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestorePointPolicy
{
    /// <summary>Default. Never modify the system unless a new restore point was verified.</summary>
    RequireVerifiedRestorePoint,
    /// <summary>Allow an explicit, per-operation override when every change has a verified app-level rollback.</summary>
    AllowBackupOnlyOverride
}

/// <summary>Persisted application settings. Defaults are the safest configuration.</summary>
public sealed class AppSettings
{
    public bool ScanOnStartup { get; set; } = true;
    public int DashboardRefreshMs { get; set; } = 1500;
    public int HardwareRescanMinutes { get; set; }
    public string AccentColor { get; set; } = "#00B4FF";
    public bool UsePurpleAccents { get; set; } = true;
    public RestorePointPolicy RestorePointPolicy { get; set; } = RestorePointPolicy.RequireVerifiedRestorePoint;
    public bool AutoRollbackOnFailure { get; set; } = true;
    public bool ExportRegistryKeysWithBackups { get; set; } = true;
    public string DefaultProfile { get; set; } = "safe";
    public bool ConfirmLowRiskChanges { get; set; } = true;
    public bool DetailedLogging { get; set; }
    public int BenchmarkHistoryLimit { get; set; } = 100;
    public bool NotificationsEnabled { get; set; } = true;
    public PcTier? TierOverride { get; set; }
    public string? PresentMonPath { get; set; }
    public string PingTarget { get; set; } = "1.1.1.1";
    public string BufferbloatDownloadUrl { get; set; } = "https://speed.cloudflare.com/__down?bytes=250000000";
    public List<string> FavoriteTweaks { get; set; } = new();
}
