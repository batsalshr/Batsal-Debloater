namespace UltimatePCOptimizerPro.Core.Models;

public sealed class NetworkAdapterInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string InterfaceType { get; set; } = "";
    public bool IsWireless { get; set; }
    public bool IsEthernet { get; set; }
    public bool IsActive { get; set; }
    public long LinkSpeedBps { get; set; }
    public string? MacAddress { get; set; }
    public List<string> IPv4Addresses { get; set; } = new();
    public List<string> IPv6Addresses { get; set; } = new();
    public List<string> Gateways { get; set; } = new();
    public List<string> DnsServers { get; set; } = new();
    public bool? DhcpEnabled { get; set; }
    public string? DriverVersion { get; set; }
    public string? DriverDate { get; set; }
    public string? DriverProvider { get; set; }
    public string? DeviceInstanceId { get; set; }
    public int? WifiSignalPercent { get; set; }
    public string? WifiSsid { get; set; }
    public string? WifiRadioType { get; set; }

    public string LinkSpeedDisplay => LinkSpeedBps <= 0 ? "Unknown"
        : LinkSpeedBps >= 1_000_000_000 ? $"{LinkSpeedBps / 1_000_000_000d:0.#} Gbps" : $"{LinkSpeedBps / 1_000_000d:0} Mbps";
}

public sealed class NetworkInfo
{
    public List<NetworkAdapterInfo> Adapters { get; set; } = new();
    public NetworkAdapterInfo? Active => Adapters.FirstOrDefault(a => a.IsActive);
}
