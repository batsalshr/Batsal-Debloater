using System.Text.RegularExpressions;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>
/// Weighted, explainable hardware classification. Scores are heuristics for choosing optimisation priorities,
/// not performance predictions; every contributing factor is reported back to the user.
/// </summary>
public sealed class HardwareTierService
{
    public const double CpuWeight = 0.35, GpuWeight = 0.40, RamWeight = 0.15, StorageWeight = 0.10;
    public const double LowThreshold = 40, HighThreshold = 68;

    public TierResult Classify(SystemSnapshot s, TargetWorkload? workload = null, PcTier? overrideTier = null, MonitorSample? live = null)
    {
        var r = new TierResult { IsLaptop = s.IsLaptop };
        var cpu = ScoreCpu(s.Cpu, out var cpuBasis);
        var gpu = ScoreGpu(s, out var gpuBasis);
        var ram = ScoreRam(s.Memory, out var ramBasis);
        var storage = ScoreStorage(s.Storage, out var storageBasis);
        r.Scores = new()
        {
            new ComponentScore { Component = "CPU", Score = cpu, Weight = CpuWeight, Basis = cpuBasis },
            new ComponentScore { Component = "GPU", Score = gpu, Weight = GpuWeight, Basis = gpuBasis },
            new ComponentScore { Component = "Memory", Score = ram, Weight = RamWeight, Basis = ramBasis },
            new ComponentScore { Component = "Storage", Score = storage, Weight = StorageWeight, Basis = storageBasis },
        };
        r.OverallScore = Math.Round(cpu * CpuWeight + gpu * GpuWeight + ram * RamWeight + storage * StorageWeight, 1);

        var tier = r.OverallScore < LowThreshold ? PcTier.LowEnd : r.OverallScore < HighThreshold ? PcTier.MidRange : PcTier.HighEnd;
        r.Reasons.Add($"Weighted score {r.OverallScore:0.#}/100 (CPU {CpuWeight:P0}, GPU {GpuWeight:P0}, RAM {RamWeight:P0}, storage {StorageWeight:P0}). Thresholds: <{LowThreshold} low-end, <{HighThreshold} mid-range.");

        // Hard constraints that dominate gaming experience regardless of the weighted score.
        var totalGb = s.Memory.TotalGB;
        if (totalGb > 0 && totalGb < 7.5 && tier > PcTier.LowEnd)
        {
            tier = PcTier.LowEnd;
            r.Caps.Add($"Capped to Low-End: {totalGb:0.#} GB RAM is below the 8 GB most current games require.");
        }
        if (!s.HasDedicatedGpu && tier == PcTier.HighEnd)
        {
            tier = PcTier.MidRange;
            r.Caps.Add("Capped to Mid-Range: no dedicated GPU detected (integrated graphics share system memory bandwidth).");
        }
        if (s.Storage.SystemDisk?.MediaKind == StorageMediaKind.Hdd && tier == PcTier.HighEnd)
        {
            tier = PcTier.MidRange;
            r.Caps.Add("Capped to Mid-Range: Windows is installed on a hard disk drive, which causes asset-streaming stutter.");
        }
        r.ComputedTier = tier;
        r.Tier = overrideTier ?? tier;
        r.IsOverridden = overrideTier is not null && overrideTier != tier;
        if (r.IsOverridden) r.Reasons.Add($"User override active: classification set to {r.Tier} (computed {tier}).");

        r.LikelyLimiter = EstimateLimiter(cpu, gpu, workload ?? new TargetWorkload { TargetFps = s.PrimaryDisplay?.MaxRefreshAtCurrentResolution ?? 60, ResolutionHeight = s.PrimaryDisplay?.Height ?? 1080 });
        r.Bottlenecks = new BottleneckAnalyzer().Analyze(s, live);
        FillPriorities(r);
        return r;
    }

    private static string EstimateLimiter(double cpu, double gpu, TargetWorkload w)
    {
        // Higher resolutions shift load to the GPU; high frame-rate targets shift it to the CPU.
        double gpuDemand = w.ResolutionHeight >= 2160 ? 1.45 : w.ResolutionHeight >= 1440 ? 1.2 : 1.0;
        double cpuDemand = w.TargetFps >= 240 ? 1.35 : w.TargetFps >= 144 ? 1.15 : 1.0;
        double gpuHeadroom = gpu / gpuDemand, cpuHeadroom = cpu / cpuDemand;
        var label = $"at {w.ResolutionHeight}p / {w.TargetFps} FPS target";
        if (Math.Abs(gpuHeadroom - cpuHeadroom) < 8) return $"Balanced {label} (estimate — confirm with a benchmark: GPU usage near 95-100% means GPU-bound)";
        return gpuHeadroom < cpuHeadroom
            ? $"Likely GPU-bound {label} (estimate — lowering resolution/graphics presets helps most)"
            : $"Likely CPU-bound {label} (estimate — background load, frame caps and CPU-heavy settings matter most)";
    }

    private static void FillPriorities(TierResult r)
    {
        switch (r.Tier)
        {
            case PcTier.LowEnd:
                r.Priorities.AddRange(new[]
                {
                    "Reduce background CPU and RAM usage (startup apps, background apps, Edge/Chrome background modes)",
                    "Disable background game recording and overlay capture",
                    "Lower in-game presets and resolution scale before touching system tweaks",
                    "Reduce visual effects and transparency to free GPU/compositor time",
                    "Check storage free space and thermal throttling"
                });
                r.Tradeoffs.AddRange(new[] { "Fewer background conveniences (widgets, background apps)", "Plainer desktop visuals", "Lower in-game visual quality" });
                break;
            case PcTier.MidRange:
                r.Priorities.AddRange(new[]
                {
                    "Stable frame pacing: Game Mode, windowed-game optimisations, frame caps",
                    "Remove avoidable background interference (recording, auto-installed apps, telemetry tasks)",
                    "Correct power plan and refresh rate",
                    "Balance CPU/GPU load with per-game settings"
                });
                r.Tradeoffs.AddRange(new[] { "Minor loss of background features", "Slightly higher idle power on desktop power plans" });
                break;
            default:
                r.Priorities.AddRange(new[]
                {
                    "Frame-time consistency and latency (refresh rate, VRR, frame limiter, HAGS where supported)",
                    "Keep CPU scheduling features and memory protections intact",
                    "Avoid background interference during play (capture, scheduled maintenance popups)",
                    "Watch power/thermal limits under sustained load"
                });
                r.Tradeoffs.AddRange(new[] { "Higher idle power on performance power plans", "Little benefit from RAM-saving debloat on large-memory systems" });
                break;
        }
        if (r.IsLaptop)
        {
            r.Priorities.Add("Laptop: game plugged in, use the OEM performance mode, and keep separate battery settings");
            r.Tradeoffs.Add("Laptop: performance settings increase heat, fan noise and battery drain");
        }
    }

    // ---------------------------------------------------------------------------------------- CPU

    internal static double ScoreCpu(CpuInfo cpu, out string basis)
    {
        var notes = new List<string>();
        int threads = Math.Max(1, cpu.LogicalProcessors);
        double threadScore = Math.Clamp(Math.Log2(threads) / Math.Log2(32), 0, 1) * 35;
        notes.Add($"{cpu.PhysicalCores}C/{threads}T → {threadScore:0}");

        double gen = 0;
        var name = cpu.Name;
        var intel = Regex.Match(name, @"\bi([3579])-(\d{4,5})([A-Z]{0,3})\b", RegexOptions.IgnoreCase);
        var ultra = Regex.Match(name, @"Ultra\s+([3579])\s+(\d)\d{2}([A-Z]{0,2})", RegexOptions.IgnoreCase);
        var ryzen = Regex.Match(name, @"Ryzen\s+(?:Threadripper\s+)?([3579])\s+(?:PRO\s+)?(\d)(\d{3})([A-Z0-9]{0,4})", RegexOptions.IgnoreCase);
        int classLevel = 0;
        string suffix = "";
        if (intel.Success)
        {
            var d = intel.Groups[2].Value;
            int g = d.Length == 5 ? int.Parse(d[..2]) : int.Parse(d[..1]);
            gen = g >= 12 ? 40 : g >= 10 ? 30 : g >= 8 ? 22 : g >= 6 ? 14 : 6;
            classLevel = int.Parse(intel.Groups[1].Value);
            suffix = intel.Groups[3].Value.ToUpperInvariant();
            notes.Add($"Intel gen {g} → {gen}");
        }
        else if (ultra.Success)
        {
            gen = 38;
            classLevel = int.Parse(ultra.Groups[1].Value);
            suffix = ultra.Groups[3].Value.ToUpperInvariant();
            notes.Add($"Core Ultra → {gen}");
        }
        else if (ryzen.Success)
        {
            int series = int.Parse(ryzen.Groups[2].Value);
            int model = int.Parse(ryzen.Groups[3].Value);
            suffix = ryzen.Groups[4].Value.ToUpperInvariant();
            bool mobile = suffix.Contains('U') || suffix.Contains('H');
            // Mobile parts reuse series numbers with older architectures; use the third digit as a rough architecture hint.
            gen = series >= 7 ? 42 : series >= 5 ? 36 : series >= 3 ? 26 : 16;
            if (mobile && series >= 7 && model % 1000 / 100 < 4) gen -= 8;
            if (suffix.Contains("X3D")) { gen += 5; notes.Add("X3D cache +5"); }
            classLevel = int.Parse(ryzen.Groups[1].Value);
            notes.Add($"Ryzen {series}000 → {gen}");
        }
        else
        {
            gen = cpu.InstructionSets.Contains("AVX-512F") || cpu.InstructionSets.Contains("AVX-VNNI") ? 34 : cpu.InstructionSets.Contains("AVX2") ? 22 : 10;
            notes.Add($"Unrecognised model, ISA-based → {gen}");
            if (Regex.IsMatch(name, "Celeron|Pentium|Athlon|Atom|N[1-9]\\d{2}\\b", RegexOptions.IgnoreCase)) { gen = Math.Min(gen, 10); classLevel = 1; }
        }

        double classScore = classLevel switch { 9 => 12, 7 => 9, 5 => 6, 3 => 2, _ => 0 };
        double mobilePenalty = 0;
        if (suffix.EndsWith('U') || suffix.Contains("U")) mobilePenalty = 6;
        if (suffix.Contains('Y')) mobilePenalty = 10;
        double score = Math.Clamp(threadScore + gen + classScore - mobilePenalty, 0, 100);
        if (classScore > 0) notes.Add($"class {classLevel} +{classScore}");
        if (mobilePenalty > 0) notes.Add($"low-power suffix −{mobilePenalty}");
        basis = string.Join(", ", notes);
        return Math.Round(score, 1);
    }

    // ---------------------------------------------------------------------------------------- GPU

    private static readonly (string Pattern, double Score)[] GpuTable =
    {
        // NVIDIA
        (@"RTX\s*5090", 100), (@"RTX\s*5080", 95), (@"RTX\s*5070\s*Ti", 88), (@"RTX\s*5070", 82), (@"RTX\s*5060\s*Ti", 72), (@"RTX\s*5060", 68), (@"RTX\s*5050", 56),
        (@"RTX\s*4090", 98), (@"RTX\s*4080", 92), (@"RTX\s*4070\s*Ti", 85), (@"RTX\s*4070", 78), (@"RTX\s*4060\s*Ti", 68), (@"RTX\s*4060", 62), (@"RTX\s*4050", 52),
        (@"RTX\s*3090", 85), (@"RTX\s*3080", 80), (@"RTX\s*3070", 72), (@"RTX\s*3060\s*Ti", 66), (@"RTX\s*3060", 58), (@"RTX\s*3050", 45),
        (@"RTX\s*2080\s*Ti", 70), (@"RTX\s*2080", 65), (@"RTX\s*2070", 60), (@"RTX\s*2060", 52),
        (@"GTX\s*1660\s*(Ti|SUPER)", 44), (@"GTX\s*1660", 40), (@"GTX\s*1650\s*SUPER", 34), (@"GTX\s*1650", 30), (@"GTX\s*1630", 18),
        (@"GTX\s*1080\s*Ti", 58), (@"GTX\s*1080", 50), (@"GTX\s*1070\s*Ti", 46), (@"GTX\s*1070", 44), (@"GTX\s*1060", 34), (@"GTX\s*1050\s*Ti", 24), (@"GTX\s*1050", 20), (@"GT\s*1030", 10),
        (@"GTX\s*980\s*Ti", 40), (@"GTX\s*980", 32), (@"GTX\s*970", 28), (@"GTX\s*960", 20), (@"GTX\s*950", 15), (@"MX\s*\d{3}", 12),
        // AMD
        (@"RX\s*9070\s*XT", 88), (@"RX\s*9070", 82), (@"RX\s*9060\s*XT", 66),
        (@"RX\s*7900\s*XTX", 92), (@"RX\s*7900\s*XT", 86), (@"RX\s*7900\s*GRE", 80), (@"RX\s*7800\s*XT", 76), (@"RX\s*7700\s*XT", 70), (@"RX\s*7600\s*XT", 60), (@"RX\s*7600", 58),
        (@"RX\s*6950\s*XT", 82), (@"RX\s*6900\s*XT", 80), (@"RX\s*6800\s*XT", 76), (@"RX\s*6800", 70), (@"RX\s*6750\s*XT", 64), (@"RX\s*6700\s*XT", 62), (@"RX\s*6700", 58),
        (@"RX\s*6650\s*XT", 54), (@"RX\s*6600\s*XT", 52), (@"RX\s*6600", 48), (@"RX\s*6500\s*XT", 26), (@"RX\s*6400", 20),
        (@"RX\s*5700\s*XT", 52), (@"RX\s*5700", 48), (@"RX\s*5600\s*XT", 44), (@"RX\s*5500\s*XT", 32), (@"RX\s*590", 30), (@"RX\s*580", 28), (@"RX\s*570", 24),
        (@"Vega\s*64", 40), (@"Vega\s*56", 36),
        (@"Radeon\s*890M", 30), (@"Radeon\s*880M", 28), (@"Radeon\s*8060S", 60), (@"Radeon\s*780M", 26), (@"Radeon\s*760M", 22), (@"Radeon\s*680M", 22), (@"Radeon\s*660M", 16),
        (@"Vega\s*\d+\s*Graphics", 8), (@"Radeon\(TM\)\s*Graphics", 10), (@"Radeon\s*Graphics", 10),
        // Intel
        (@"Arc.*B580", 62), (@"Arc.*B570", 56), (@"Arc.*A770", 56), (@"Arc.*A750", 52), (@"Arc.*A580", 44), (@"Arc.*A380", 22), (@"Arc.*A310", 14),
        (@"Arc.*140V", 24), (@"Arc.*130V", 20), (@"Arc\(TM\)\s*Graphics", 20), (@"Iris", 12), (@"UHD", 6), (@"HD\s*Graphics", 4),
    };

    internal static double ScoreGpu(SystemSnapshot s, out string basis)
    {
        var gpu = s.PrimaryGpu;
        if (gpu is null) { basis = "No GPU detected"; return 5; }
        foreach (var (pattern, score) in GpuTable)
        {
            if (Regex.IsMatch(gpu.Name, pattern, RegexOptions.IgnoreCase))
            {
                double adjusted = score;
                var notes = $"{gpu.Name} matched model table → {score}";
                if (gpu.Name.Contains("Laptop", StringComparison.OrdinalIgnoreCase) || (s.IsLaptop && gpu.IsDedicated))
                {
                    adjusted = Math.Round(score * 0.8, 1);
                    notes += " × 0.8 (mobile variant, power limited)";
                }
                basis = notes;
                return adjusted;
            }
        }
        double gb = gpu.DedicatedVideoMemoryBytes / 1024d / 1024 / 1024;
        double vramScore = !gpu.IsDedicated ? 8 : gb >= 16 ? 80 : gb >= 12 ? 70 : gb >= 8 ? 55 : gb >= 6 ? 45 : gb >= 4 ? 30 : gb >= 2 ? 18 : 10;
        basis = $"{gpu.Name} not in model table; estimated from {(gpu.IsDedicated ? $"{gb:0.#} GB dedicated VRAM" : "integrated graphics")} → {vramScore}";
        return vramScore;
    }

    // ---------------------------------------------------------------------------------------- RAM / storage

    internal static double ScoreRam(MemoryInfo mem, out string basis)
    {
        double gb = mem.TotalGB;
        double score = gb < 6 ? 10 : gb < 7.5 ? 20 : gb < 11 ? 35 : gb < 15 ? 45 : gb < 23 ? 65 : gb < 31 ? 72 : gb < 63 ? 85 : 90;
        var notes = new List<string> { $"{gb:0.#} GB → {score}" };
        if (mem.PrimaryType == "DDR5" || mem.PrimaryType == "LPDDR5") { score += 8; notes.Add($"{mem.PrimaryType} +8"); }
        else if (mem.PrimaryType == "DDR4") { score += 3; notes.Add("DDR4 +3"); }
        if (mem.Modules.Count == 1 && !(mem.PrimaryType?.StartsWith("LP") ?? false)) { score -= 10; notes.Add("single module (likely single-channel) −10"); }
        basis = string.Join(", ", notes);
        return Math.Clamp(score, 0, 100);
    }

    internal static double ScoreStorage(StorageInfo st, out string basis)
    {
        var d = st.SystemDisk;
        if (d is null) { basis = "System disk not identified → 60"; return 60; }
        double score = d.MediaKind switch
        {
            StorageMediaKind.Ssd when d.IsNvme => 100,
            StorageMediaKind.Ssd => 80,
            StorageMediaKind.Hdd => 25,
            _ when d.IsNvme => 95,
            _ => 60
        };
        basis = $"System disk {d.Name}: {d.KindDisplay} → {score}";
        return score;
    }
}
