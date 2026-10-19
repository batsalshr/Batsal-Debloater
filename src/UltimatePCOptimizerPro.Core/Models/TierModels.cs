namespace UltimatePCOptimizerPro.Core.Models;

public enum PcTier { LowEnd, MidRange, HighEnd }

public enum Severity { Info, Low, Medium, High }

public sealed class ComponentScore
{
    public string Component { get; set; } = "";
    public double Score { get; set; }
    public double Weight { get; set; }
    public string Basis { get; set; } = "";
}

public sealed class Bottleneck
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public Severity Severity { get; set; }
    public List<string> SuggestedTweakIds { get; set; } = new();
    /// <summary>True when the finding is based on a live measurement rather than configuration.</summary>
    public bool IsMeasured { get; set; }
}

public sealed class TierResult
{
    public PcTier Tier { get; set; }
    public PcTier ComputedTier { get; set; }
    public bool IsOverridden { get; set; }
    public bool IsLaptop { get; set; }
    public double OverallScore { get; set; }
    public List<ComponentScore> Scores { get; set; } = new();
    public List<string> Reasons { get; set; } = new();
    public List<string> Caps { get; set; } = new();
    public List<Bottleneck> Bottlenecks { get; set; } = new();
    public string LikelyLimiter { get; set; } = "Unknown";
    public List<string> Priorities { get; set; } = new();
    public List<string> Tradeoffs { get; set; } = new();

    public string TierDisplay => Tier switch
    {
        PcTier.LowEnd => "Low-End",
        PcTier.MidRange => "Mid-Range",
        _ => "High-End"
    } + (IsLaptop ? " Laptop" : " Desktop");
}

/// <summary>Optional workload context supplied by the user on the tier analysis page.</summary>
public sealed class TargetWorkload
{
    public int ResolutionHeight { get; set; } = 1080;
    public int TargetFps { get; set; } = 144;
    public string? Game { get; set; }
}
