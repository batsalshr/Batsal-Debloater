using System.Runtime.InteropServices;

namespace UltimatePCOptimizerPro.Core.Interop;

/// <summary>Minimal DXGI COM interop used to enumerate graphics adapters with accurate 64-bit VRAM sizes.</summary>
internal static class Dxgi
{
    public const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    public const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIFactory1
    {
        // IDXGIObject
        [PreserveSig] int SetPrivateData(ref Guid name, uint size, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint size, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
        // IDXGIFactory
        [PreserveSig] int EnumAdapters(uint index, out IntPtr adapter);
        [PreserveSig] int MakeWindowAssociation(IntPtr hwnd, uint flags);
        [PreserveSig] int GetWindowAssociation(out IntPtr hwnd);
        [PreserveSig] int CreateSwapChain(IntPtr device, IntPtr desc, out IntPtr swapChain);
        [PreserveSig] int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
        // IDXGIFactory1
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1? adapter);
        [PreserveSig] int IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIAdapter1
    {
        // IDXGIObject
        [PreserveSig] int SetPrivateData(ref Guid name, uint size, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint size, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
        // IDXGIAdapter
        [PreserveSig] int EnumOutputs(uint index, out IntPtr output);
        [PreserveSig] int GetDesc(IntPtr desc);
        [PreserveSig] int CheckInterfaceSupport(ref Guid interfaceName, out long umdVersion);
        // IDXGIAdapter1
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1 factory);

    public static List<DXGI_ADAPTER_DESC1> EnumerateAdapters()
    {
        var result = new List<DXGI_ADAPTER_DESC1>();
        var iid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref iid, out var factory) < 0 || factory is null) return result;
        try
        {
            for (uint i = 0; i < 16; i++)
            {
                int hr = factory.EnumAdapters1(i, out var adapter);
                if (hr == DXGI_ERROR_NOT_FOUND || adapter is null) break;
                try
                {
                    if (adapter.GetDesc1(out var desc) >= 0) result.Add(desc);
                }
                finally { Marshal.ReleaseComObject(adapter); }
            }
        }
        finally { Marshal.ReleaseComObject(factory); }
        return result;
    }
}
