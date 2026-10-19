using System.Runtime.InteropServices;

namespace UltimatePCOptimizerPro.Core.Interop;

/// <summary>
/// Kernel graphics thunk queries (the same source Task Manager uses for GPU temperature).
/// Supported on Windows 10 2004+ with WDDM 2.7+ drivers; every call is optional and failure means "not available".
/// </summary>
internal static class D3dkmt
{
    private const int KMTQAITYPE_ADAPTERTYPE = 15;
    private const int KMTQAITYPE_NODEPERFDATA = 61;
    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_OPENADAPTERFROMLUID { public LUID AdapterLuid; public uint hAdapter; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER { public uint hAdapter; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_NODE_PERFDATA
    {
        public uint NodeOrdinal;
        public uint PhysicalAdapterIndex;
        public ulong Frequency;
        public ulong MaxFrequency;
        public ulong MaxFrequencyOC;
        public uint Voltage;
        public uint VoltageMax;
        public uint VoltageMaxOC;
        public ulong MaxTransitionLatency;
    }

    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromLuid(ref D3DKMT_OPENADAPTERFROMLUID p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO p);

    public sealed record AdapterPerf(double? TemperatureC, double? PowerPercent, uint? FanRpm);
    public sealed record AdapterType(bool HybridDiscrete, bool HybridIntegrated, bool Software);

    private static T? Query<T>(uint luidLow, int luidHigh, int type, T input) where T : struct
    {
        var open = new D3DKMT_OPENADAPTERFROMLUID { AdapterLuid = new LUID { LowPart = luidLow, HighPart = luidHigh } };
        try
        {
            if (D3DKMTOpenAdapterFromLuid(ref open) != 0) return null;
        }
        catch (EntryPointNotFoundException) { return null; }
        catch (DllNotFoundException) { return null; }

        int size = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(input, buffer, false);
            var q = new D3DKMT_QUERYADAPTERINFO { hAdapter = open.hAdapter, Type = type, pPrivateDriverData = buffer, PrivateDriverDataSize = (uint)size };
            if (D3DKMTQueryAdapterInfo(ref q) != 0) return null;
            return Marshal.PtrToStructure<T>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            var close = new D3DKMT_CLOSEADAPTER { hAdapter = open.hAdapter };
            D3DKMTCloseAdapter(ref close);
        }
    }

    public static AdapterPerf? QueryAdapterPerf(uint luidLow, int luidHigh)
    {
        var data = Query(luidLow, luidHigh, KMTQAITYPE_ADAPTERPERFDATA, new D3DKMT_ADAPTER_PERFDATA());
        if (data is null) return null;
        var d = data.Value;
        // Temperature is reported in deci-degrees Celsius; 0 means the driver does not report it.
        double? temp = d.Temperature is > 0 and < 1500 ? d.Temperature / 10.0 : null;
        double? power = d.Power is > 0 and <= 1000 ? d.Power / 10.0 : null;
        uint? fan = d.FanRPM is > 0 and < 20000 ? d.FanRPM : null;
        return new AdapterPerf(temp, power, fan);
    }

    /// <summary>Returns the current frequency (MHz) of engine node 0 (normally the 3D engine).</summary>
    public static (double? CurrentMHz, double? MaxMHz) QueryNodeFrequency(uint luidLow, int luidHigh)
    {
        var data = Query(luidLow, luidHigh, KMTQAITYPE_NODEPERFDATA, new D3DKMT_NODE_PERFDATA { NodeOrdinal = 0 });
        if (data is null) return (null, null);
        var d = data.Value;
        double? cur = d.Frequency is > 0 and < 10_000_000_000 ? d.Frequency / 1_000_000.0 : null;
        double? max = d.MaxFrequency is > 0 and < 10_000_000_000 ? d.MaxFrequency / 1_000_000.0 : null;
        return (cur, max);
    }

    public static AdapterType? QueryAdapterType(uint luidLow, int luidHigh)
    {
        var data = Query(luidLow, luidHigh, KMTQAITYPE_ADAPTERTYPE, 0u);
        if (data is null) return null;
        uint v = data.Value;
        return new AdapterType(HybridDiscrete: (v & (1u << 4)) != 0, HybridIntegrated: (v & (1u << 5)) != 0, Software: (v & (1u << 2)) != 0);
    }
}
