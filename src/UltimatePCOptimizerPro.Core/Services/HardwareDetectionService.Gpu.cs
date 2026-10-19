using System.Text.RegularExpressions;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

public sealed partial class HardwareDetectionService
{
    private static readonly Regex IntegratedNamePattern = new(
        @"(UHD|HD Graphics|Iris|Arc\(TM\) Graphics|Arc\(TM\) 1[34]0V|Radeon\(TM\) Graphics|Radeon Graphics|Radeon \d{3}M|Vega \d+ Graphics|Adreno)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static List<GpuInfo> DetectGpus(List<ScanWarning> warnings)
    {
        var gpus = new List<GpuInfo>();
        try
        {
            int index = 0;
            foreach (var d in Dxgi.EnumerateAdapters())
            {
                if ((d.Flags & Dxgi.DXGI_ADAPTER_FLAG_SOFTWARE) != 0 || d.VendorId == 0x1414 && d.DeviceId == 0x8c)
                {
                    // Microsoft Basic Render Driver – not a real GPU.
                    continue;
                }
                var g = new GpuInfo
                {
                    AdapterIndex = index++,
                    Name = d.Description.Trim(),
                    VendorId = d.VendorId,
                    DeviceId = d.DeviceId,
                    Vendor = VendorFromId(d.VendorId),
                    DedicatedVideoMemoryBytes = d.DedicatedVideoMemory,
                    SharedSystemMemoryBytes = d.SharedSystemMemory,
                    LuidLow = d.AdapterLuidLow,
                    LuidHigh = d.AdapterLuidHigh,
                };
                // Indirect display / remote adapters can appear multiple times with the same name; keep unique LUIDs.
                if (gpus.Any(x => x.LuidLow == g.LuidLow && x.LuidHigh == g.LuidHigh)) continue;
                var type = D3dkmt.QueryAdapterType(g.LuidLow, g.LuidHigh);
                if (type is not null)
                {
                    g.IsHybridIntegrated = type.HybridIntegrated;
                    g.IsHybridDiscrete = type.HybridDiscrete;
                    g.IsSoftware = type.Software;
                }
                g.IsIntegrated = type?.HybridIntegrated == true
                                 || (type?.HybridDiscrete != true && (IntegratedNamePattern.IsMatch(g.Name) || g.DedicatedVideoMemoryBytes < 1UL * 1024 * 1024 * 1024 && g.Vendor is GpuVendor.Intel or GpuVendor.Amd or GpuVendor.Qualcomm));
                var (_, maxMhz) = D3dkmt.QueryNodeFrequency(g.LuidLow, g.LuidHigh);
                g.MaxCoreClockMHz = maxMhz;
                gpus.Add(g);
            }
        }
        catch (Exception ex)
        {
            warnings.Add(new ScanWarning("Graphics", "DXGI enumeration failed: " + ex.Message));
        }

        // Driver details come from WMI; match by name.
        var controllers = Wmi.TryQuery(Wmi.Cimv2, "SELECT Name, DriverVersion, DriverDate, PNPDeviceID, AdapterRAM, VideoModeDescription, AdapterCompatibility FROM Win32_VideoController", out var err);
        if (err is not null) warnings.Add(new ScanWarning("Graphics", err));
        foreach (var c in controllers)
        {
            var name = c.Str("Name") ?? "";
            var match = gpus.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && g.DriverVersion is null);
            if (match is null && gpus.Count == 0)
            {
                // DXGI failed; fall back to WMI only (AdapterRAM is a 32-bit field capped at 4 GB).
                match = new GpuInfo { Name = name, Vendor = VendorFromName(c.Str("AdapterCompatibility") ?? name), DedicatedVideoMemoryBytes = c.ULong("AdapterRAM") ?? 0 };
                match.IsIntegrated = IntegratedNamePattern.IsMatch(name);
                gpus.Add(match);
            }
            if (match is null) continue;
            match.DriverVersion = c.Str("DriverVersion");
            match.DriverDate = c.Date("DriverDate");
            match.PnpDeviceId = c.Str("PNPDeviceID");
            match.CurrentVideoMode = c.Str("VideoModeDescription");
        }
        return gpus;
    }

    private static GpuVendor VendorFromId(uint id) => id switch
    {
        0x10DE => GpuVendor.Nvidia,
        0x1002 or 0x1022 => GpuVendor.Amd,
        0x8086 => GpuVendor.Intel,
        0x1414 => GpuVendor.Microsoft,
        0x5143 or 0x4D4F4351 => GpuVendor.Qualcomm,
        _ => GpuVendor.Other
    };

    private static GpuVendor VendorFromName(string s) =>
        s.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia :
        s.Contains("AMD", StringComparison.OrdinalIgnoreCase) || s.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd :
        s.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel : GpuVendor.Other;
}
