using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Converters;
using UltimatePCOptimizerPro.Core.Engine;
using UltimatePCOptimizerPro.Core.Engine.Handlers;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using UltimatePCOptimizerPro.Services;

namespace UltimatePCOptimizerPro.ViewModels;

/// <summary>Base for module pages that show live information plus an embedded tweak library.</summary>
public abstract partial class ModulePageViewModel : PageViewModel
{
    protected readonly SystemState State;
    public TweakListViewModel Tweaks { get; }
    public ObservableCollection<InfoRow> Info { get; } = new();
    public ObservableCollection<string> Guidance { get; } = new();

    protected ModulePageViewModel(SystemState state, TweakListViewModel tweaks)
    {
        State = state;
        Tweaks = tweaks;
        State.SnapshotChanged += () => BuildInfo();
        State.SystemChanged += () => BuildInfo();
    }

    protected override async Task LoadAsync()
    {
        if (State.Snapshot is null) await State.ScanAsync();
        BuildInfo();
        await Tweaks.RefreshAsync();
    }

    protected override async Task OnRevisitedAsync()
    {
        BuildInfo();
        await Tweaks.RefreshAsync();
    }

    protected abstract void BuildInfo();

    protected static string? ReadString(RegistryKey root, string path, string name)
    {
        try { using var k = root.OpenSubKey(path); return k?.GetValue(name)?.ToString(); }
        catch { return null; }
    }

    protected void SetInfo(IEnumerable<InfoRow> rows)
    {
        Info.Clear();
        foreach (var r in rows) Info.Add(r);
    }

    protected void SetGuidance(IEnumerable<string> lines)
    {
        Guidance.Clear();
        foreach (var l in lines) Guidance.Add(l);
    }
}

public sealed partial class DisplayRefreshItem : ObservableObject
{
    public DisplayInfo Display { get; init; } = null!;
    public string Name => $"{Display.FriendlyName}{(Display.IsPrimary ? " (primary)" : "")}";
    public string Current => $"{Display.Width}×{Display.Height} @ {Display.RefreshHz} Hz";
    public string Supported => string.Join(", ", Display.SupportedRefreshRates.Select(r => $"{r}"));
    public bool CanRaise => Display.RunningBelowMaxRefresh;
    public string Status => CanRaise ? $"Running below the maximum {Display.MaxRefreshAtCurrentResolution} Hz" : "At maximum refresh rate";
}

public sealed partial class FpsBoosterViewModel : ModulePageViewModel
{
    private readonly OperationRunner _runner;
    public override string Title => "FPS Booster";
    public override string Subtitle => "Gaming configuration that affects FPS, frame-time stability and latency — each change explains what it really does";

    public ObservableCollection<DisplayRefreshItem> Displays { get; } = new();

    public FpsBoosterViewModel(SystemState state, TweakListViewModel tweaks, OperationRunner runner) : base(state, tweaks)
    {
        _runner = runner;
        tweaks.Configure("Gaming Optimization", t => t.Category is TweakCategory.Gaming or TweakCategory.Gpu or TweakCategory.Power, showFilters: false);
        SetGuidance(new[]
        {
            "FPS vs frame time: average FPS can rise while 1% lows (stutter) get worse. Measure both in the Benchmark Center before and after changes.",
            "GPU-bound (GPU ≈ 95–100%): lower resolution, upscaling (DLSS/FSR/XeSS Quality), shadows and effects. System tweaks barely help here.",
            "CPU-bound (GPU well below 90%, one CPU thread near 100%): reduce background load, lower view distance/crowd/physics settings, use a frame cap.",
            "Frame pacing: a frame cap slightly below what your PC sustains gives more even frame times than an uncapped, fluctuating frame rate.",
            "V-Sync adds latency when the frame rate exceeds the refresh rate. With VRR (G-SYNC/FreeSync) enable V-Sync in the driver and cap FPS ~3 below the refresh rate.",
            "NVIDIA Reflex / AMD Anti-Lag 2 in supported games reduce render queue latency more than any registry setting.",
            "Fullscreen optimizations: Microsoft designed flip-model 'fullscreen optimized' presentation to match exclusive-fullscreen latency. Disable it per game (Game Profiles) only if that game stutters or alt-tabs badly, and benchmark the result."
        });
    }

    protected override void BuildInfo()
    {
        var s = State.Snapshot;
        if (s is null) return;
        var dx = ReadString(Registry.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings");
        string Token(string t) => RegistryTokenHandler.GetToken(dx, t) switch { "1" => "On", "0" => "Off", _ => "Windows default" };
        SetInfo(new[]
        {
            new InfoRow("Game Mode", s.Os.GameModeEnabled == true ? "On" : "Off", Tone: s.Os.GameModeEnabled == true ? "Success" : "Warning"),
            new InfoRow("Background recording", s.Os.BackgroundRecordingEnabled == true ? "On (costs GPU encoder + disk)" : "Off", Tone: s.Os.BackgroundRecordingEnabled == true ? "Warning" : "Success"),
            new InfoRow("Game Bar capture", s.Os.GameDvrEnabled switch { true => "On", false => "Off", _ => "Windows default (on)" }),
            new InfoRow("Hardware-accelerated GPU scheduling", s.Os.HagsState),
            new InfoRow("Optimizations for windowed games", s.Os.Build >= 22621 ? Token("SwapEffectUpgradeEnable") : "Requires Windows 11 22H2"),
            new InfoRow("Variable refresh rate (Windows)", Token("VRROptimizeEnable")),
            new InfoRow("Power plan", s.Power.ActivePlanName),
            new InfoRow("Likely limiter", State.Tier?.LikelyLimiter ?? "Unknown")
        });
        Displays.Clear();
        foreach (var d in s.Displays) Displays.Add(new DisplayRefreshItem { Display = d });
    }

    [RelayCommand]
    private async Task RaiseRefresh(DisplayRefreshItem? item)
    {
        if (item is null || !item.CanRaise) return;
        var d = item.Display;
        await _runner.ApplyAsync("Display Refresh Rate", new[]
        {
            new PlannedChange
            {
                Title = $"{d.FriendlyName}: {d.RefreshHz} Hz → {d.MaxRefreshAtCurrentResolution} Hz", TweakId = "display.refresh.max", Category = "Display", Risk = RiskLevel.Low,
                Spec = new DisplayModeSpec { DeviceName = d.DeviceName, Width = d.Width, Height = d.Height, RefreshHz = d.MaxRefreshAtCurrentResolution }
            }
        });
        await State.ScanAsync();
    }

    [RelayCommand] private void OpenGraphicsSettings() => Process.Start(new ProcessStartInfo("ms-settings:display-advancedgraphics") { UseShellExecute = true });
    [RelayCommand] private void OpenGamingSettings() => Process.Start(new ProcessStartInfo("ms-settings:gaming-gamemode") { UseShellExecute = true });
}

public sealed record CoreItem(string Label, string Kind, int Threads, bool IsEfficiency);

public sealed partial class CpuViewModel : ModulePageViewModel
{
    private readonly OperationRunner _runner;
    private readonly Queue<double> _hist = new(), _clock = new();
    public override string Title => "CPU Tweaks";
    public override string Subtitle => "Topology, live clocks and limits, power plans and scheduling-related settings";

    public ObservableCollection<CoreItem> Cores { get; } = new();
    public ObservableCollection<PowerSchemes.SchemeInfo> PowerPlans { get; } = new();
    [ObservableProperty] private double[] _usageHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _clockHistory = Array.Empty<double>();
    [ObservableProperty] private string _liveText = "";
    [ObservableProperty] private string _throttleText = "";
    [ObservableProperty] private string _throttleTone = "Success";

    public CpuViewModel(SystemState state, TweakListViewModel tweaks, OperationRunner runner) : base(state, tweaks)
    {
        _runner = runner;
        tweaks.Configure("CPU Tweaks", t => t.Category is TweakCategory.Cpu or TweakCategory.Power, showFilters: false);
        state.SampleReceived += OnSample;
        SetGuidance(new[]
        {
            "This app never forces Realtime priority, disables cores, or edits undocumented scheduler values — these can cause input lag, audio dropouts and instability.",
            "Hybrid CPUs (P-cores + E-cores): Windows 11's scheduler and Intel Thread Director place games on P-cores automatically; keep the game in the foreground and Game Mode on.",
            "AMD dual-CCD X3D CPUs: keep the AMD chipset driver and 'AMD 3D V-Cache Performance Optimizer' installed, and Game Mode on, so games are parked on the cache CCD.",
            "A '% Performance Limit' below 100 while gaming means power/thermal limits are lowering clocks — check cooling and firmware power limits.",
            "Per-game priority (Above normal/High) is available on the Game Profiles page and is applied by Windows at game launch."
        });
    }

    private void OnSample(MonitorSample s)
    {
        _hist.Enqueue(s.CpuPercent); while (_hist.Count > 60) _hist.Dequeue();
        UsageHistory = _hist.ToArray();
        if (s.CpuEffectiveMHz is { } mhz)
        {
            _clock.Enqueue(mhz); while (_clock.Count > 60) _clock.Dequeue();
            ClockHistory = _clock.ToArray();
        }
        LiveText = $"Usage {s.CpuPercent:0}% · effective clock {(s.CpuEffectiveMHz is { } m ? $"{m:0} MHz" : "n/a")} · performance {(s.CpuPerformancePercent is { } p ? $"{p:0}%" : "n/a")} of base";
        if (s.CpuPerformanceLimitPercent is { } lim)
        {
            ThrottleText = lim >= 99 ? "No active performance limit (100%)." : $"Performance limited to {lim:0}% by power/thermal/platform limits.";
            ThrottleTone = lim >= 99 ? "Success" : lim >= 85 ? "Warning" : "Danger";
        }
        else ThrottleText = "Performance-limit counter not available on this system.";
    }

    protected override void BuildInfo()
    {
        var s = State.Snapshot;
        if (s is null) return;
        var c = s.Cpu;
        SetInfo(new[]
        {
            new InfoRow("Processor", c.Name), new InfoRow("Topology", $"{c.PhysicalCores} cores / {c.LogicalProcessors} threads" + (c.IsHybrid ? $" · {c.PerformanceCores} P + {c.EfficiencyCores} E" : "")),
            new InfoRow("Base clock", c.BaseClockMHz is { } b ? $"{b} MHz" : "n/a"), new InfoRow("Generation", c.GenerationHint ?? "Not identified"),
            new InfoRow("CPU score (tier)", State.Tier?.Scores.FirstOrDefault(x => x.Component == "CPU") is { } sc ? $"{sc.Score:0} — {sc.Basis}" : "–"),
            new InfoRow("Temperature", "Not exposed by Windows without vendor drivers", true)
        });
        Cores.Clear();
        int maxClass = c.Cores.Count == 0 ? 0 : c.Cores.Max(x => x.EfficiencyClass);
        foreach (var core in c.Cores)
        {
            bool eff = c.IsHybrid && core.EfficiencyClass < maxClass;
            Cores.Add(new CoreItem($"Core {core.Index}", (c.IsHybrid ? (eff ? "E" : "P") : "C") + core.Index, core.LogicalProcessorCount, eff));
        }
        PowerPlans.Clear();
        try { foreach (var p in PowerSchemes.List()) PowerPlans.Add(p); } catch { }
    }

    [RelayCommand]
    private async Task ActivatePlan(PowerSchemes.SchemeInfo? plan)
    {
        if (plan is null || plan.IsActive) return;
        await _runner.ApplyAsync("Power Plan", new[]
        {
            new PlannedChange
            {
                Title = $"Activate power plan '{plan.Name}'", TweakId = "power.plan.custom", Category = "Power", Risk = RiskLevel.Low,
                Spec = new PowerSchemeSpec { Scheme = PowerSchemeKind.Custom, CustomGuid = plan.Id }
            }
        });
        BuildInfo();
    }

    [RelayCommand] private void OpenPowerOptions() => Process.Start(new ProcessStartInfo("control.exe", "powercfg.cpl") { UseShellExecute = true });
}

public sealed partial class GpuViewModel : ModulePageViewModel
{
    private readonly Queue<double> _util = new(), _vram = new(), _temp = new();
    public override string Title => "GPU Tweaks";
    public override string Subtitle => "Graphics hardware, live utilisation and supported Windows graphics settings (no driver hacks)";

    public ObservableCollection<InfoSection> Gpus { get; } = new();
    [ObservableProperty] private double[] _utilHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _vramHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _tempHistory = Array.Empty<double>();
    [ObservableProperty] private string _liveText = "";

    public GpuViewModel(SystemState state, TweakListViewModel tweaks) : base(state, tweaks)
    {
        tweaks.Configure("GPU Tweaks", t => t.Category == TweakCategory.Gpu || t.Id is "gaming.vrr.enable" or "gaming.windowed.optimizations", showFilters: false);
        state.SampleReceived += OnSample;
    }

    private static double[] Push(Queue<double> q, double v) { q.Enqueue(v); while (q.Count > 60) q.Dequeue(); return q.ToArray(); }

    private void OnSample(MonitorSample s)
    {
        if (s.GpuPercent is { } u) UtilHistory = Push(_util, u);
        if (s.GpuDedicatedUsedBytes is { } v) VramHistory = Push(_vram, v / 1024d / 1024 / 1024);
        if (s.GpuTemperatureC is { } t) TempHistory = Push(_temp, t);
        LiveText = $"Utilisation {(s.GpuPercent is { } g ? $"{g:0}%" : "n/a")} · VRAM in use {(s.GpuDedicatedUsedBytes is { } vb ? BytesConverter.Format(vb) : "n/a")} · " +
                   $"temperature {(s.GpuTemperatureC is { } tc ? $"{tc:0.0} °C ({s.GpuTemperatureSource})" : "not reported by driver")} · clock {(s.GpuCoreClockMHz is { } c ? $"{c:0} MHz" : "n/a")}";
    }

    protected override void BuildInfo()
    {
        var s = State.Snapshot;
        if (s is null) return;
        Gpus.Clear();
        foreach (var sec in PcSpecsViewModel.BuildSections(s).Where(x => x.Title.StartsWith("Graphics"))) Gpus.Add(sec);
        SetInfo(new[]
        {
            new InfoRow("Hardware-accelerated GPU scheduling", s.Os.HagsState),
            new InfoRow("Hybrid graphics", s.IsHybridGraphics ? "Yes — set per-game GPU preference on the Game Profiles page" : "No"),
            new InfoRow("GPU score (tier)", State.Tier?.Scores.FirstOrDefault(x => x.Component == "GPU") is { } sc ? $"{sc.Score:0} — {sc.Basis}" : "–")
        });
        var vendor = s.PrimaryGpu?.Vendor ?? GpuVendor.Unknown;
        var lines = new List<string>
        {
            "GPU-bound (utilisation ≈ 95–100%): lower resolution scale or use DLSS/FSR/XeSS; system tweaks cannot add GPU performance.",
            "Keep VRAM use below capacity — exceeding it causes severe stutter as textures stream over PCIe. Lower texture quality first.",
            "Driver settings are changed only through the vendor's own control panel; this app does not write undocumented driver registry values."
        };
        if (vendor == GpuVendor.Nvidia) lines.AddRange(new[]
        {
            "NVIDIA: use 'NVIDIA Reflex' in games that support it. In NVIDIA Control Panel, 'Low Latency Mode: On' helps only in games without Reflex.",
            "NVIDIA: with G-SYNC, enable G-SYNC + driver V-Sync and cap FPS ~3 below refresh (Reflex does this automatically)."
        });
        if (vendor == GpuVendor.Amd) lines.AddRange(new[]
        {
            "AMD: enable Anti-Lag (or Anti-Lag 2 in supported games) in AMD Software; use FreeSync with an FPS cap ~3 below refresh.",
            "AMD: Radeon Chill or FRTC can cap frame rate for lower power and stable frame times."
        });
        if (vendor == GpuVendor.Intel) lines.Add("Intel: keep Arc drivers current — DX11/older titles improved substantially in driver updates. Use XeSS where available.");
        SetGuidance(lines);
    }

    [RelayCommand] private void OpenGraphicsSettings() => Process.Start(new ProcessStartInfo("ms-settings:display-advancedgraphics") { UseShellExecute = true });
}

public sealed partial class RamViewModel : ModulePageViewModel
{
    private readonly ProcessMonitorService _processes;
    private readonly Queue<double> _ram = new();
    public override string Title => "RAM Tweaks";
    public override string Subtitle => "Memory pressure, paging and memory-heavy processes. No fake 'RAM boosters' or standby-list clearing.";

    public ObservableCollection<ProcessUsage> TopProcesses { get; } = new();
    public ObservableCollection<string> Recommendations { get; } = new();
    [ObservableProperty] private double[] _ramHistory = Array.Empty<double>();
    [ObservableProperty] private string _liveText = "";

    public RamViewModel(SystemState state, TweakListViewModel tweaks, ProcessMonitorService processes) : base(state, tweaks)
    {
        _processes = processes;
        tweaks.Configure("RAM Tweaks", t => t.Category == TweakCategory.Memory || t.Id is "explorer.widgets.disable" or "explorer.newsinterests.win10.disable", showFilters: false);
        state.SampleReceived += s =>
        {
            _ram.Enqueue(s.RamUsedPercent); while (_ram.Count > 60) _ram.Dequeue();
            RamHistory = _ram.ToArray();
            LiveText = $"In use {s.RamUsedPercent:0}% · available {BytesConverter.Format(s.RamAvailableBytes)} · committed {BytesConverter.Format(s.CommitBytes)} of {BytesConverter.Format(s.CommitLimitBytes)} · hard faults {s.PagesInputPerSec:0}/s";
        };
        SetGuidance(new[]
        {
            "Windows uses free RAM as file cache (standby list); 'free' memory is not wasted, and clearing it makes games reload data from disk.",
            "Only committed memory approaching the commit limit, or sustained hard faults while RAM is full, indicate a real shortage.",
            "Keep the page file enabled and system-managed: some games crash without one even on 32 GB systems.",
            "Two matched modules (dual-channel) and the XMP/EXPO profile enabled in firmware matter more for CPU-bound games than any Windows setting."
        });
    }

    protected override async Task LoadAsync()
    {
        await base.LoadAsync();
        await RefreshProcesses();
    }

    protected override void BuildInfo()
    {
        var s = State.Snapshot;
        if (s is null) return;
        var m = s.Memory;
        SetInfo(PcSpecsViewModel.BuildSections(s).First(x => x.Title == "Memory").Rows);
        Recommendations.Clear();
        if (m.TotalGB < 7.5) Recommendations.Add($"Upgrade: {m.TotalGB:0} GB is below the 8 GB minimum of most current games — 16 GB (2×8 GB) is the most effective upgrade.");
        else if (m.TotalGB < 15) Recommendations.Add("Upgrade suggestion: 16–32 GB avoids paging when a browser, Discord and launchers run alongside games.");
        if (m.Modules.Count == 1 && !(m.PrimaryType?.StartsWith("LP") ?? false)) Recommendations.Add("Add a matching second module to enable dual-channel mode (roughly doubles memory bandwidth).");
        if (m.Modules.Select(x => x.CapacityBytes).Distinct().Count() > 1) Recommendations.Add("Mixed module sizes can run in flex mode with partially single-channel bandwidth.");
        if (m.Modules.Any(x => x.SpeedMTs is { } sp && x.ConfiguredSpeedMTs is { } cs && cs < sp)) Recommendations.Add("Modules run below their rated speed — enable the XMP/EXPO profile in firmware (not changed by this app).");
        if (Recommendations.Count == 0) Recommendations.Add("Memory configuration looks appropriate for gaming.");
    }

    [RelayCommand]
    private async Task RefreshProcesses()
    {
        var list = await _processes.SampleAsync(800);
        TopProcesses.Clear();
        foreach (var p in list.OrderByDescending(p => p.PrivateBytes).Take(15)) TopProcesses.Add(p);
    }
}

public sealed partial class WindowsTweaksViewModel : ModulePageViewModel
{
    public override string Title => "Windows Tweaks";
    public override string Subtitle => "Explorer, visual effects, privacy, suggestions and startup behaviour";

    public WindowsTweaksViewModel(SystemState state, TweakListViewModel tweaks) : base(state, tweaks)
        => tweaks.Configure("Windows Tweaks", t => t.Category is TweakCategory.Explorer or TweakCategory.Privacy or TweakCategory.Startup or TweakCategory.Recovery);

    protected override void BuildInfo() { }

    [RelayCommand] private Task RestartExplorer() => ExplorerService.RestartAsync();
    [RelayCommand] private void OpenPerformanceOptions() => Process.Start(new ProcessStartInfo(ProcessRunner.System32("SystemPropertiesPerformance.exe")) { UseShellExecute = true });
}

public sealed partial class RegistryTweaksViewModel : ModulePageViewModel
{
    public override string Title => "Registry Tweaks";
    public override string Subtitle => "Searchable database of documented, reversible registry-backed tweaks with live current values";

    public RegistryTweaksViewModel(SystemState state, TweakListViewModel tweaks) : base(state, tweaks)
        => tweaks.Configure("Registry Tweaks", t => t.IsRegistryBacked);

    protected override void BuildInfo() { }
}

public sealed partial class InputLatencyViewModel : ModulePageViewModel
{
    public override string Title => "Input Latency";
    public override string Subtitle => "Mouse, keyboard, display and USB settings that affect responsiveness — configuration checks, not mouse-to-photon measurements";

    public ObservableCollection<InfoRow> Devices { get; } = new();

    public InputLatencyViewModel(SystemState state, TweakListViewModel tweaks) : base(state, tweaks)
    {
        tweaks.Configure("Input Latency", t => t.Category == TweakCategory.Input || t.Id is "power.usb.selectivesuspend.disable" or "gaming.vrr.enable", showFilters: false);
        SetGuidance(new[]
        {
            "What this page shows are configuration states and theoretical latency mechanisms. Real end-to-end latency needs hardware such as NVIDIA's LDAT, a high-speed camera, or the in-game Reflex latency analyzer — none of these values are measured latency.",
            "Biggest real-world factors: frame rate and refresh rate (a 240 Hz display refreshes every 4.2 ms vs 16.7 ms at 60 Hz), render queue depth (Reflex/Anti-Lag), V-Sync, and frame caps.",
            "Mouse polling rate is set in the mouse's own software; Windows does not expose it. 1000 Hz is a good default; higher rates cost CPU time.",
            "Prefer wired or 2.4 GHz dongles over Bluetooth for mice and controllers in competitive games.",
            "USB selective suspend can add wake-up delay after idle periods; disabling it on AC power is low-risk."
        });
    }

    protected override void BuildInfo()
    {
        var s = State.Snapshot;
        if (s is null) return;
        string mouse = ReadString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed") ?? "1";
        string sens = ReadString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSensitivity") ?? "10";
        string delay = ReadString(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardDelay") ?? "1";
        string speed = ReadString(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardSpeed") ?? "31";
        bool Hotkey(string path, int def) => (int.TryParse(ReadString(Registry.CurrentUser, path, "Flags"), out var f) ? f : def) is var v && (v & 4) != 0;
        var usb = new PowerSettingHandler().Preflight(new PowerSettingSpec { Subgroup = new("2a737441-1930-4402-8d77-b2bebba308a3"), Setting = new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226"), AcValue = 0 });
        var rows = new List<InfoRow>
        {
            new("Enhance pointer precision (acceleration)", mouse == "0" ? "Off" : "On", Tone: mouse == "0" ? "Success" : null),
            new("Pointer speed", $"{sens} / 20 (10 = 1:1 without acceleration)"),
            new("Mouse polling rate", "Not exposed by Windows — check the mouse software", true),
            new("Keyboard repeat delay / rate", $"{delay} (0 = shortest) / {speed} (31 = fastest)"),
            new("Sticky Keys shortcut", Hotkey(@"Control Panel\Accessibility\StickyKeys", 510) ? "Enabled (Shift ×5 opens prompt)" : "Disabled"),
            new("Filter Keys shortcut", Hotkey(@"Control Panel\Accessibility\Keyboard Response", 126) ? "Enabled" : "Disabled"),
            new("USB selective suspend (AC)", usb.Supported ? usb.Current : "Not available"),
        };
        foreach (var d in s.Displays)
            rows.Add(new($"Display: {d.FriendlyName}", $"{d.RefreshHz} Hz ({1000.0 / Math.Max(1, d.RefreshHz):0.0} ms per refresh)" + (d.RunningBelowMaxRefresh ? $" — supports {d.MaxRefreshAtCurrentResolution} Hz" : ""),
                Tone: d.RunningBelowMaxRefresh ? "Warning" : "Success"));
        SetInfo(rows);

        Devices.Clear();
        foreach (var o in Wmi.TryQuery(Wmi.Cimv2, "SELECT Name, PNPClass, Status FROM Win32_PnPEntity WHERE PNPClass='HIDClass' OR PNPClass='XnaComposite' OR PNPClass='XboxComposite' OR PNPClass='Mouse' OR PNPClass='Keyboard'", out _))
        {
            var name = o.Str("Name") ?? "";
            var cls = o.Str("PNPClass") ?? "";
            bool pad = cls.Contains("Xna") || cls.Contains("Xbox") || name.Contains("game", StringComparison.OrdinalIgnoreCase) || name.Contains("controller", StringComparison.OrdinalIgnoreCase) || name.Contains("XINPUT", StringComparison.OrdinalIgnoreCase);
            if (cls is "Mouse" or "Keyboard" || pad)
                Devices.Add(new InfoRow(pad ? "Game controller" : cls, $"{name} ({o.Str("Status")})"));
        }
        if (!Devices.Any(d => d.Label == "Game controller")) Devices.Add(new InfoRow("Game controller", "None detected", true));
    }
}
