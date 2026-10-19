using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimatePCOptimizerPro.Converters;
using UltimatePCOptimizerPro.Core;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

public sealed partial class PcSpecsViewModel : PageViewModel
{
    private readonly SystemState _state;
    private readonly DialogService _dialogs;
    private readonly IServiceProvider _services;

    public override string Title => "My PC Specs";
    public override string Subtitle => "Every value below was read from this PC. Sensors Windows does not expose are labelled as unavailable.";

    public ObservableCollection<InfoSection> LeftSections { get; } = new();
    public ObservableCollection<InfoSection> RightSections { get; } = new();
    [ObservableProperty] private string _scannedAt = "";

    public PcSpecsViewModel(SystemState state, DialogService dialogs, IServiceProvider services)
    {
        _state = state;
        _dialogs = dialogs;
        _services = services;
        _state.SnapshotChanged += Build;
    }

    protected override Task LoadAsync()
    {
        Build();
        return _state.Snapshot is null ? _state.ScanAsync() : Task.CompletedTask;
    }

    private static string NA(string what = "") => "Not available" + (what.Length > 0 ? $" ({what})" : "");
    private static string GB(ulong b) => BytesConverter.Format(b);

    public static List<InfoSection> BuildSections(SystemSnapshot s)
    {
        var list = new List<InfoSection>();
        var c = s.Cpu;
        list.Add(new InfoSection
        {
            Title = "Processor", Icon = "", Rows =
            {
                new("Model", c.Name), new("Manufacturer", c.Manufacturer), new("Generation / series", c.GenerationHint ?? NA("model string not recognised"), c.GenerationHint is null),
                new("Architecture", c.Architecture), new("Cores / threads", $"{c.PhysicalCores} cores / {c.LogicalProcessors} threads" + (c.SmtActive ? " (SMT/Hyper-Threading on)" : "")),
                new("Hybrid design", c.IsHybrid ? $"Yes — {c.PerformanceCores} performance + {c.EfficiencyCores} efficiency cores" : "No (all cores same class)"),
                new("Base clock (reported)", c.BaseClockMHz is { } mhz ? $"{mhz} MHz" : NA(), c.BaseClockMHz is null),
                new("L2 / L3 cache", $"{(c.L2CacheKB is { } l2 ? $"{l2 / 1024.0:0.#} MB" : "?")} / {(c.L3CacheKB is { } l3 ? $"{l3 / 1024.0:0.#} MB" : "?")}"),
                new("Socket", c.Socket ?? NA(), c.Socket is null),
                new("Virtualization (firmware)", c.VirtualizationFirmwareEnabled switch { true => "Enabled", false => "Disabled", _ => NA() }),
                new("Instruction sets", string.Join(", ", c.InstructionSets)),
                new("Temperature", NA("Windows exposes no standard CPU temperature API; vendor drivers required"), true)
            }
        });
        foreach (var g in s.Gpus)
        {
            list.Add(new InfoSection
            {
                Title = s.Gpus.Count > 1 ? $"Graphics — adapter {g.AdapterIndex}" : "Graphics", Icon = "", Rows =
                {
                    new("Model", g.Name), new("Vendor", g.Vendor.ToString()), new("Type", g.IsIntegrated ? "Integrated" : "Dedicated"),
                    new("Dedicated video memory", GB(g.DedicatedVideoMemoryBytes)), new("Shared system memory", GB(g.SharedSystemMemoryBytes)),
                    new("Driver version", g.DriverVersion ?? NA(), g.DriverVersion is null), new("Driver date", g.DriverDate?.ToString("yyyy-MM-dd") ?? NA(), g.DriverDate is null),
                    new("Max core clock", g.MaxCoreClockMHz is { } maxClock ? $"{maxClock:0} MHz (reported by driver)" : NA("driver does not report"), g.MaxCoreClockMHz is null),
                    new("Current mode", g.CurrentVideoMode ?? NA(), g.CurrentVideoMode is null),
                    new("Primary rendering GPU", g == s.PrimaryGpu ? "Yes (highest-capability adapter; per-app choice in Settings › Graphics)" : "No")
                }
            });
        }
        var m = s.Memory;
        var mem = new InfoSection
        {
            Title = "Memory", Icon = "", Rows =
            {
                new("Installed", $"{m.TotalGB:0.#} GB"), new("Available at scan", GB(m.AvailableBytes)), new("In use at scan", $"{m.UsedPercent:0}%"),
                new("Committed / limit", $"{GB(m.CommitTotalBytes)} / {GB(m.CommitLimitBytes)}"),
                new("Type", m.PrimaryType ?? NA(), m.PrimaryType is null), new("Speed", m.ReportedSpeedMTs is { } sp ? $"{sp} MT/s (configured)" : NA(), m.ReportedSpeedMTs is null),
                new("Modules / slots", $"{m.Modules.Count} installed" + (m.TotalSlots is { } ts ? $" of {ts} slots" : "")),
                new("Channel layout", m.ChannelEstimate, m.ChannelEstimate.StartsWith("Unknown"))
            }
        };
        foreach (var mod in m.Modules)
            mem.Rows.Add(new($"  {mod.DeviceLocator ?? "Module"}", $"{GB(mod.CapacityBytes)} {mod.MemoryType} {mod.ConfiguredSpeedMTs ?? mod.SpeedMTs} MT/s · {mod.Manufacturer} {mod.PartNumber}".Trim()));
        list.Add(mem);

        var st = new InfoSection { Title = "Storage", Icon = "" };
        foreach (var d in s.Storage.Disks)
            st.Rows.Add(new(d.Name + (d.IsSystemDisk ? "  (Windows)" : ""), $"{d.KindDisplay} · {d.BusType} · {GB(d.SizeBytes)} · Health: {d.Health}" + (d.DriveLetters.Count > 0 ? $" · {string.Join(", ", d.DriveLetters.Select(l => l + ":"))}" : "")));
        foreach (var v in s.Storage.Volumes)
            st.Rows.Add(new($"Volume {v.Name}", $"{v.FileSystem} · {GB(v.FreeBytes)} free of {GB(v.TotalBytes)} ({v.FreePercent:0}%)", Tone: v.FreePercent < 10 ? "Warning" : null));
        st.Rows.Add(new("TRIM", s.Storage.TrimEnabled switch { true => "Enabled", false => "Disabled", _ => NA() }));
        st.Rows.Add(new("SMART temperature / wear", NA("requires administrator rights and vendor support"), true));
        list.Add(st);

        var b = s.Board;
        list.Add(new InfoSection
        {
            Title = "Motherboard & firmware", Icon = "", Rows =
            {
                new("System", $"{b.SystemManufacturer} {b.SystemModel}".Trim()), new("Motherboard", $"{b.BoardManufacturer} {b.BoardProduct}".Trim()),
                new("BIOS/UEFI vendor", b.BiosVendor ?? NA(), b.BiosVendor is null), new("Firmware version", b.BiosVersion ?? NA(), b.BiosVersion is null),
                new("Firmware date", b.BiosReleaseDate?.ToString("yyyy-MM-dd") ?? NA(), b.BiosReleaseDate is null), new("Boot mode", b.BootMode),
                new("Secure Boot", b.SecureBootEnabled switch { true => "On", false => "Off", _ => NA() }),
                new("Form factor", (b.IsLaptop ? "Laptop / portable" : "Desktop") + (b.ChassisDescription is { } cd ? $" ({cd})" : ""))
            }
        });

        var disp = new InfoSection { Title = "Displays", Icon = "" };
        foreach (var d in s.Displays)
        {
            disp.Rows.Add(new(d.FriendlyName + (d.IsPrimary ? "  (primary)" : ""), $"{d.Width}×{d.Height} @ {d.RefreshHz} Hz · {d.BitsPerPixel}-bit · on {d.AdapterName}",
                Tone: d.RunningBelowMaxRefresh ? "Warning" : null));
            disp.Rows.Add(new("  Supported refresh rates", string.Join(", ", d.SupportedRefreshRates.Select(r => r + " Hz")) + (d.RunningBelowMaxRefresh ? $"  ⚠ running below {d.MaxRefreshAtCurrentResolution} Hz" : "")));
            disp.Rows.Add(new("  Variable refresh rate", d.VariableRefreshStatus, true));
        }
        if (s.Displays.Count > 1) disp.Rows.Add(new("Configuration", $"{s.Displays.Count} monitors"));
        list.Add(disp);

        var net = new InfoSection { Title = "Network", Icon = "" };
        foreach (var a in s.Network.Adapters)
        {
            net.Rows.Add(new(a.Description + (a.IsActive ? "  (active)" : ""), $"{(a.IsWireless ? "Wi-Fi" : a.IsEthernet ? "Ethernet" : a.InterfaceType)} · {a.LinkSpeedDisplay}" +
                (a.WifiSignalPercent is { } sig ? $" · signal {sig}%" : "") + (a.WifiRadioType is { } rt ? $" · {rt}" : "")));
            net.Rows.Add(new("  Driver", a.DriverVersion is null ? NA() : $"{a.DriverVersion} ({a.DriverDate}) {a.DriverProvider}", a.DriverVersion is null));
            net.Rows.Add(new("  IPv4 / gateway", $"{string.Join(", ", a.IPv4Addresses)} / {string.Join(", ", a.Gateways)}"));
            net.Rows.Add(new("  DNS servers", string.Join(", ", a.DnsServers) + (a.DhcpEnabled == true ? " (DHCP)" : "")));
        }
        net.Rows.Add(new("Ping / jitter / packet loss", "Measured on the Network Optimizer page (not part of the hardware scan)", true));
        list.Add(net);

        var o = s.Os;
        list.Add(new InfoSection
        {
            Title = "Operating system", Icon = "", Rows =
            {
                new("Edition", $"{o.ProductName} ({o.EditionId})"), new("Version", o.DisplayVersion ?? NA()), new("Build", o.BuildDisplay), new("Architecture", o.Architecture),
                new("Uptime", $"{(int)o.Uptime.TotalDays} d {o.Uptime.Hours} h {o.Uptime.Minutes} min"), new("Power plan", s.Power.ActivePlanName),
                new("Power source", s.Power.HasBattery ? (s.Power.OnAcPower ? "Charger connected" : "On battery") + (s.Power.BatteryPercent is { } bp ? $" · {bp}%" : "") : "Mains (no battery)"),
                new("Running as", o.IsElevated ? "Administrator" : "Standard user"), new("Processes / running services", $"{o.ProcessCount} / {o.RunningServiceCount}"),
                new("Enabled startup entries", o.StartupEntryCount.ToString())
            }
        });
        list.Add(new InfoSection
        {
            Title = "Gaming & security settings", Icon = "", Rows =
            {
                new("Game Mode", o.GameModeEnabled == true ? "On" : "Off"), new("Game Bar capture", o.GameDvrEnabled switch { true => "On", false => "Off", _ => "Windows default" }),
                new("Background recording", o.BackgroundRecordingEnabled == true ? "On" : "Off", Tone: o.BackgroundRecordingEnabled == true ? "Warning" : null),
                new("Hardware-accelerated GPU scheduling", o.HagsState), new("Virtualization-based security", o.VbsStatus),
                new("Memory Integrity (HVCI)", o.MemoryIntegrityRunning switch { true => "Running", false => "Not running", _ => NA() }),
                new("Antivirus", o.AntivirusProducts.Count > 0 ? string.Join(", ", o.AntivirusProducts) : NA())
            }
        });
        if (s.RelevantSoftware.Count > 0)
        {
            var sw = new InfoSection { Title = "Installed software relevant to gaming", Icon = "" };
            foreach (var e in s.RelevantSoftware) sw.Rows.Add(new($"{e.Name} {e.Version}".Trim(), $"{e.Category}{(e.Note.Length > 0 ? " — " + e.Note : "")}"));
            list.Add(sw);
        }
        if (s.Warnings.Count > 0)
        {
            var w = new InfoSection { Title = "Scan warnings", Icon = "" };
            foreach (var x in s.Warnings) w.Rows.Add(new(x.Area, x.Message, true));
            list.Add(w);
        }
        return list;
    }

    private void Build()
    {
        var s = _state.Snapshot;
        if (s is null) return;
        LeftSections.Clear();
        RightSections.Clear();
        int left = 0, right = 0;
        foreach (var sec in BuildSections(s))
        {
            // Greedy balancing by row count keeps both columns similar in height.
            if (left <= right) { LeftSections.Add(sec); left += sec.Rows.Count + 3; }
            else { RightSections.Add(sec); right += sec.Rows.Count + 3; }
        }
        ScannedAt = $"Scanned {s.ScannedAt:yyyy-MM-dd HH:mm:ss} in {s.ScanDuration.TotalSeconds:0.0} s";
    }

    public static string ToText(SystemSnapshot s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ULTIMATE PC OPTIMIZER PRO — PC specifications ({s.ScannedAt:yyyy-MM-dd HH:mm:ss zzz})");
        foreach (var sec in BuildSections(s))
        {
            sb.AppendLine().AppendLine($"== {sec.Title} ==");
            foreach (var r in sec.Rows) sb.AppendLine($"{r.Label}: {r.Value}");
        }
        return sb.ToString();
    }

    [RelayCommand]
    private void Copy()
    {
        if (_state.Snapshot is null) return;
        Clipboard.SetText(ToText(_state.Snapshot));
        StatusMessage = "Specifications copied to the clipboard.";
    }

    [RelayCommand]
    private void ExportJson()
    {
        if (_state.Snapshot is null) return;
        var path = _dialogs.SaveFile("Export specs", "JSON (*.json)|*.json", $"pc-specs-{DateTime.Now:yyyyMMdd-HHmm}.json");
        if (path is null) return;
        File.WriteAllText(path, JsonSerializer.Serialize(_state.Snapshot, JsonDefaults.Indented));
        StatusMessage = "Exported to " + path;
    }

    [RelayCommand]
    private void ExportReport()
    {
        if (_state.Snapshot is null) return;
        var path = _dialogs.SaveFile("Export system report", "Text report (*.txt)|*.txt", $"system-report-{DateTime.Now:yyyyMMdd-HHmm}.txt");
        if (path is null) return;
        var sb = new StringBuilder(ToText(_state.Snapshot));
        if (_state.Tier is { } t)
        {
            sb.AppendLine().AppendLine("== Hardware tier ==").AppendLine($"{t.TierDisplay}, score {t.OverallScore:0.#}");
            foreach (var sc in t.Scores) sb.AppendLine($"{sc.Component}: {sc.Score:0.#} ({sc.Basis})");
            sb.AppendLine().AppendLine("== Bottlenecks ==");
            foreach (var bn in t.Bottlenecks) sb.AppendLine($"[{bn.Severity}] {bn.Title} — {bn.Recommendation}");
        }
        File.WriteAllText(path, sb.ToString());
        StatusMessage = "Exported to " + path;
    }

    [RelayCommand] private async Task Refresh() => await _state.ScanAsync();
    [RelayCommand] private void Analyze() => ((MainViewModel)_services.GetService(typeof(MainViewModel))!).NavigateTo<TierAnalysisViewModel>();
    [RelayCommand] private void Recommend() => ((MainViewModel)_services.GetService(typeof(MainViewModel))!).NavigateTo<SmartOptimizeViewModel>();
}

public sealed partial class ScannerViewModel : PageViewModel
{
    private readonly SystemState _state;
    public override string Title => "Full PC Scanner";
    public override string Subtitle => "Hardware discovery, configuration analysis and bottleneck detection (read-only — nothing is changed)";

    public SystemState State => _state;
    public ObservableCollection<string> Log { get; } = new();
    public ObservableCollection<Bottleneck> Findings { get; } = new();
    [ObservableProperty] private string _summary = "Press Start scan to analyse this PC.";

    public ScannerViewModel(SystemState state)
    {
        _state = state;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemState.ScanStep) && _state.IsScanning)
                Application.Current.Dispatcher.InvokeAsync(() => Log.Add($"{DateTime.Now:HH:mm:ss}  Scanning {_state.ScanStep}…"));
        };
        _state.SnapshotChanged += Fill;
    }

    protected override Task LoadAsync()
    {
        Fill();
        return Task.CompletedTask;
    }

    private void Fill()
    {
        var s = _state.Snapshot;
        if (s is null) return;
        _state.Reclassify();
        Findings.Clear();
        foreach (var b in _state.Tier?.Bottlenecks ?? new()) Findings.Add(b);
        Summary = $"Last scan {s.ScannedAt:HH:mm:ss}: {s.Gpus.Count} GPU(s), {s.Storage.Disks.Count} disk(s), {s.Displays.Count} display(s), {s.Network.Adapters.Count} active adapter(s). " +
                  $"{Findings.Count} finding(s), {s.Warnings.Count} warning(s). Duration {s.ScanDuration.TotalSeconds:0.0} s.";
    }

    [RelayCommand]
    private async Task Scan()
    {
        Log.Clear();
        Log.Add($"{DateTime.Now:HH:mm:ss}  Scan started");
        var sw = Stopwatch.StartNew();
        try
        {
            var s = await _state.ScanAsync();
            foreach (var w in s.Warnings) Log.Add($"{DateTime.Now:HH:mm:ss}  ⚠ {w.Area}: {w.Message}");
            Log.Add($"{DateTime.Now:HH:mm:ss}  Scan complete in {sw.Elapsed.TotalSeconds:0.0} s");
        }
        catch (Exception ex) { Log.Add("Scan failed: " + ex.Message); }
    }
}
