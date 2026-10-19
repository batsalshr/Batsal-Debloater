namespace UltimatePCOptimizerPro.Core.Models;

public enum GpuVendor { Unknown, Nvidia, Amd, Intel, Microsoft, Qualcomm, Other }

public sealed class GpuInfo
{
    public int AdapterIndex { get; set; }
    public string Name { get; set; } = "Unknown GPU";
    public GpuVendor Vendor { get; set; }
    public uint VendorId { get; set; }
    public uint DeviceId { get; set; }
    public string? DriverVersion { get; set; }
    public DateTime? DriverDate { get; set; }
    public ulong DedicatedVideoMemoryBytes { get; set; }
    public ulong SharedSystemMemoryBytes { get; set; }
    public bool IsIntegrated { get; set; }
    public bool IsSoftware { get; set; }
    /// <summary>Reported by the kernel graphics stack on hybrid (Optimus / switchable) systems.</summary>
    public bool? IsHybridIntegrated { get; set; }
    public bool? IsHybridDiscrete { get; set; }
    public uint LuidLow { get; set; }
    public int LuidHigh { get; set; }
    public string? PnpDeviceId { get; set; }
    public string? CurrentVideoMode { get; set; }
    public double? MaxCoreClockMHz { get; set; }

    /// <summary>Format used by the "GPU Engine" performance counter instance names.</summary>
    public string LuidKey => $"luid_0x{(uint)LuidHigh:X8}_0x{LuidLow:X8}";

    public bool IsDedicated => !IsIntegrated && !IsSoftware;
}
