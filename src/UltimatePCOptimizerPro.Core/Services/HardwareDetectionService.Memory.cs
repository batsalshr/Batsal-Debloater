using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

public sealed partial class HardwareDetectionService
{
    private static MemoryInfo DetectMemory(List<ScanWarning> warnings)
    {
        var mem = new MemoryInfo();
        var status = NativeMethods.GetMemoryStatus();
        mem.TotalBytes = status.ullTotalPhys;
        mem.AvailableBytes = status.ullAvailPhys;
        if (NativeMethods.GetPerformanceInfo(out var perf, (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.PERFORMANCE_INFORMATION>()))
        {
            mem.CommitTotalBytes = (ulong)perf.CommitTotal * (ulong)perf.PageSize;
            mem.CommitLimitBytes = (ulong)perf.CommitLimit * (ulong)perf.PageSize;
        }

        var modules = Wmi.TryQuery(Wmi.Cimv2,
            "SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, MemoryType, Manufacturer, PartNumber, DeviceLocator, BankLabel, FormFactor FROM Win32_PhysicalMemory",
            out var err);
        foreach (var o in modules)
        {
            mem.Modules.Add(new MemoryModule
            {
                CapacityBytes = o.ULong("Capacity") ?? 0,
                SpeedMTs = o.Int("Speed") is > 0 and var s ? s : null,
                ConfiguredSpeedMTs = o.Int("ConfiguredClockSpeed") is > 0 and var cs ? cs : null,
                MemoryType = MemoryTypeName(o.Int("SMBIOSMemoryType") ?? 0, o.Int("MemoryType") ?? 0),
                Manufacturer = o.Str("Manufacturer"),
                PartNumber = o.Str("PartNumber"),
                DeviceLocator = o.Str("DeviceLocator"),
                BankLabel = o.Str("BankLabel"),
                FormFactor = (o.Int("FormFactor") ?? 0) switch { 8 => "DIMM", 12 => "SO-DIMM", 0 => null, var f => $"Form factor {f}" }
            });
        }
        if (err is not null) warnings.Add(new ScanWarning("Memory", err));

        foreach (var o in Wmi.TryQuery(Wmi.Cimv2, "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray", out _))
        {
            mem.TotalSlots = (mem.TotalSlots ?? 0) + (o.Int("MemoryDevices") ?? 0);
        }

        mem.ChannelEstimate = EstimateChannels(mem);
        return mem;
    }

    private static string MemoryTypeName(int smbios, int legacy) => smbios switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => legacy switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", _ => "Unknown" }
    };

    /// <summary>
    /// Windows does not expose the memory controller's channel mode. A single desktop module is a reliable sign of
    /// single-channel operation; other layouts are reported as estimates only.
    /// </summary>
    private static string EstimateChannels(MemoryInfo mem)
    {
        int count = mem.Modules.Count;
        if (count == 0) return "Unknown (module information not reported)";
        if (mem.Modules.Any(m => m.MemoryType.StartsWith("LP", StringComparison.OrdinalIgnoreCase)))
            return "Soldered LPDDR – channel layout fixed by the manufacturer";
        if (count == 1) return "Single module – single-channel (estimate)";
        bool sameSize = mem.Modules.Select(m => m.CapacityBytes).Distinct().Count() == 1;
        if (count % 2 == 0 && sameSize) return $"{count} matched modules – dual-channel likely (estimate)";
        return $"{count} modules with mixed sizes – may run in flex/asymmetric mode (estimate)";
    }
}
