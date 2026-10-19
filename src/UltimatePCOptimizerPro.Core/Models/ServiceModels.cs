using System.Text.Json.Serialization;

namespace UltimatePCOptimizerPro.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServiceCategory { Essential, Recommended, Optional, HardwareDependent, ApplicationDependent, Unknown }

/// <summary>Curated knowledge about a Windows service (Data/Services/Services.json).</summary>
public sealed class ServiceCatalogEntry
{
    public string Name { get; set; } = "";
    public ServiceCategory Category { get; set; }
    public string Purpose { get; set; } = "";
    public string SideEffects { get; set; } = "";
    public ServiceStartMode? SuggestedStartMode { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.Medium;
    public string? Condition { get; set; }
}

public sealed class ServiceEntry
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string State { get; set; } = "";
    public ServiceStartMode? StartMode { get; set; }
    public string StartModeDisplay { get; set; } = "";
    public string? PathName { get; set; }
    public int ProcessId { get; set; }
    public bool IsSharedHost { get; set; }
    public ulong? WorkingSetBytes { get; set; }
    public ServiceCategory Category { get; set; } = ServiceCategory.Unknown;
    public ServiceCatalogEntry? Catalog { get; set; }
    public bool IsProtected { get; set; }
    public bool IsPerUserTemplate { get; set; }
    public string RecommendedAction { get; set; } = "Leave unchanged";
    public bool CanModify => !IsProtected && Category is ServiceCategory.Optional or ServiceCategory.HardwareDependent or ServiceCategory.ApplicationDependent;
}
