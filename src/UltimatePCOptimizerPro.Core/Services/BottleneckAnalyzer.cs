using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Turns configuration and live measurements into concrete, explained findings.</summary>
public sealed class BottleneckAnalyzer
{
    public List<Bottleneck> Analyze(SystemSnapshot s, MonitorSample? live)
    {
        var list = new List<Bottleneck>();

        foreach (var d in s.Displays.Where(d => d.RunningBelowMaxRefresh))
        {
            list.Add(new Bottleneck
            {
                Id = "display.refresh." + d.DeviceName,
                Category = "Display",
                Severity = Severity.High,
                Title = $"{d.FriendlyName} runs at {d.RefreshHz} Hz but supports {d.MaxRefreshAtCurrentResolution} Hz",
                Detail = "Frames above the refresh rate are never shown, and a low refresh rate increases display latency and motion blur.",
                Recommendation = $"Set {d.FriendlyName} to {d.MaxRefreshAtCurrentResolution} Hz (FPS Booster or Smart Optimize can do this with rollback). Verify the cable supports it (DisplayPort / HDMI 2.0+).",
                SuggestedTweakIds = { "display.refresh.max" }
            });
        }

        var totalGb = s.Memory.TotalGB;
        if (totalGb > 0 && totalGb < 7.5)
            list.Add(new Bottleneck
            {
                Id = "ram.capacity", Category = "Memory", Severity = Severity.High,
                Title = $"Only {totalGb:0.#} GB of RAM installed",
                Detail = "Modern games commonly need 8–16 GB. Under memory pressure Windows pages to disk, which causes severe stutter.",
                Recommendation = "Close background applications before gaming; upgrading to at least 16 GB (two matched modules) is the most effective fix."
            });
        else if (totalGb > 0 && totalGb < 15)
            list.Add(new Bottleneck
            {
                Id = "ram.capacity.mid", Category = "Memory", Severity = Severity.Medium,
                Title = $"{totalGb:0.#} GB of RAM",
                Detail = "Adequate for many games but browsers, Discord and launchers running alongside can push the system into paging.",
                Recommendation = "Trim startup and background applications; 16–32 GB is recommended for current titles."
            });

        if (s.Memory.Modules.Count == 1 && !(s.Memory.PrimaryType?.StartsWith("LP") ?? false))
            list.Add(new Bottleneck
            {
                Id = "ram.singlechannel", Category = "Memory", Severity = Severity.Medium,
                Title = "Single memory module detected (likely single-channel)",
                Detail = "Single-channel memory halves bandwidth, which hurts CPU-bound games and especially integrated graphics.",
                Recommendation = "Install a matching second module in the slot recommended by the motherboard manual. (Hardware change — the app cannot fix this.)"
            });

        if (live is not null && live.RamUsedPercent > 85)
            list.Add(new Bottleneck
            {
                Id = "ram.pressure", Category = "Memory", Severity = Severity.High, IsMeasured = true,
                Title = $"Memory usage is {live.RamUsedPercent:0}% right now",
                Detail = "High memory usage at the desktop leaves little room for games and increases paging.",
                Recommendation = "Review memory-heavy processes on the RAM page and disable unneeded startup applications.",
                SuggestedTweakIds = { "mem.edge.startupboost.disable", "mem.edge.backgroundmode.disable", "mem.chrome.backgroundmode.disable" }
            });

        // Pages Input/sec also counts reads of memory-mapped files (e.g. apps loading), so only flag it together with high RAM use.
        if (live?.PagesInputPerSec is > 500 && live.RamUsedPercent > 80)
            list.Add(new Bottleneck
            {
                Id = "ram.paging", Category = "Memory", Severity = Severity.Medium, IsMeasured = true,
                Title = $"Hard page faults: {live.PagesInputPerSec:0} pages/s with {live.RamUsedPercent:0}% RAM in use",
                Detail = "Pages are being read from disk to resolve page faults while memory is nearly full. This includes page-file reads and memory-mapped files; under memory pressure it usually means working sets are being trimmed.",
                Recommendation = "Reduce concurrent applications or add RAM. Re-check while idle — a single reading during app launches is normal."
            });

        var sys = s.Storage.SystemDisk;
        if (sys?.MediaKind == StorageMediaKind.Hdd)
            list.Add(new Bottleneck
            {
                Id = "storage.hdd", Category = "Storage", Severity = Severity.High,
                Title = "Windows is installed on a hard disk drive",
                Detail = "Random-access latency of HDDs causes long load times and texture-streaming stutter.",
                Recommendation = "Move Windows and games to an SSD. Avoid removing the startup delay on HDD systems."
            });
        var sysVol = s.Storage.Volumes.FirstOrDefault(v => v.IsSystemVolume);
        if (sysVol is not null && sysVol.FreePercent < 10)
            list.Add(new Bottleneck
            {
                Id = "storage.free", Category = "Storage", Severity = sysVol.FreePercent < 5 ? Severity.High : Severity.Medium,
                Title = $"System drive has only {sysVol.FreePercent:0}% free ({sysVol.FreeBytes / 1024d / 1024 / 1024:0.#} GB)",
                Detail = "Low free space limits the page file, Windows Update and shader caches, and reduces SSD write performance.",
                Recommendation = "Use Temporary File Cleanup on the Debloater page, or Windows Storage Sense."
            });
        if (s.Storage.TrimEnabled == false && s.Storage.Disks.Any(d => d.MediaKind == StorageMediaKind.Ssd))
            list.Add(new Bottleneck
            {
                Id = "storage.trim", Category = "Storage", Severity = Severity.Medium,
                Title = "TRIM is disabled while SSDs are present",
                Detail = "Without TRIM, SSD write performance degrades over time.",
                Recommendation = "Run 'fsutil behavior set DisableDeleteNotify 0' in an elevated terminal (not changed automatically by this app)."
            });

        if (live?.CpuPerformanceLimitPercent is < 95 and var limit)
            list.Add(new Bottleneck
            {
                Id = "cpu.limit", Category = "CPU", Severity = limit < 80 ? Severity.High : Severity.Medium, IsMeasured = true,
                Title = $"CPU performance limited to {limit:0}% of nominal",
                Detail = "Windows reports an active performance limit (thermal, power, current or platform limit). Sustained limits lower clocks and FPS.",
                Recommendation = s.IsLaptop
                    ? "Plug in the charger, select the OEM performance mode, and check cooling (vents, fans, dust)."
                    : "Check CPU cooler mounting, case airflow and motherboard power limits in firmware."
            });

        if (live is not null && live.CpuPercent > 25)
            list.Add(new Bottleneck
            {
                Id = "cpu.background", Category = "CPU", Severity = live.CpuPercent > 50 ? Severity.High : Severity.Medium, IsMeasured = true,
                Title = $"Background CPU usage is {live.CpuPercent:0}% at the time of the scan",
                Detail = "CPU time used by background work is unavailable to the game and can cause frame-time spikes.",
                Recommendation = "Inspect the Background Processes tab on the Debloater page. Note: this reading may include the scan itself."
            });

        if (s.Power.ActivePlanName.Contains("saver", StringComparison.OrdinalIgnoreCase))
            list.Add(new Bottleneck
            {
                Id = "power.saver", Category = "Power", Severity = Severity.High,
                Title = "Power saver plan is active",
                Detail = "Power saver caps processor performance states.",
                Recommendation = "Switch to Balanced or High performance while gaming.",
                SuggestedTweakIds = { "cpu.powerplan.highperformance" }
            });
        if (s.IsLaptop && !s.Power.OnAcPower)
            list.Add(new Bottleneck
            {
                Id = "power.battery", Category = "Power", Severity = Severity.High, IsMeasured = true,
                Title = "Laptop is running on battery",
                Detail = "Most gaming laptops reduce CPU and GPU power limits substantially on battery.",
                Recommendation = "Plug in the charger for gaming. Battery-focused recommendations are shown separately."
            });

        if (s.Os.BackgroundRecordingEnabled == true)
            list.Add(new Bottleneck
            {
                Id = "gaming.backgroundrecording", Category = "Gaming", Severity = Severity.Medium,
                Title = "Background game recording ('Record what happened') is on",
                Detail = "Continuous recording uses the GPU video encoder and disk writes during play.",
                Recommendation = "Disable it unless you use instant replay.",
                SuggestedTweakIds = { "gaming.backgroundrecording.disable" }
            });
        if (s.Os.GameModeEnabled == false)
            list.Add(new Bottleneck
            {
                Id = "gaming.gamemode", Category = "Gaming", Severity = Severity.Low,
                Title = "Game Mode is off",
                Detail = "Game Mode prevents Windows Update driver installs/restart notifications and prioritises the foreground game.",
                Recommendation = "Turn Game Mode on.",
                SuggestedTweakIds = { "gaming.gamemode.enable" }
            });

        var net = s.Network.Active;
        if (net is { IsWireless: true })
            list.Add(new Bottleneck
            {
                Id = "network.wifi", Category = "Network", Severity = net.WifiSignalPercent is < 60 ? Severity.High : Severity.Low,
                Title = net.WifiSignalPercent is { } sig ? $"Gaming over Wi-Fi (signal {sig}%)" : "Gaming over Wi-Fi",
                Detail = "Wi-Fi adds latency variation (jitter) and is sensitive to interference; registry tweaks cannot change radio conditions.",
                Recommendation = "Use Ethernet where possible, or 5/6 GHz close to the router. Run the jitter test on the Network page.",
                SuggestedTweakIds = { "net.adapter.powersaving.disable" }
            });

        if (s.Os.MemoryIntegrityRunning == true)
            list.Add(new Bottleneck
            {
                Id = "security.hvci", Category = "Security (information)", Severity = Severity.Info,
                Title = "Memory Integrity (HVCI) is running",
                Detail = "Microsoft acknowledges a small gaming performance cost on some systems. It is an important protection against kernel exploits.",
                Recommendation = "This app does not disable security features. Leave it on unless you have a specific, informed reason."
            });

        foreach (var sw in s.RelevantSoftware.Where(x => x.Category is "Recording" or "Overlay"))
            list.Add(new Bottleneck
            {
                Id = "software." + sw.Name, Category = "Software", Severity = Severity.Info,
                Title = $"{sw.Name} installed",
                Detail = sw.Note,
                Recommendation = "Disable its overlay/background recording in the application's own settings if unused."
            });

        return list.OrderByDescending(b => b.Severity).ToList();
    }
}
