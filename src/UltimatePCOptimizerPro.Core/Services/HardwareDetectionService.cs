using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Reads real hardware and OS configuration. Read-only: never modifies the system.</summary>
public sealed partial class HardwareDetectionService
{
    private readonly LoggingService _log;

    public HardwareDetectionService(LoggingService log) => _log = log;

    public Task<SystemSnapshot> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Scan(progress, ct), ct);

    private SystemSnapshot Scan(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var s = new SystemSnapshot { ScannedAt = DateTimeOffset.Now };
        var steps = new (string Name, Action Run)[]
        {
            ("Processor", () => s.Cpu = DetectCpu(s.Warnings)),
            ("Graphics", () => s.Gpus = DetectGpus(s.Warnings)),
            ("Memory", () => s.Memory = DetectMemory(s.Warnings)),
            ("Storage", () => s.Storage = DetectStorage(s.Warnings)),
            ("Motherboard & firmware", () => s.Board = DetectBoard(s.Warnings)),
            ("Displays", () => s.Displays = DetectDisplays(s.Warnings)),
            ("Network", () => s.Network = DetectNetwork(s.Warnings)),
            ("Power", () => s.Power = DetectPower(s.Warnings)),
            ("Operating system", () => s.Os = DetectOs(s.Warnings)),
            ("Relevant software", () => s.RelevantSoftware = DetectRelevantSoftware(s.Warnings)),
        };
        for (int i = 0; i < steps.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(steps[i].Name, i, steps.Length));
            try
            {
                steps[i].Run();
            }
            catch (Exception ex)
            {
                s.Warnings.Add(new ScanWarning(steps[i].Name, $"Detection failed: {ex.Message}"));
                _log.Warn("Scanner", $"{steps[i].Name} detection failed", ex);
            }
        }
        if (s.Power.HasBattery) s.Board.IsLaptop = true;
        sw.Stop();
        s.ScanDuration = sw.Elapsed;
        progress?.Report(new ScanProgress("Complete", steps.Length, steps.Length));
        _log.Info("Scanner", $"Scan completed in {sw.ElapsedMilliseconds} ms with {s.Warnings.Count} warnings");
        return s;
    }

    // ------------------------------------------------------------------ Motherboard / firmware

    private static BoardInfo DetectBoard(List<ScanWarning> warnings)
    {
        var b = new BoardInfo();
        var systems = Wmi.TryQuery(Wmi.Cimv2, "SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem", out var e1);
        foreach (var o in systems)
        {
            b.SystemManufacturer = o.Str("Manufacturer");
            b.SystemModel = o.Str("Model");
            // PCSystemType: 2 = Mobile
            if (o.Int("PCSystemType") == 2) b.IsLaptop = true;
        }
        if (e1 is not null) warnings.Add(new ScanWarning("Motherboard", e1));

        foreach (var o in Wmi.TryQuery(Wmi.Cimv2, "SELECT Manufacturer, Product FROM Win32_BaseBoard", out _))
        {
            b.BoardManufacturer = o.Str("Manufacturer");
            b.BoardProduct = o.Str("Product");
        }
        foreach (var o in Wmi.TryQuery(Wmi.Cimv2, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS", out _))
        {
            b.BiosVendor = o.Str("Manufacturer");
            b.BiosVersion = o.Str("SMBIOSBIOSVersion");
            b.BiosReleaseDate = o.Date("ReleaseDate");
        }
        foreach (var o in Wmi.TryQuery(Wmi.Cimv2, "SELECT ChassisTypes FROM Win32_SystemEnclosure", out _))
        {
            if (o.Raw("ChassisTypes") is ushort[] types && types.Length > 0)
            {
                // SMBIOS chassis: 8 portable, 9 laptop, 10 notebook, 14 sub-notebook, 30 tablet, 31 convertible, 32 detachable
                if (types.Any(t => t is 8 or 9 or 10 or 14 or 30 or 31 or 32)) b.IsLaptop = true;
                b.ChassisDescription = ChassisName(types[0]);
            }
        }
        try
        {
            if (NativeMethods.GetFirmwareType(out var fw))
                b.BootMode = fw switch { 1 => "Legacy BIOS", 2 => "UEFI", _ => "Unknown" };
        }
        catch (EntryPointNotFoundException) { }
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (k?.GetValue("UEFISecureBootEnabled") is int sb) b.SecureBootEnabled = sb == 1;
        }
        catch (Exception ex) { warnings.Add(new ScanWarning("Firmware", "Secure Boot state unreadable: " + ex.Message)); }
        return b;
    }

    private static string ChassisName(ushort t) => t switch
    {
        3 => "Desktop", 4 => "Low-profile desktop", 5 => "Pizza box", 6 => "Mini tower", 7 => "Tower", 8 => "Portable",
        9 => "Laptop", 10 => "Notebook", 13 => "All-in-one", 14 => "Sub-notebook", 15 => "Space-saving", 30 => "Tablet",
        31 => "Convertible", 32 => "Detachable", 35 => "Mini PC", 36 => "Stick PC", _ => $"Chassis type {t}"
    };

    // ------------------------------------------------------------------ Power

    private static PowerStatusInfo DetectPower(List<ScanWarning> warnings)
    {
        var p = new PowerStatusInfo();
        if (NativeMethods.GetSystemPowerStatus(out var st))
        {
            // BatteryFlag 128 = no system battery, 255 = unknown
            p.HasBattery = st.BatteryFlag != 128 && st.BatteryFlag != 255;
            p.OnAcPower = st.ACLineStatus != 0;
            if (p.HasBattery && st.BatteryLifePercent <= 100) p.BatteryPercent = st.BatteryLifePercent;
        }
        var scheme = NativeMethods.GetActivePowerScheme();
        if (scheme is { } g)
        {
            p.ActivePlanGuid = g;
            p.ActivePlanName = NativeMethods.GetPowerSchemeName(g) ?? g.ToString();
        }
        else warnings.Add(new ScanWarning("Power", "Active power plan could not be read."));
        return p;
    }

    // ------------------------------------------------------------------ Operating system

    private OsInfo DetectOs(List<ScanWarning> warnings)
    {
        var os = new OsInfo
        {
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            IsElevated = Elevation.IsElevated,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            ProcessCount = Process.GetProcesses().Length
        };
        using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            if (k is not null)
            {
                os.ProductName = k.GetValue("ProductName") as string ?? "Windows";
                os.EditionId = k.GetValue("EditionID") as string;
                os.DisplayVersion = k.GetValue("DisplayVersion") as string ?? k.GetValue("ReleaseId") as string;
                if (int.TryParse(k.GetValue("CurrentBuildNumber") as string ?? k.GetValue("CurrentBuild") as string, out var build)) os.Build = build;
                if (k.GetValue("UBR") is int ubr) os.Ubr = ubr;
            }
        }
        if (os.Build == 0) os.Build = Environment.OSVersion.Version.Build;
        // ProductName still says "Windows 10" on Windows 11 for compatibility reasons.
        if (os.IsWindows11 && os.ProductName.Contains("Windows 10")) os.ProductName = os.ProductName.Replace("Windows 10", "Windows 11");

        os.GameModeEnabled = ReadDword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") is { } gm ? gm != 0 : true;
        var dvr = ReadDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled");
        var appCapture = ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
        os.GameDvrEnabled = dvr is null && appCapture is null ? null : (dvr ?? 1) != 0 && (appCapture ?? 1) != 0;
        os.BackgroundRecordingEnabled = ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled") is { } hc ? hc != 0 : false;
        os.HagsState = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") switch
        {
            2 => "On",
            1 => "Off",
            _ => "Not configured (driver default)"
        };

        var guards = Wmi.TryQuery(Wmi.DeviceGuard, "SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard", out var dgErr);
        foreach (var o in guards)
        {
            os.VbsStatus = o.Int("VirtualizationBasedSecurityStatus") switch
            {
                0 => "Not enabled",
                1 => "Enabled but not running",
                2 => "Running",
                _ => "Unknown"
            };
            if (o.Raw("SecurityServicesRunning") is uint[] running) os.MemoryIntegrityRunning = running.Contains(2u);
        }
        if (dgErr is not null) os.VbsStatus = "Unavailable (" + dgErr + ")";

        foreach (var o in Wmi.TryQuery(Wmi.SecurityCenter, "SELECT displayName FROM AntiVirusProduct", out _))
        {
            if (o.Str("displayName") is { } name && !os.AntivirusProducts.Contains(name)) os.AntivirusProducts.Add(name);
        }
        try
        {
            os.RunningServiceCount = System.ServiceProcess.ServiceController.GetServices()
                .Count(s => { try { return s.Status == System.ServiceProcess.ServiceControllerStatus.Running; } finally { s.Dispose(); } });
        }
        catch (Exception ex) { warnings.Add(new ScanWarning("Services", ex.Message)); }
        try
        {
            os.StartupEntryCount = new StartupManagerService(_log).GetEntries().Count(e => e.Enabled);
        }
        catch (Exception ex) { warnings.Add(new ScanWarning("Startup", ex.Message)); }
        return os;
    }

    internal static int? ReadDword(RegistryKey root, string path, string name)
    {
        try
        {
            using var k = root.OpenSubKey(path);
            return k?.GetValue(name) is int v ? v : null;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ Installed software relevant to gaming

    private static readonly (string Match, string Category, string Note)[] RelevantSoftwarePatterns =
    {
        ("Steam", "Launcher", "Steam overlay can be disabled per game if unused."),
        ("Epic Games Launcher", "Launcher", "Keeps background services; close when not needed."),
        ("Battle.net", "Launcher", ""),
        ("EA app", "Launcher", ""),
        ("Ubisoft Connect", "Launcher", "In-game overlay can be disabled in settings."),
        ("Riot Client", "Launcher", "Includes Riot Vanguard kernel anti-cheat for VALORANT."),
        ("Riot Vanguard", "Anti-cheat", "Kernel anti-cheat; never modify or disable it from third-party tools."),
        ("Discord", "Overlay / voice", "In-game overlay and hardware acceleration can affect frame pacing in some titles."),
        ("NVIDIA App", "GPU software", "Overlay and instant replay record in the background when enabled."),
        ("GeForce Experience", "GPU software", "ShadowPlay instant replay records in the background when enabled."),
        ("AMD Software", "GPU software", "ReLive instant replay records in the background when enabled."),
        ("Intel® Graphics Command Center", "GPU software", ""),
        ("Intel Graphics Software", "GPU software", ""),
        ("MSI Afterburner", "Monitoring / OC", "On-screen display relies on RivaTuner; overclocks are the user's responsibility."),
        ("RivaTuner Statistics Server", "Overlay / limiter", "Frame limiter can improve frame pacing; overlay hooks can conflict with some anti-cheat."),
        ("OBS Studio", "Recording", "Encoding load competes with the game when recording."),
        ("Overwolf", "Overlay", "Injects overlays into games; adds background processes."),
        ("Medal", "Recording", "Background clip recording consumes GPU encoder and disk."),
        ("Wallpaper Engine", "Desktop", "Animated wallpapers use GPU; enable 'pause when fullscreen'."),
        ("iCUE", "Peripheral / RGB", "RGB software polls devices continuously; known to add DPC/CPU overhead in some versions."),
        ("Armoury Crate", "Peripheral / RGB", "Installs several background services."),
        ("Razer Synapse", "Peripheral / RGB", ""),
        ("Logitech G HUB", "Peripheral / RGB", ""),
        ("SteelSeries GG", "Peripheral / RGB", ""),
        ("NZXT CAM", "Monitoring", "Polling hardware sensors continuously adds background load."),
        ("MSI Center", "OEM utility", ""),
        ("Lenovo Vantage", "OEM utility", "Contains the laptop performance/thermal mode switch."),
        ("OMEN Gaming Hub", "OEM utility", "Contains the laptop performance/thermal mode switch."),
        ("Alienware Command Center", "OEM utility", "Contains the laptop performance/thermal mode switch."),
        ("PresentMon", "Benchmark", "Used by the Benchmark Center for frame-time capture."),
        ("CapFrameX", "Benchmark", "Its CSV exports can be imported into the Benchmark Center."),
        ("Google Chrome", "Browser", "'Continue running background apps' keeps Chrome resident after closing."),
        ("Microsoft Edge", "Browser", "Startup boost and background mode keep Edge resident."),
    };

    private static List<SoftwareEntry> DetectRelevantSoftware(List<ScanWarning> warnings)
    {
        var found = new Dictionary<string, SoftwareEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, publisher, version) in EnumerateInstalledPrograms())
        {
            foreach (var p in RelevantSoftwarePatterns)
            {
                if (name.Contains(p.Match, StringComparison.OrdinalIgnoreCase) && !found.ContainsKey(p.Match))
                {
                    found[p.Match] = new SoftwareEntry { Name = name, Publisher = publisher, Version = version, Category = p.Category, Note = p.Note };
                }
            }
        }
        return found.Values.OrderBy(e => e.Category).ThenBy(e => e.Name).ToList();
    }

    /// <summary>Enumerates the Uninstall registry keys (64-bit, 32-bit and per-user).</summary>
    public static IEnumerable<(string Name, string? Publisher, string? Version)> EnumerateInstalledPrograms()
    {
        var sources = new (RegistryHive Hive, RegistryView View)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64)
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view) in sources)
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var sub in uninstall.GetSubKeyNames())
            {
                using var k = uninstall.OpenSubKey(sub);
                if (k?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                if (!seen.Add(name)) continue;
                yield return (name, k.GetValue("Publisher") as string, k.GetValue("DisplayVersion") as string);
            }
        }
    }

    public static IEnumerable<(string Name, string? InstallLocation)> EnumerateInstallLocations()
    {
        var sources = new (RegistryHive Hive, RegistryView View)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64)
        };
        foreach (var (hive, view) in sources)
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var sub in uninstall.GetSubKeyNames())
            {
                using var k = uninstall.OpenSubKey(sub);
                if (k?.GetValue("DisplayName") is not string name) continue;
                yield return (name, k.GetValue("InstallLocation") as string);
            }
        }
    }
}
