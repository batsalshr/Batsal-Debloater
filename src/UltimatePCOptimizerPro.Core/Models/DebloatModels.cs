using System.Text.Json.Serialization;

namespace UltimatePCOptimizerPro.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DebloatPreset { Light, Gaming, Advanced }

/// <summary>Curated knowledge about a known Appx/MSIX package (Data/Debloat/Apps.json).</summary>
public sealed class AppCatalogEntry
{
    public string Match { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "";
    public RiskLevel Risk { get; set; } = RiskLevel.Low;
    public string Notes { get; set; } = "";
    public List<DebloatPreset> Presets { get; set; } = new();
    public string? StoreId { get; set; }
}

public sealed class InstalledApp
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public string? InstallLocation { get; set; }
    public DateTimeOffset? InstalledDate { get; set; }
    public string SignatureKind { get; set; } = "";
    public bool IsProtected { get; set; }
    public string ProtectionReason { get; set; } = "";
    public AppCatalogEntry? Catalog { get; set; }
    public string Category => Catalog?.Category ?? (IsProtected ? "Protected" : "Installed / unknown");
}

public sealed class StartupEntry
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public StartupLocation Location { get; set; }
    public string LocationDisplay { get; set; } = "";
    public bool Enabled { get; set; }
    public string? ExecutablePath { get; set; }
    public bool ExecutableExists { get; set; }
    public string? Publisher { get; set; }
    public string? Description { get; set; }
    public bool RequiresAdmin => Location is StartupLocation.LocalMachineRun or StartupLocation.LocalMachineRun32 or StartupLocation.CommonStartupFolder;
    public string Impact { get; set; } = "Not measurable";
}

public sealed class LogonTaskEntry
{
    public string TaskPath { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public string? Action { get; set; }
}

public sealed class ScheduledTaskCatalogEntry
{
    public string TaskPath { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string Effect { get; set; } = "";
    public RiskLevel Risk { get; set; } = RiskLevel.Low;
}

public sealed class ScheduledTaskState
{
    public ScheduledTaskCatalogEntry Entry { get; set; } = null!;
    public bool Exists { get; set; }
    public bool? Enabled { get; set; }
    public string? LastRun { get; set; }
}

public sealed class OptionalFeatureCatalogEntry
{
    public string FeatureName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public bool RecommendDisable { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.Low;
}

public sealed class OptionalFeatureState
{
    public string FeatureName { get; set; } = "";
    public string Caption { get; set; } = "";
    /// <summary>1 Enabled, 2 Disabled, 3 Absent, 4 Unknown (Win32_OptionalFeature.InstallState).</summary>
    public int InstallState { get; set; }
    public OptionalFeatureCatalogEntry? Catalog { get; set; }
    public string StateDisplay => InstallState switch { 1 => "Enabled", 2 => "Disabled", 3 => "Absent", _ => "Unknown" };
}

public sealed class CleanupCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Warning { get; set; } = "";
    public bool SelectedByDefault { get; set; }
    public bool RequiresAdmin { get; set; }
    public List<string> Roots { get; set; } = new();
    public List<CleanupFile> Files { get; set; } = new();
    public long TotalBytes => Files.Sum(f => f.SizeBytes);
    public int SkippedRecent { get; set; }
    public int InaccessibleCount { get; set; }
}

public sealed record CleanupFile(string Path, long SizeBytes, DateTime LastWriteTime);

public sealed class CleanupResult
{
    public int Deleted { get; set; }
    public long BytesFreed { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}
