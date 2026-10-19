namespace UltimatePCOptimizerPro.Core.Models;

public sealed class MemoryInfo
{
    public ulong TotalBytes { get; set; }
    public ulong AvailableBytes { get; set; }
    public ulong CommitTotalBytes { get; set; }
    public ulong CommitLimitBytes { get; set; }
    public int? TotalSlots { get; set; }
    public List<MemoryModule> Modules { get; set; } = new();
    /// <summary>Channel layout cannot be read directly from Windows; this is an explicitly labelled estimate.</summary>
    public string ChannelEstimate { get; set; } = "Unknown";

    public double UsedPercent => TotalBytes == 0 ? 0 : 100.0 * (TotalBytes - AvailableBytes) / TotalBytes;
    public double TotalGB => TotalBytes / 1024d / 1024d / 1024d;
    public string? PrimaryType => Modules.Select(m => m.MemoryType).FirstOrDefault(t => !string.IsNullOrEmpty(t) && t != "Unknown");
    public int? ReportedSpeedMTs => Modules.Select(m => m.ConfiguredSpeedMTs ?? m.SpeedMTs).Where(s => s > 0).DefaultIfEmpty().Min();
}

public sealed class MemoryModule
{
    public ulong CapacityBytes { get; set; }
    public int? SpeedMTs { get; set; }
    public int? ConfiguredSpeedMTs { get; set; }
    public string MemoryType { get; set; } = "Unknown";
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
    public string? DeviceLocator { get; set; }
    public string? BankLabel { get; set; }
    public string? FormFactor { get; set; }
}
