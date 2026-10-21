using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

public sealed class BootRecord
{
    public DateTime Time { get; set; }
    public double TotalSec { get; set; }
    public double MainPathSec { get; set; }
    public double PostBootSec { get; set; }
    public double KernelSec { get; set; }
    public double DriversSec { get; set; }
    public double ProfileSec { get; set; }
    public double ExplorerSec { get; set; }
}

public sealed class StartupCause
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>Firmware, Startup app, Logon task, Windows setting, Driver, Service, Hardware.</summary>
    public string Source { get; set; } = "";
    /// <summary>Measured delay in seconds (from Windows' own boot diagnostics); null when Windows has no measurement.</summary>
    public double? Seconds { get; set; }
    public bool IsMeasured => Seconds is not null;
    public Severity Severity { get; set; }
    public bool Fixable => Changes.Count > 0;
    public string Fix { get; set; } = "";
    public bool PreSelected { get; set; }
    public List<PlannedChange> Changes { get; set; } = new();
}

public sealed class StartupReport
{
    public DateTime ScannedAt { get; set; } = DateTime.Now;
    public double? FirmwareSec { get; set; }
    public List<BootRecord> Boots { get; set; } = new();
    public BootRecord? LatestBoot => Boots.OrderByDescending(b => b.Time).FirstOrDefault();
    public double? AverageWindowsSec => Boots.Count == 0 ? null : Boots.Average(b => b.TotalSec);
    public List<StartupCause> Causes { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public string? EventLogNote { get; set; }
}

/// <summary>
/// Explains a slow boot. Windows records its own boot timings (Diagnostics-Performance log) and the firmware POST time;
/// this service reads those real measurements, then proposes fixes only for the parts Windows can influence.
/// </summary>
public sealed class StartupDoctorService
{
    private const string PerfLog = "Microsoft-Windows-Diagnostics-Performance/Operational";
    private readonly StartupManagerService _startup;
    private readonly WindowsDebloatService _debloat;
    private readonly TweakCatalogService _catalog;
    private readonly LoggingService _log;

    public StartupDoctorService(StartupManagerService startup, WindowsDebloatService debloat, TweakCatalogService catalog, LoggingService log)
    {
        _startup = startup;
        _debloat = debloat;
        _catalog = catalog;
        _log = log;
    }

    // (match, preselect, note): launchers/updaters/chat apps are not needed until you open them.
    private static readonly (string Match, bool Pre, string Note)[] KnownApps =
    {
        ("WhatsApp", true, "Chat app — opens fine from the Start menu; you only lose automatic sign-in at boot."),
        ("Discord", true, "Starts a full Chromium app at login. You can still open it manually."),
        ("Spotify", true, "Music app; not needed until you play music."),
        ("Steam", true, "Launcher; start it when you want to play."),
        ("Epic", true, "Launcher; start it when you want to play."),
        ("Battle.net", true, "Launcher; start it when you want to play."),
        ("EADM", true, "EA app launcher."), ("EALauncher", true, "EA app launcher."),
        ("RiotClient", true, "Riot launcher — starts on demand when you launch VALORANT. Vanguard anti-cheat is a separate service and is NOT affected."),
        ("Adobe", true, "Adobe background helper (updater/cloud sync)."), ("CCXProcess", true, "Adobe Creative Cloud helper."),
        ("Teams", true, "Chat app."), ("Skype", true, "Chat app."), ("Dropbox", true, "Sync client; sync pauses until opened."),
        ("OneDrive", false, "File sync — disable only if you do not use OneDrive backup."),
        ("IDMan", false, "Internet Download Manager — needed for browser download capture."),
        ("lghub", false, "Logitech G HUB — needed if your mouse/keyboard settings are applied by software rather than stored on the device."),
        ("rzappengine", false, "Razer Synapse engine — needed for Razer lighting/macros."),
        ("RZSurround", false, "Razer helper."),
        ("vgtray", false, "Riot Vanguard tray icon only. Vanguard itself keeps protecting VALORANT either way, but leave it unless you want the icon gone."),
        ("SecurityHealth", false, "Windows Security tray icon — leave enabled."),
        ("RtkAudUService", false, "Audio driver helper — leave enabled."),
    };

    public Task<StartupReport> AnalyzeAsync(SystemSnapshot? snapshot) => Task.Run(() => Analyze(snapshot));

    public StartupReport Analyze(SystemSnapshot? snapshot)
    {
        var r = new StartupReport();
        r.FirmwareSec = ReadFirmwareSeconds();
        ReadBootEvents(r, out var degraded);

        // 1. Firmware
        if (r.FirmwareSec is { } fw)
        {
            var sev = fw >= 15 ? Severity.High : fw >= 8 ? Severity.Medium : Severity.Info;
            r.Causes.Add(new StartupCause
            {
                Source = "Firmware",
                Title = $"Firmware (UEFI/BIOS) takes {fw:0.0} s before Windows even starts",
                Seconds = fw,
                Severity = sev,
                Detail = fw >= 8
                    ? "This time is spent by your motherboard's firmware, not by Windows or any startup app, so it cannot be reduced from inside Windows. " +
                      "A typical desktop needs about 5–10 s. Common causes of a long POST: memory training on DDR5/AM5 platforms, Fast Boot disabled, " +
                      "boot-device or network-boot scanning, and many USB devices at power-on."
                    : "Normal for a desktop. Windows cannot change it.",
                Fix = fw >= 8
                    ? "Open UEFI settings and enable Fast Boot, enable Memory Context Restore / disable full memory training if your board offers it (names vary by vendor), " +
                      "set the Windows disk as first boot device, and disable network/PXE boot. Check your motherboard manual; update the BIOS if a newer version mentions boot-time or memory improvements."
                    : ""
            });
        }
        else r.Notes.Add("Firmware boot time is not reported by this PC (it appears in Task Manager as 'Last BIOS time' only when the firmware supports it).");

        // 2. Windows boot phases
        if (r.LatestBoot is { } b)
        {
            if (b.DriversSec > 5)
                r.Causes.Add(new StartupCause { Source = "Driver", Severity = Severity.Medium, Seconds = b.DriversSec, Title = $"Driver initialisation took {b.DriversSec:0.0} s",
                    Detail = "Slow driver start-up is usually a specific old or faulty driver. Windows lists the slowest ones below if it measured any. Updating drivers from your motherboard/GPU vendor is the fix — this app does not touch drivers." });
            if (b.ProfileSec > 6)
                r.Causes.Add(new StartupCause { Source = "Windows", Severity = Severity.Medium, Seconds = b.ProfileSec, Title = $"User profile load took {b.ProfileSec:0.0} s",
                    Detail = "Often caused by a very large profile, redirected folders, or antivirus scanning during sign-in." });
            if (b.PostBootSec > 30)
                r.Causes.Add(new StartupCause { Source = "Startup app", Severity = Severity.High, Seconds = b.PostBootSec, Title = $"After the desktop appears, Windows is busy for another {b.PostBootSec:0} s",
                    Detail = "This 'post-boot' period is when startup apps, services and scheduled tasks all start. It is the part you can improve by disabling startup items (below)." });
        }

        // 3. Startup apps — correlate with Windows' own measurements
        var entries = _startup.GetEntries().Where(e => e.Enabled).ToList();
        foreach (var e in entries)
        {
            var key = Path.GetFileNameWithoutExtension(e.ExecutablePath ?? e.Name);
            var measured = degraded.Where(d => d.Kind == "App" && (d.Name.Contains(key, StringComparison.OrdinalIgnoreCase) || key.Contains(d.Name, StringComparison.OrdinalIgnoreCase))).ToList();
            var known = KnownApps.FirstOrDefault(k => e.Name.Contains(k.Match, StringComparison.OrdinalIgnoreCase) || (e.Command?.Contains(k.Match, StringComparison.OrdinalIgnoreCase) ?? false));
            double? secs = measured.Count > 0 ? measured.Average(m => m.TotalMs) / 1000.0 : null;
            bool pre = secs is > 3 || (known.Match is not null && known.Pre);
            if (known.Match is not null && !known.Pre && secs is not > 3) pre = false;
            var cause = new StartupCause
            {
                Source = "Startup app",
                Title = $"{e.Name} starts with Windows",
                Seconds = secs,
                Severity = secs is > 10 ? Severity.High : secs is > 3 ? Severity.Medium : Severity.Low,
                Detail = (secs is { } s ? $"Windows measured it taking {s:0.0} s to start (average of {measured.Count} slow boots). " : "Windows has no timing for this app. ") +
                         (known.Match is not null ? known.Note : e.Publisher is null ? "Publisher unknown — check what it is before keeping it." : $"By {e.Publisher}.") +
                         $" ({e.LocationDisplay})",
                Fix = "Disable the startup entry (reversible; the app stays installed and can still be opened manually).",
                PreSelected = pre,
                Changes =
                {
                    new PlannedChange
                    {
                        Title = $"Disable startup: {e.Name}", TweakId = $"startup:{e.Location}:{e.Name}", Category = "Startup", Risk = RiskLevel.Low, Restart = RestartRequirement.SignOut,
                        Notes = "Takes effect at next sign-in. Restore re-enables it.",
                        Spec = new StartupEntrySpec { Location = e.Location, EntryName = e.Name, Enabled = false }
                    }
                }
            };
            r.Causes.Add(cause);
        }

        // 4. Slow services / drivers reported by Windows (informational — not modified by this app)
        foreach (var d in degraded.Where(x => x.Kind is "Service" or "Driver").GroupBy(x => x.Kind + x.Name).Select(g => g.First()).Take(5))
        {
            var avg = degraded.Where(x => x.Kind == d.Kind && x.Name == d.Name).Average(x => x.TotalMs) / 1000.0;
            if (avg < 2) continue;
            r.Causes.Add(new StartupCause
            {
                Source = d.Kind, Seconds = avg, Severity = avg > 8 ? Severity.High : Severity.Medium,
                Title = $"{d.Kind} '{d.Friendly ?? d.Name}' was slow to start ({avg:0.0} s)",
                Detail = d.Kind == "Driver"
                    ? "Update this driver from the hardware vendor. This app does not modify drivers."
                    : "Open Services Optimizer to see whether this service is optional. Essential and unknown services are never changed by this app."
            });
        }

        // 5. Logon scheduled tasks (hidden startup mechanism)
        try
        {
            foreach (var t in _debloat.GetLogonTasks().Where(t => t.Enabled))
            {
                r.Causes.Add(new StartupCause
                {
                    Source = "Logon task", Severity = Severity.Low, Title = $"Scheduled task runs at logon: {t.TaskPath}",
                    Detail = $"{t.Author} · {t.Action}. Tasks like this often launch updaters or helpers.",
                    Fix = "Disable the task (reversible).",
                    Changes =
                    {
                        new PlannedChange
                        {
                            Title = $"Disable logon task {t.TaskPath}", TweakId = "task:" + t.TaskPath, Category = "Startup", Risk = RiskLevel.Low,
                            Spec = new ScheduledTaskSpec { TaskPath = t.TaskPath, Enabled = false }
                        }
                    }
                });
            }
        }
        catch (Exception ex) { _log.Warn("StartupDoctor", "Logon task scan failed", ex); }

        // 6. Windows startup-delay setting (reuses the documented catalog tweak)
        if (_catalog.Get("startup.delay.remove") is { } delay)
        {
            var (ok, why) = TweakResolver.CheckApplicability(delay, snapshot);
            var planned = ok ? TweakResolver.ToPlannedChanges(delay, snapshot, out _) : null;
            if (planned is not null && planned.Count > 0 && !IsApplied(delay))
            {
                r.Causes.Add(new StartupCause
                {
                    Source = "Windows setting", Severity = Severity.Low, Title = "Windows waits before launching startup apps",
                    Detail = "Explorer deliberately delays startup apps. On an SSD this delay can be removed so the desktop is ready sooner. Note: it starts all apps at once.",
                    Fix = "Set the startup delay to 0 (reversible).", PreSelected = true, Changes = planned
                });
            }
            else if (!ok) r.Notes.Add($"Startup delay tweak not offered: {why}");
        }

        // 7. Hardware facts that limit boot speed
        var sys = snapshot?.Storage.SystemDisk;
        if (sys?.MediaKind == StorageMediaKind.Hdd)
            r.Causes.Add(new StartupCause { Source = "Hardware", Severity = Severity.High, Title = "Windows is installed on a hard disk",
                Detail = "An HDD makes both Windows boot and app start-up much slower. Moving Windows to an SSD is the biggest improvement possible — software cannot compensate." });
        var vol = snapshot?.Storage.Volumes.FirstOrDefault(v => v.IsSystemVolume);
        if (vol is { FreePercent: < 10 })
            r.Causes.Add(new StartupCause { Source = "Hardware", Severity = Severity.Medium, Title = $"System drive is nearly full ({vol.FreePercent:0}% free)",
                Detail = "Low free space slows updates, the page file and boot housekeeping. Use the Windows Debloater › Temporary files tab." });

        // Notes
        r.Notes.Add("Fast Startup is not listed as a fix: it shortens the Windows part of a shutdown/start cycle but has no effect on the firmware time, and a restart always does a full boot.");
        if (r.Boots.Count == 0 && r.EventLogNote is null) r.EventLogNote = "No boot-performance events were found yet (Windows records them after boots).";
        r.Causes = r.Causes.OrderByDescending(c => c.Source == "Firmware" ? 1000 : 0).ThenByDescending(c => c.Seconds ?? -1).ThenByDescending(c => c.Severity).ToList();
        return r;
    }

    private bool IsApplied(TweakDefinition t)
    {
        try
        {
            var pre = new Engine.Handlers.RegistryValueHandler().Preflight(t.Actions.OfType<RegistryValueSpec>().First());
            return pre.AlreadyApplied;
        }
        catch { return false; }
    }

    private static double? ReadFirmwareSeconds()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            return k?.GetValue("FwPOSTTime") is int ms and > 0 ? ms / 1000.0 : null;
        }
        catch { return null; }
    }

    private sealed record Degraded(string Kind, string Name, string? Friendly, double TotalMs);

    private void ReadBootEvents(StartupReport r, out List<Degraded> degraded)
    {
        degraded = new List<Degraded>();
        try
        {
            var query = new EventLogQuery(PerfLog, PathType.LogName,
                "*[System[(EventID>=100 and EventID<=103) and TimeCreated[timediff(@SystemTime) <= 2592000000]]]");
            using var reader = new EventLogReader(query);
            int count = 0;
            for (EventRecord? ev = reader.ReadEvent(); ev is not null && count < 800; ev = reader.ReadEvent(), count++)
            {
                using (ev)
                {
                    var data = XDocument.Parse(ev.ToXml()).Descendants().Where(x => x.Name.LocalName == "Data" && x.Attribute("Name") is not null)
                        .ToDictionary(x => x.Attribute("Name")!.Value, x => x.Value);
                    double Ms(string key) => data.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
                    switch (ev.Id)
                    {
                        case 100:
                            r.Boots.Add(new BootRecord
                            {
                                Time = ev.TimeCreated ?? DateTime.MinValue, TotalSec = Ms("BootTime") / 1000, MainPathSec = Ms("MainPathBootTime") / 1000,
                                PostBootSec = Ms("BootPostBootTime") / 1000, KernelSec = Ms("BootKernelInitTime") / 1000, DriversSec = Ms("BootDriverInitTime") / 1000,
                                ProfileSec = Ms("BootUserProfileProcessingTime") / 1000, ExplorerSec = Ms("BootExplorerInitTime") / 1000
                            });
                            break;
                        case 101: degraded.Add(new Degraded("App", data.GetValueOrDefault("Name") ?? "", data.GetValueOrDefault("FriendlyName"), Ms("TotalTime"))); break;
                        case 102: degraded.Add(new Degraded("Driver", data.GetValueOrDefault("Name") ?? "", data.GetValueOrDefault("FriendlyName"), Ms("TotalTime"))); break;
                        case 103: degraded.Add(new Degraded("Service", data.GetValueOrDefault("Name") ?? "", data.GetValueOrDefault("FriendlyName"), Ms("TotalTime"))); break;
                    }
                }
            }
            r.Boots = r.Boots.OrderByDescending(b => b.Time).Take(10).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or EventLogException or InvalidOperationException)
        {
            r.EventLogNote = ex is UnauthorizedAccessException
                ? "Windows' boot-timing log needs administrator rights to read. Restart as administrator for measured per-app delays."
                : "Windows' boot-timing log is unavailable: " + ex.Message;
            _log.Warn("StartupDoctor", "Boot events unavailable", ex);
        }
    }
}
