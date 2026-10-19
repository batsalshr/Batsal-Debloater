using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

public sealed partial class HardwareDetectionService
{
    private const string NetClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    public static NetworkInfo DetectNetwork(List<ScanWarning> warnings)
    {
        var info = new NetworkInfo();
        var drivers = ReadNetworkDriverInfo();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            IPInterfaceProperties props;
            try { props = ni.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }

            var a = new NetworkAdapterInfo
            {
                Id = ni.Id,
                Name = ni.Name,
                Description = ni.Description,
                InterfaceType = ni.NetworkInterfaceType.ToString(),
                IsWireless = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                IsEthernet = ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT,
                LinkSpeedBps = ni.Speed,
                MacAddress = FormatMac(ni.GetPhysicalAddress()),
            };
            foreach (var u in props.UnicastAddresses)
            {
                if (u.Address.AddressFamily == AddressFamily.InterNetwork) a.IPv4Addresses.Add(u.Address.ToString());
                else if (u.Address.AddressFamily == AddressFamily.InterNetworkV6 && !u.Address.IsIPv6LinkLocal) a.IPv6Addresses.Add(u.Address.ToString());
            }
            foreach (var g in props.GatewayAddresses)
            {
                if (!g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any)) a.Gateways.Add(g.Address.ToString());
            }
            a.DnsServers = props.DnsAddresses.Select(d => d.ToString()).ToList();
            try { a.DhcpEnabled = props.GetIPv4Properties()?.IsDhcpEnabled; } catch { /* IPv4 disabled */ }

            if (drivers.TryGetValue(ni.Id, out var drv))
            {
                a.DriverVersion = drv.Version;
                a.DriverDate = drv.Date;
                a.DriverProvider = drv.Provider;
                a.DeviceInstanceId = drv.InstanceId;
            }
            info.Adapters.Add(a);
        }

        // The active adapter is the one that owns the IPv4 default route; prefer wired when both exist.
        var active = info.Adapters.Where(a => a.Gateways.Any(g => g.Contains('.')))
                         .OrderByDescending(a => a.IsEthernet).ThenByDescending(a => a.LinkSpeedBps).FirstOrDefault()
                     ?? info.Adapters.FirstOrDefault(a => a.Gateways.Count > 0);
        if (active is not null) active.IsActive = true;
        else warnings.Add(new ScanWarning("Network", "No adapter with a default gateway was found (offline?)."));

        if (info.Adapters.Any(a => a.IsWireless))
        {
            try { ReadWifiDetails(info); }
            catch (Exception ex) { warnings.Add(new ScanWarning("Network", "Wi-Fi details unavailable: " + ex.Message)); }
        }
        return info;
    }

    private static string? FormatMac(PhysicalAddress pa)
    {
        var bytes = pa.GetAddressBytes();
        return bytes.Length == 0 ? null : string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    private sealed record DriverDetails(string? Version, string? Date, string? Provider, string? InstanceId);

    /// <summary>Reads driver details from the network device class key (no admin required).</summary>
    private static Dictionary<string, DriverDetails> ReadNetworkDriverInfo()
    {
        var map = new Dictionary<string, DriverDetails>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(NetClassKey);
            if (cls is null) return map;
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    if (k?.GetValue("NetCfgInstanceId") is not string id) continue;
                    map[id] = new DriverDetails(k.GetValue("DriverVersion") as string, k.GetValue("DriverDate") as string,
                        k.GetValue("ProviderName") as string, k.GetValue("DeviceInstanceID") as string);
                }
                catch (System.Security.SecurityException) { /* "Properties" subkeys are ACL protected */ }
            }
        }
        catch (Exception) { /* best effort */ }
        return map;
    }

    /// <summary>netsh output is localised; only numeric/SSID fields that are language independent are parsed.</summary>
    private static void ReadWifiDetails(NetworkInfo info)
    {
        var r = ProcessRunner.Run(ProcessRunner.System32("netsh.exe"), "wlan show interfaces", 8000);
        if (!r.Success) return;
        var blocks = Regex.Split(r.StdOut, @"\r?\n\s*\r?\n");
        foreach (var block in blocks)
        {
            var signal = Regex.Match(block, @":\s*(\d{1,3})\s*%");
            if (!signal.Success) continue;
            var adapter = info.Adapters.FirstOrDefault(a => a.IsWireless && block.Contains(a.Description, StringComparison.OrdinalIgnoreCase))
                          ?? info.Adapters.FirstOrDefault(a => a.IsWireless);
            if (adapter is null) continue;
            adapter.WifiSignalPercent = int.Parse(signal.Groups[1].Value);
            var ssid = Regex.Match(block, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
            if (ssid.Success) adapter.WifiSsid = ssid.Groups[1].Value.Trim();
            var radio = Regex.Match(block, @"(802\.11[a-z]{1,2})", RegexOptions.IgnoreCase);
            if (radio.Success) adapter.WifiRadioType = radio.Groups[1].Value;
        }
    }
}
