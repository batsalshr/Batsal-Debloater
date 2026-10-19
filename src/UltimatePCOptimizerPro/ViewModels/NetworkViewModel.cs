using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimatePCOptimizerPro.Core;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

public sealed class SavedPingResult
{
    public DateTimeOffset Timestamp { get; set; }
    public string Label { get; set; } = "";
    public string Adapter { get; set; } = "";
    public PingStatistics Stats { get; set; } = new();
    public string Display => $"{Timestamp:yyyy-MM-dd HH:mm} · {Label} · {Stats.Target} · avg {Stats.AverageMs:0.0} ms · jitter {Stats.JitterMs:0.0} ms · loss {Stats.LossPercent:0.#}% · {Adapter}";
}

public sealed partial class NetworkViewModel : ModulePageViewModel
{
    private readonly NetworkDiagnosticsService _net;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private CancellationTokenSource? _monitorCts;
    private readonly List<PingSample> _monitorSamples = new();
    private static string HistoryFile => Path.Combine(AppPaths.Root, "network-history.json");

    public override string Title => "Network & Ping Optimizer";
    public override string Subtitle => "Measured latency, jitter, packet loss, DNS and bufferbloat. Registry tweaks cannot fix ISP routing or distance to the server.";

    public ObservableCollection<DnsServerResult> DnsResults { get; } = new();
    public ObservableCollection<TraceHop> Hops { get; } = new();
    public ObservableCollection<SavedPingResult> Saved { get; } = new();
    public ObservableCollection<string> RouterTips { get; } = new();
    public List<string> Targets { get; } = new() { "1.1.1.1", "8.8.8.8", "9.9.9.9", "riotgames.com", "valvesoftware.com", "epicgames.com" };

    [ObservableProperty] private string _target = "1.1.1.1";
    [ObservableProperty] private bool _isMonitoring;
    [ObservableProperty] private double[] _pingHistory = Array.Empty<double>();
    [ObservableProperty] private string _monitorStats = "Not running";
    [ObservableProperty] private string _testResult = "";
    [ObservableProperty] private string _dnsStatus = "";
    [ObservableProperty] private string _traceTarget = "valvesoftware.com";
    [ObservableProperty] private string _bloatResult = "";
    [ObservableProperty] private string _bloatGrade = "";
    [ObservableProperty] private string _comparison = "";
    [ObservableProperty] private string _resultLabel = "Before";

    public NetworkViewModel(SystemState state, TweakListViewModel tweaks, NetworkDiagnosticsService net, SettingsService settings, DialogService dialogs) : base(state, tweaks)
    {
        _net = net;
        _settings = settings;
        _dialogs = dialogs;
        _target = settings.Current.PingTarget;
        tweaks.Configure("Network Tweaks", t => t.Category == TweakCategory.Network, showFilters: false);
        foreach (var tip in new[]
                 {
                     "Use Ethernet for competitive gaming; Wi-Fi adds jitter that no Windows setting can remove. If Wi-Fi is required, use 5 GHz/6 GHz close to the router.",
                     "Bufferbloat (latency rising while others download) is fixed on the router: enable SQM / Smart Queue / 'QoS' with fq_codel or CAKE, set to ~90% of your measured speeds.",
                     "Router QoS that prioritises 'gaming devices' only helps if your connection is congested; it cannot lower latency to the server.",
                     "Restart the router/modem if latency or loss to the first hop (your gateway) is high — that is a local problem.",
                     "Loss or latency that appears only at later traceroute hops is on the ISP/server path; contact your ISP with traceroute results.",
                     "DNS speed affects how fast connections start (lookups), not in-game ping.",
                     "Keep network adapter drivers current from the chipset/motherboard or adapter vendor."
                 }) RouterTips.Add(tip);
        LoadSaved();
    }

    protected override void BuildInfo()
    {
        var a = State.Snapshot?.Network.Active;
        if (a is null) { SetInfo(new[] { new InfoRow("Active adapter", "None (offline?)", true) }); return; }
        SetInfo(new[]
        {
            new InfoRow("Active adapter", a.Description), new InfoRow("Connection", a.IsWireless ? $"Wi-Fi{(a.WifiSsid is null ? "" : $" ({a.WifiSsid})")}" : a.IsEthernet ? "Ethernet" : a.InterfaceType,
                Tone: a.IsWireless ? "Warning" : "Success"),
            new InfoRow("Link speed", a.LinkSpeedDisplay), new InfoRow("Wi-Fi signal", a.WifiSignalPercent is { } s ? $"{s}% {a.WifiRadioType}" : "n/a", a.WifiSignalPercent is null),
            new InfoRow("Driver", a.DriverVersion is null ? "n/a" : $"{a.DriverVersion} ({a.DriverDate}) {a.DriverProvider}"),
            new InfoRow("IPv4", string.Join(", ", a.IPv4Addresses)), new InfoRow("Gateway", string.Join(", ", a.Gateways)),
            new InfoRow("DNS servers", string.Join(", ", a.DnsServers)), new InfoRow("Other adapters", string.Join(", ", State.Snapshot!.Network.Adapters.Where(x => !x.IsActive).Select(x => x.Description)))
        });
    }

    partial void OnTargetChanged(string value)
    {
        _settings.Current.PingTarget = value;
        _settings.Save();
    }

    [RelayCommand]
    private async Task ToggleMonitor()
    {
        if (IsMonitoring)
        {
            _monitorCts?.Cancel();
            return;
        }
        IsMonitoring = true;
        _monitorSamples.Clear();
        _monitorCts = new CancellationTokenSource();
        var progress = new Progress<PingSample>(s =>
        {
            _monitorSamples.Add(s);
            if (_monitorSamples.Count > 600) _monitorSamples.RemoveAt(0);
            PingHistory = _monitorSamples.Select(x => x.RoundTripMs ?? 0).TakeLast(120).ToArray();
            var st = NetworkDiagnosticsService.Compute(Target, _monitorSamples);
            MonitorStats = $"Last {s.RoundTripMs?.ToString("0") ?? s.Status} ms · avg {st.AverageMs:0.0} · min {st.MinMs:0} · max {st.MaxMs:0} · jitter {st.JitterMs:0.0} ms · loss {st.LossPercent:0.#}% ({st.Sent} sent)";
        });
        try { await _net.PingSeriesAsync(Target, int.MaxValue, 500, progress, _monitorCts.Token); }
        catch (Exception ex) { MonitorStats = "Error: " + ex.Message; }
        finally { IsMonitoring = false; }
    }

    [RelayCommand]
    private async Task QuickTest()
    {
        await RunBusy($"Sending 100 pings to {Target}…", async () =>
        {
            var samples = new List<PingSample>();
            var progress = new Progress<PingSample>(s => { samples.Add(s); TestResult = $"{samples.Count}/100…"; });
            var st = await _net.PingSeriesAsync(Target, 100, 100, progress);
            TestResult = $"{st.Target}: avg {st.AverageMs:0.0} ms · median {st.MedianMs:0.0} · p95 {st.P95Ms:0.0} · min {st.MinMs:0} · max {st.MaxMs:0} · jitter {st.JitterMs:0.0} ms · loss {st.LossPercent:0.#}%";
            var saved = new SavedPingResult { Timestamp = DateTimeOffset.Now, Label = ResultLabel, Stats = st, Adapter = State.Snapshot?.Network.Active?.Description ?? "" };
            Saved.Insert(0, saved);
            PersistSaved();
            UpdateComparison();
            StatusMessage = "";
        });
    }

    private void UpdateComparison()
    {
        var after = Saved.FirstOrDefault();
        var before = after is null ? null : Saved.Skip(1).FirstOrDefault(s => s.Stats.Target == after.Stats.Target);
        if (after is null || before is null) { Comparison = "Run the same test twice (e.g. 'Before' and 'After' a change) to compare. Use the same target and similar network conditions."; return; }
        string D(double? b, double? a) => b is null || a is null ? "n/a" : $"{a - b:+0.0;-0.0;0.0}";
        Comparison = $"{before.Label} → {after.Label} ({after.Stats.Target}): avg {D(before.Stats.AverageMs, after.Stats.AverageMs)} ms · jitter {D(before.Stats.JitterMs, after.Stats.JitterMs)} ms · " +
                     $"loss {after.Stats.LossPercent - before.Stats.LossPercent:+0.0;-0.0;0.0} pp. Differences of a few ms are normal variation.";
    }

    [RelayCommand]
    private async Task BenchmarkDns()
    {
        await RunBusy("Benchmarking DNS resolvers…", async () =>
        {
            var progress = new Progress<string>(m => DnsStatus = m);
            var results = await _net.BenchmarkDnsAsync(State.Snapshot?.Network.Active?.DnsServers ?? new List<string>(), progress);
            DnsResults.Clear();
            foreach (var r in results) DnsResults.Add(r);
            DnsStatus = "Lookup time for popular domains after cache warm-up (second round). Faster DNS speeds up connection setup, not in-game ping. " +
                        "To change DNS: Settings › Network › your adapter › DNS server assignment.";
            StatusMessage = "";
        });
    }

    [RelayCommand]
    private async Task Trace()
    {
        Hops.Clear();
        await RunBusy($"Tracing route to {TraceTarget}…", async () =>
        {
            var progress = new Progress<TraceHop>(h => Hops.Add(h));
            await _net.TracerouteAsync(TraceTarget, 30, progress);
            StatusMessage = "";
        });
    }

    [RelayCommand]
    private async Task Bufferbloat()
    {
        if (!_dialogs.Confirm("Run bufferbloat test?",
                $"The test measures idle latency to {Target} for 10 s, then downloads up to ~1 GB from\n{_settings.Current.BufferbloatDownloadUrl}\nfor ~10 s while measuring latency again. " +
                "Avoid running it on metered connections. Other devices on the network affect the result.", "Run test")) return;
        await RunBusy("Running bufferbloat test (≈25 s)…", async () =>
        {
            var progress = new Progress<string>(m => StatusMessage = m);
            var r = await _net.BufferbloatTestAsync(Target, _settings.Current.BufferbloatDownloadUrl, 10, progress);
            BloatGrade = r.Grade;
            BloatResult = $"Idle median {r.Idle.MedianMs:0.0} ms → under download load {r.Loaded.MedianMs:0.0} ms (+{r.LatencyIncreaseMs:0.0} ms) · download {r.DownloadMbps:0} Mb/s · " +
                          $"loaded jitter {r.Loaded.JitterMs:0.0} ms, loss {r.Loaded.LossPercent:0.#}%.\n{r.Interpretation}" + (r.Error is null ? "" : $"\n⚠ {r.Error}");
            StatusMessage = "";
        });
    }

    [RelayCommand] private void OpenNetworkSettings() => Process.Start(new ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true });
    [RelayCommand] private void OpenResourceMonitor() => Process.Start(new ProcessStartInfo("resmon.exe") { UseShellExecute = true });

    [RelayCommand]
    private void ClearSaved()
    {
        Saved.Clear();
        PersistSaved();
        UpdateComparison();
    }

    private void LoadSaved()
    {
        try
        {
            foreach (var s in JsonDefaults.ReadFile<List<SavedPingResult>>(HistoryFile) ?? new()) Saved.Add(s);
        }
        catch { }
        UpdateComparison();
    }

    private void PersistSaved()
    {
        try { JsonDefaults.WriteFile(HistoryFile, Saved.Take(100).ToList()); } catch { }
    }
}
