using System.Text.Json.Serialization;

namespace UltimatePCOptimizerPro.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RiskLevel { Low, Medium, High, Expert }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestartRequirement { None, AppRestart, ExplorerRestart, SignOut, Restart }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TweakCategory
{
    Gaming, Cpu, Gpu, Memory, Network, Input, Explorer, Privacy, Startup, Power, Services, Tasks, Recovery
}

/// <summary>One documented optimisation from the tweak database (Data/Tweaks/*.json).</summary>
public sealed class TweakDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public TweakCategory Category { get; set; }
    public string Description { get; set; } = "";
    /// <summary>What actually changes inside Windows and why it can have an effect.</summary>
    public string Mechanism { get; set; } = "";
    public string Benefit { get; set; } = "";
    public string Drawback { get; set; } = "";
    public string RecommendedFor { get; set; } = "";
    /// <summary>Evidence / documentation notes and known limitations.</summary>
    public string Evidence { get; set; } = "";
    public RiskLevel Risk { get; set; } = RiskLevel.Low;
    public RestartRequirement Restart { get; set; } = RestartRequirement.None;
    /// <summary>What the change mainly affects: Fps, FrameTime, Latency, Responsiveness, Privacy, Network, Power, Convenience.</summary>
    public List<string> Impacts { get; set; } = new();
    public TweakConditions Conditions { get; set; } = new();
    /// <summary>Tiers the tweak is appropriate for. Empty = all tiers.</summary>
    public List<PcTier> Tiers { get; set; } = new();
    public List<ChangeSpec> Actions { get; set; } = new();

    [JsonIgnore] public bool IsRegistryBacked => Actions.Count > 0 && Actions.All(a => a is RegistryValueSpec or RegistryTokenSpec or RegistryFlagsSpec);
    [JsonIgnore] public bool RequiresAdmin => Actions.Any(a => a switch
    {
        RegistryValueSpec r => r.Hive == "HKLM",
        RegistryTokenSpec t => t.Hive == "HKLM",
        RegistryFlagsSpec f => f.Hive == "HKLM",
        AppCompatLayerSpec => false,
        _ => true
    });
}

public sealed class TweakConditions
{
    public int MinBuild { get; set; }
    public int MaxBuild { get; set; }
    public bool DesktopOnly { get; set; }
    public bool LaptopOnly { get; set; }
    public bool RequiresDedicatedGpu { get; set; }
    public bool RequiresSsdSystemDrive { get; set; }
    public bool RequiresHybridCpu { get; set; }
    public bool RequiresActiveNetworkAdapter { get; set; }
    /// <summary>Case-insensitive substring that must match an installed program's display name.</summary>
    public string? RequiresSoftware { get; set; }
    public GpuVendor? RequiresGpuVendor { get; set; }
}

public sealed class OptimizationProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public RiskLevel MaxRisk { get; set; } = RiskLevel.Low;
    public bool RequiresExplicitOptIn { get; set; }
    public bool IncludeRefreshRateFix { get; set; } = true;
    public List<string> Tweaks { get; set; } = new();
}
