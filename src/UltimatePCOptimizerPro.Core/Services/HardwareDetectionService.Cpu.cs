using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.RegularExpressions;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

public sealed partial class HardwareDetectionService
{
    private static CpuInfo DetectCpu(List<ScanWarning> warnings)
    {
        var cpu = new CpuInfo { Architecture = RuntimeInformation.ProcessArchitecture.ToString(), LogicalProcessors = Environment.ProcessorCount };
        int physical = 0;
        var rows = Wmi.TryQuery(Wmi.Cimv2,
            "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation, VirtualizationFirmwareEnabled FROM Win32_Processor",
            out var err);
        foreach (var o in rows)
        {
            cpu.Name = Regex.Replace(o.Str("Name") ?? cpu.Name, @"\s+", " ");
            cpu.Manufacturer = o.Str("Manufacturer") ?? cpu.Manufacturer;
            physical += o.Int("NumberOfCores") ?? 0;
            cpu.BaseClockMHz = o.Int("MaxClockSpeed");
            cpu.L2CacheKB = o.Int("L2CacheSize");
            cpu.L3CacheKB = o.Int("L3CacheSize");
            cpu.Socket = o.Str("SocketDesignation");
            cpu.VirtualizationFirmwareEnabled = o.Bool("VirtualizationFirmwareEnabled");
        }
        if (err is not null) warnings.Add(new ScanWarning("Processor", err));
        if (cpu.Manufacturer == "GenuineIntel") cpu.Manufacturer = "Intel";
        if (cpu.Manufacturer == "AuthenticAMD") cpu.Manufacturer = "AMD";

        try
        {
            cpu.Cores = ReadCoreTopology();
        }
        catch (Exception ex) { warnings.Add(new ScanWarning("Processor", "Core topology unavailable: " + ex.Message)); }

        if (cpu.Cores.Count > 0)
        {
            cpu.PhysicalCores = cpu.Cores.Count;
            var classes = cpu.Cores.Select(c => c.EfficiencyClass).Distinct().OrderBy(c => c).ToList();
            cpu.IsHybrid = classes.Count > 1;
            if (cpu.IsHybrid)
            {
                int max = classes.Max();
                cpu.PerformanceCores = cpu.Cores.Count(c => c.EfficiencyClass == max);
                cpu.EfficiencyCores = cpu.Cores.Count - cpu.PerformanceCores;
            }
            else
            {
                cpu.PerformanceCores = cpu.Cores.Count;
            }
            cpu.SmtActive = cpu.Cores.Any(c => c.LogicalProcessorCount > 1);
        }
        else
        {
            cpu.PhysicalCores = physical;
            cpu.SmtActive = cpu.LogicalProcessors > physical && physical > 0;
        }

        cpu.InstructionSets = DetectInstructionSets();
        cpu.GenerationHint = ParseGeneration(cpu.Name);
        return cpu;
    }

    private static List<CpuCoreInfo> ReadCoreTopology()
    {
        uint length = 0;
        NativeMethods.GetLogicalProcessorInformationEx(NativeMethods.RelationProcessorCore, IntPtr.Zero, ref length);
        if (length == 0) return new();
        var buffer = Marshal.AllocHGlobal((int)length);
        var cores = new List<CpuCoreInfo>();
        try
        {
            if (!NativeMethods.GetLogicalProcessorInformationEx(NativeMethods.RelationProcessorCore, buffer, ref length))
                return cores;
            int offset = 0;
            int index = 0;
            while (offset < length)
            {
                var item = buffer + offset;
                int relationship = Marshal.ReadInt32(item, 0);
                int size = Marshal.ReadInt32(item, 4);
                if (size <= 0) break;
                if (relationship == NativeMethods.RelationProcessorCore)
                {
                    // PROCESSOR_RELATIONSHIP: Flags(1) EfficiencyClass(1) Reserved(20) GroupCount(2) GROUP_AFFINITY[]
                    byte efficiency = Marshal.ReadByte(item, 9);
                    ushort groupCount = (ushort)Marshal.ReadInt16(item, 30);
                    int logical = 0;
                    ulong mask = 0;
                    int group = 0;
                    for (int g = 0; g < groupCount; g++)
                    {
                        // GROUP_AFFINITY: KAFFINITY Mask (pointer-size) + WORD Group + WORD Reserved[3]
                        var ga = item + 32 + g * (IntPtr.Size + 8);
                        mask = IntPtr.Size == 8 ? (ulong)Marshal.ReadInt64(ga) : (uint)Marshal.ReadInt32(ga);
                        group = Marshal.ReadInt16(ga, IntPtr.Size);
                        logical += BitOperations.PopCount(mask);
                    }
                    cores.Add(new CpuCoreInfo(index++, group, efficiency, logical, mask));
                }
                offset += size;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return cores;
    }

    private static List<string> DetectInstructionSets()
    {
        var list = new List<string>();
        if (RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.X86)
        {
            if (Sse3.IsSupported) list.Add("SSE3");
            if (Ssse3.IsSupported) list.Add("SSSE3");
            if (Sse41.IsSupported) list.Add("SSE4.1");
            if (Sse42.IsSupported) list.Add("SSE4.2");
            if (Popcnt.IsSupported) list.Add("POPCNT");
            if (Aes.IsSupported) list.Add("AES-NI");
            if (Pclmulqdq.IsSupported) list.Add("PCLMULQDQ");
            if (Avx.IsSupported) list.Add("AVX");
            if (Avx2.IsSupported) list.Add("AVX2");
            if (Fma.IsSupported) list.Add("FMA3");
            if (Bmi1.IsSupported) list.Add("BMI1");
            if (Bmi2.IsSupported) list.Add("BMI2");
            if (Lzcnt.IsSupported) list.Add("LZCNT");
            if (Avx512F.IsSupported) list.Add("AVX-512F");
            if (Avx512BW.IsSupported) list.Add("AVX-512BW");
        }
        else if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            if (System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported) list.Add("NEON");
            if (System.Runtime.Intrinsics.Arm.Aes.IsSupported) list.Add("ARMv8 AES");
            if (System.Runtime.Intrinsics.Arm.Crc32.IsSupported) list.Add("CRC32");
            if (System.Runtime.Intrinsics.Arm.Dp.IsSupported) list.Add("DotProd");
        }
        return list;
    }

    /// <summary>Returns a generation/series label only when the model string matches a known naming scheme.</summary>
    internal static string? ParseGeneration(string name)
    {
        var intel = Regex.Match(name, @"\bi[3579]-(\d{4,5})([A-Z]{0,3})\b", RegexOptions.IgnoreCase);
        if (intel.Success)
        {
            var digits = intel.Groups[1].Value;
            int gen = digits.Length == 5 ? int.Parse(digits[..2]) : int.Parse(digits[..1]);
            return $"Intel Core {gen}th generation";
        }
        var ultra = Regex.Match(name, @"Ultra\s+[3579]\s+(\d)(\d{2})([A-Z]{0,2})", RegexOptions.IgnoreCase);
        if (ultra.Success)
        {
            return ultra.Groups[1].Value switch
            {
                "1" => "Intel Core Ultra Series 1 (Meteor Lake)",
                "2" => "Intel Core Ultra Series 2 (Lunar Lake / Arrow Lake)",
                var s => $"Intel Core Ultra Series {s}"
            };
        }
        var ryzen = Regex.Match(name, @"Ryzen\s+(?:Threadripper\s+)?[3579]\s+(?:PRO\s+)?(\d)(\d{3})", RegexOptions.IgnoreCase);
        if (ryzen.Success)
        {
            return $"AMD Ryzen {ryzen.Groups[1].Value}000 series";
        }
        var ryzenAi = Regex.Match(name, @"Ryzen\s+AI\s+(?:MAX\+?\s+|[579]\s+)(?:PRO\s+)?(\d)(\d{2})", RegexOptions.IgnoreCase);
        if (ryzenAi.Success) return $"AMD Ryzen AI {ryzenAi.Groups[1].Value}00 series";
        return null;
    }
}
