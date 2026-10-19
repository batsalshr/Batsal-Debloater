using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

public sealed partial class HardwareDetectionService
{
    public static List<DisplayInfo> DetectDisplays(List<ScanWarning> warnings)
    {
        var displays = new List<DisplayInfo>();
        var friendlyNames = ReadMonitorFriendlyNames();
        for (uint i = 0; i < 32; i++)
        {
            var adapter = NativeMethods.DISPLAY_DEVICE.Create();
            if (!NativeMethods.EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

            var current = NativeMethods.DEVMODE.Create();
            if (!NativeMethods.EnumDisplaySettings(adapter.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref current)) continue;

            var info = new DisplayInfo
            {
                DeviceName = adapter.DeviceName,
                AdapterName = adapter.DeviceString,
                IsPrimary = (adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                Width = current.dmPelsWidth,
                Height = current.dmPelsHeight,
                BitsPerPixel = current.dmBitsPerPel,
                RefreshHz = current.dmDisplayFrequency,
                PositionX = current.dmPositionX,
                PositionY = current.dmPositionY
            };

            var monitor = NativeMethods.DISPLAY_DEVICE.Create();
            if (NativeMethods.EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
            {
                var key = InterfacePathToInstance(monitor.DeviceID);
                info.FriendlyName = key is not null && friendlyNames.TryGetValue(key, out var fn) ? fn : monitor.DeviceString;
            }

            var rates = new SortedSet<int>();
            for (int m = 0; m < 4096; m++)
            {
                var mode = NativeMethods.DEVMODE.Create();
                if (!NativeMethods.EnumDisplaySettings(adapter.DeviceName, m, ref mode)) break;
                if (mode.dmPelsWidth == info.Width && mode.dmPelsHeight == info.Height && mode.dmBitsPerPel == info.BitsPerPixel && mode.dmDisplayFrequency > 1)
                    rates.Add(mode.dmDisplayFrequency);
            }
            info.SupportedRefreshRates = rates.ToList();
            displays.Add(info);
        }
        if (displays.Count == 0) warnings.Add(new ScanWarning("Displays", "No active displays were reported by the graphics driver."));
        return displays;
    }

    /// <summary>\\?\DISPLAY#GSM5B7F#5&amp;2a&amp;0&amp;UID4352#{guid} → DISPLAY\GSM5B7F\5&amp;2a&amp;0&amp;UID4352</summary>
    private static string? InterfacePathToInstance(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(@"\\?\")) return null;
        var core = path[4..];
        var guidStart = core.IndexOf("#{", StringComparison.Ordinal);
        if (guidStart > 0) core = core[..guidStart];
        return core.Replace('#', '\\').ToUpperInvariant();
    }

    private static Dictionary<string, string> ReadMonitorFriendlyNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in Wmi.TryQuery(Wmi.RootWmi, "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID", out _))
        {
            var instance = o.Str("InstanceName");
            if (instance is null || o.Raw("UserFriendlyName") is not ushort[] chars) continue;
            var name = new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
            if (name.Length == 0) continue;
            // InstanceName ends with "_0"
            var key = instance.EndsWith("_0") ? instance[..^2] : instance;
            map[key.ToUpperInvariant()] = name;
        }
        return map;
    }
}
