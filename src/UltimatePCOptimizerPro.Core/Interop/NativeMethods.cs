using System.Runtime.InteropServices;

namespace UltimatePCOptimizerPro.Core.Interop;

internal static class NativeMethods
{
    // ---------------- kernel32 ----------------
    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    public static MEMORYSTATUSEX GetMemoryStatus()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref m);
        return m;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION info, uint size);

    public const int RelationProcessorCore = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint returnedLength);

    [DllImport("kernel32.dll")]
    public static extern bool GetFirmwareType(out int firmwareType);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessTimes(IntPtr process, out FILETIME creation, out FILETIME exit, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessIoCounters(IntPtr process, out IO_COUNTERS counters);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);

    // ---------------- user32 ----------------
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    public const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;
    public const int DM_BITSPERPEL = 0x40000;
    public const int DM_PELSWIDTH = 0x80000;
    public const int DM_PELSHEIGHT = 0x100000;
    public const int DM_DISPLAYFREQUENCY = 0x400000;
    public const int CDS_UPDATEREGISTRY = 0x01;
    public const int CDS_TEST = 0x02;
    public const int DISP_CHANGE_SUCCESSFUL = 0;
    public const int DISP_CHANGE_RESTART = 1;
    public const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;

        public static DISPLAY_DEVICE Create() => new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;

        public static DEVMODE Create() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>(), dmDeviceName = "", dmFormName = "" };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(string? device, uint devNum, ref DISPLAY_DEVICE displayDevice, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr param);

    public const uint SPI_SETMOUSE = 0x0004;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint param, int[] pvParam, uint winIni);

    // ---------------- powrprof ----------------
    [DllImport("powrprof.dll")]
    public static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    public static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroup, IntPtr setting, IntPtr buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroup, uint accessFlags, uint index, IntPtr buffer, ref uint bufferSize);

    public const uint ACCESS_SCHEME = 16;

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr mem);

    public static Guid? GetActivePowerScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    public static string? GetPowerSchemeName(Guid scheme)
    {
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (size == 0) return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0) return null;
            return Marshal.PtrToStringUni(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static List<Guid> EnumeratePowerSchemes()
    {
        var list = new List<Guid>();
        var buffer = Marshal.AllocHGlobal(16);
        try
        {
            for (uint i = 0; ; i++)
            {
                uint size = 16;
                if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, i, buffer, ref size) != 0) break;
                list.Add(Marshal.PtrToStructure<Guid>(buffer));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return list;
    }

    // ---------------- advapi32 (services) ----------------
    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SERVICE_QUERY_CONFIG = 0x0001;
    public const uint SERVICE_CHANGE_CONFIG = 0x0002;
    public const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    public const uint SERVICE_AUTO_START = 2;
    public const uint SERVICE_DEMAND_START = 3;
    public const uint SERVICE_DISABLED = 4;
    public const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ChangeServiceConfig(IntPtr service, uint serviceType, uint startType, uint errorControl,
        string? binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? startName, string? password, string? displayName);

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_DELAYED_AUTO_START_INFO
    {
        public int fDelayedAutostart;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, ref SERVICE_DELAYED_AUTO_START_INFO info);

    // ---------------- shell32 ----------------
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    public const uint SHERB_NOCONFIRMATION = 0x1;
    public const uint SHERB_NOPROGRESSUI = 0x2;
    public const uint SHERB_NOSOUND = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    // ---------------- dwmapi ----------------
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
