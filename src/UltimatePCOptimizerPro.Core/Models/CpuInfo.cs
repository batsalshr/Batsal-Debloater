namespace UltimatePCOptimizerPro.Core.Models;

public sealed class CpuInfo
{
    public string Name { get; set; } = "Unknown processor";
    public string Manufacturer { get; set; } = "Unknown";
    public string Architecture { get; set; } = "";
    /// <summary>Human readable generation/series, only filled when the model string is reliably parseable.</summary>
    public string? GenerationHint { get; set; }
    public int PhysicalCores { get; set; }
    public int LogicalProcessors { get; set; }
    public int? BaseClockMHz { get; set; }
    public int? L2CacheKB { get; set; }
    public int? L3CacheKB { get; set; }
    public string? Socket { get; set; }
    public bool IsHybrid { get; set; }
    public int PerformanceCores { get; set; }
    public int EfficiencyCores { get; set; }
    public bool SmtActive { get; set; }
    public bool? VirtualizationFirmwareEnabled { get; set; }
    public List<string> InstructionSets { get; set; } = new();
    public List<CpuCoreInfo> Cores { get; set; } = new();

    public bool IsIntel => Manufacturer.Contains("Intel", StringComparison.OrdinalIgnoreCase);
    public bool IsAmd => Manufacturer.Contains("AMD", StringComparison.OrdinalIgnoreCase);
}

public sealed record CpuCoreInfo(int Index, int Group, int EfficiencyClass, int LogicalProcessorCount, ulong AffinityMask);
