using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

public sealed class PlanItem
{
    public string Id { get; set; } = "";
    public string? TweakId { get; set; }
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Benefit { get; set; } = "";
    public string Drawback { get; set; } = "";
    public RiskLevel Risk { get; set; }
    public RestartRequirement Restart { get; set; }
    public bool Selected { get; set; } = true;
    public List<PlannedChange> Changes { get; set; } = new();
}

public sealed record ExcludedItem(string Title, string Reason);

public sealed class OptimizationPlan
{
    public OptimizationProfile Profile { get; set; } = new();
    public TierResult Tier { get; set; } = new();
    public List<PlanItem> Items { get; set; } = new();
    public List<ExcludedItem> Excluded { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

/// <summary>Builds a hardware-specific plan: profile membership ∩ applicability ∩ tier suitability ∩ not-already-applied.</summary>
public sealed class RecommendationEngine
{
    private readonly TweakCatalogService _catalog;
    private readonly TweakStatusService _status;

    public RecommendationEngine(TweakCatalogService catalog, TweakStatusService status)
    {
        _catalog = catalog;
        _status = status;
    }

    public OptimizationPlan BuildPlan(OptimizationProfile profile, SystemSnapshot snapshot, TierResult tier)
    {
        var plan = new OptimizationPlan { Profile = profile, Tier = tier };
        bool onBattery = snapshot.IsLaptop && !snapshot.Power.OnAcPower;
        if (onBattery) plan.Notes.Add("The laptop is on battery: power-plan and power-management changes are excluded. Plug in and regenerate the plan for plugged-in gaming settings.");
        if (snapshot.IsLaptop) plan.Notes.Add("Laptop detected: desktop-only tweaks are excluded; use the manufacturer's performance mode for CPU/GPU power limits.");

        foreach (var id in profile.Tweaks)
        {
            var t = _catalog.Get(id);
            if (t is null) continue;
            if (t.Risk > profile.MaxRisk) { plan.Excluded.Add(new(t.Name, $"Risk {t.Risk} exceeds this profile's limit ({profile.MaxRisk}).")); continue; }
            if (t.Tiers.Count > 0 && !t.Tiers.Contains(tier.Tier))
            {
                plan.Excluded.Add(new(t.Name, $"Not suitable for {tier.TierDisplay} systems ({string.Join("/", t.Tiers)} only)."));
                continue;
            }
            if (onBattery && t.Category == TweakCategory.Power) { plan.Excluded.Add(new(t.Name, "Excluded while on battery.")); continue; }
            var st = _status.Evaluate(t, snapshot);
            if (st.State == TweakState.Unsupported) { plan.Excluded.Add(new(t.Name, st.Message)); continue; }
            if (st.State == TweakState.Applied) { plan.Excluded.Add(new(t.Name, "Already applied.")); continue; }
            var changes = TweakResolver.ToPlannedChanges(t, snapshot, out var err);
            if (changes is null) { plan.Excluded.Add(new(t.Name, err ?? "Could not be resolved.")); continue; }
            plan.Items.Add(new PlanItem
            {
                Id = t.Id,
                TweakId = t.Id,
                Title = t.Name,
                Category = t.Category.ToString(),
                Reason = BuildReason(t, tier),
                Benefit = t.Benefit,
                Drawback = t.Drawback,
                Risk = t.Risk,
                Restart = t.Restart,
                Changes = changes
            });
        }

        if (profile.IncludeRefreshRateFix)
        {
            foreach (var d in snapshot.Displays.Where(d => d.RunningBelowMaxRefresh))
            {
                plan.Items.Insert(0, new PlanItem
                {
                    Id = "display.refresh.max:" + d.DeviceName,
                    Title = $"Set {d.FriendlyName} to {d.MaxRefreshAtCurrentResolution} Hz",
                    Category = "Display",
                    Reason = $"Detected {d.RefreshHz} Hz while the display and driver report {d.MaxRefreshAtCurrentResolution} Hz at {d.Width}x{d.Height}.",
                    Benefit = "Shows more of the frames the GPU renders, lowers display latency and motion blur.",
                    Drawback = "Slightly higher GPU idle power on some systems. The app asks you to confirm the new mode and reverts automatically after 15 seconds if you don't (e.g. if the screen goes blank).",
                    Risk = RiskLevel.Low,
                    Restart = RestartRequirement.None,
                    Changes =
                    {
                        new PlannedChange
                        {
                            Title = $"Refresh rate {d.FriendlyName}", TweakId = "display.refresh.max", Category = "Display", Risk = RiskLevel.Low,
                            Spec = new DisplayModeSpec { DeviceName = d.DeviceName, Width = d.Width, Height = d.Height, RefreshHz = d.MaxRefreshAtCurrentResolution }
                        }
                    }
                });
            }
        }
        return plan;
    }

    private static string BuildReason(TweakDefinition t, TierResult tier)
    {
        var why = tier.Tier switch
        {
            PcTier.LowEnd when t.Impacts.Contains("Responsiveness") || t.Category is TweakCategory.Memory or TweakCategory.Explorer =>
                "Frees CPU/RAM/GPU time on constrained hardware.",
            PcTier.HighEnd when t.Impacts.Contains("FrameTime") || t.Impacts.Contains("Latency") =>
                "Targets frame-time consistency and latency, which matter most on high-refresh systems.",
            _ => t.RecommendedFor
        };
        return $"{why} Impacts: {string.Join(", ", t.Impacts)}.";
    }
}
