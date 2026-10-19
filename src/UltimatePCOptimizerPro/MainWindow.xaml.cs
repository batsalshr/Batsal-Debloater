using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UltimatePCOptimizerPro;

public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    /// <summary>Flashes the taskbar button until the window is activated (FLASHW_TRAY | FLASHW_TIMERNOFG).</summary>
    public void FlashIfInactive()
    {
        if (IsActive) return;
        var info = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = new WindowInteropHelper(this).Handle, dwFlags = 0x2 | 0xC, uCount = uint.MaxValue };
        FlashWindowEx(ref info);
    }

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    /// <summary>Uses the native title bar (snap layouts, accessibility) but themes it dark. Unsupported attributes are ignored by older Windows builds.</summary>
    private void ApplyDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        int caption = 0x00150F0B;   // #0B0F15 as COLORREF (0x00BBGGRR)
        int border = 0x003A2C21;    // #212C3A
        int text = 0x00F5EDE6;      // #E6EDF5
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }
}
