namespace UltimatePCOptimizerPro.Core.Models;

/// <summary>Complete result of one hardware/OS scan. Every field is read from the live system.</summary>
public sealed class SystemSnapshot
{
    public DateTimeOffset ScannedAt { get; set; } = DateTimeOffset.Now;
    public TimeSpan ScanDuration { get; set; }
    public CpuInfo Cpu { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public StorageInfo Storage { get; set; } = new();
    public BoardInfo Board { get; set; } = new();
    public List<DisplayInfo> Displays { get; set; } = new();
    public NetworkInfo Network { get; set; } = new();
    public OsInfo Os { get; set; } = new();
    public PowerStatusInfo Power { get; set; } = new();
    public List<SoftwareEntry> RelevantSoftware { get; set; } = new();
    public List<ScanWarning> Warnings { get; set; } = new();

    public GpuInfo? PrimaryGpu => Gpus.Where(g => !g.IsSoftware).OrderByDescending(g => g.IsDedicated).ThenByDescending(g => g.DedicatedVideoMemoryBytes).FirstOrDefault();
    public DisplayInfo? PrimaryDisplay => Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays.FirstOrDefault();
    public bool HasDedicatedGpu => Gpus.Any(g => g.IsDedicated);
    public bool IsHybridGraphics => Gpus.Count(g => !g.IsSoftware) > 1 && Gpus.Any(g => g.IsIntegrated) && HasDedicatedGpu;
    public bool IsLaptop => Board.IsLaptop || Power.HasBattery;
}

public sealed record ScanWarning(string Area, string Message);

public sealed record ScanProgress(string Step, int Completed, int Total);
