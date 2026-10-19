using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Finds installed games from launcher manifests and the registry. Read-only; never touches game files.</summary>
public sealed class GameDetectionService
{
    private readonly LoggingService _log;
    public IReadOnlyList<GameDefinition> Definitions { get; }

    public GameDetectionService(LoggingService log)
    {
        _log = log;
        Definitions = JsonDefaults.ReadEmbedded<List<GameDefinition>>("Data.Games.json") ?? new();
    }

    public Task<List<DetectedGame>> DetectAsync() => Task.Run(Detect);

    public List<DetectedGame> Detect()
    {
        var games = new List<DetectedGame>();
        var steamLibraries = GetSteamLibraries();
        var epic = GetEpicManifests();
        var uninstall = HardwareDetectionService.EnumerateInstallLocations().ToList();

        foreach (var def in Definitions)
        {
            var g = new DetectedGame { Id = def.Id, Name = def.Name, Definition = def, LaunchUri = def.LaunchUri };
            try
            {
                if (def.SteamAppId is { } appId)
                {
                    foreach (var lib in steamLibraries)
                    {
                        var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
                        if (!File.Exists(manifest)) continue;
                        var installDir = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                        var dir = Path.Combine(lib, "steamapps", "common", installDir);
                        if (Directory.Exists(dir)) { g.InstallDirectory = dir; g.DetectedVia = "Steam library"; break; }
                    }
                }
                if (g.InstallDirectory is null && def.EpicAppName is not null && epic.TryGetValue(def.EpicAppName, out var ep))
                {
                    g.InstallDirectory = ep.InstallLocation;
                    g.DetectedVia = "Epic Games Launcher";
                    if (ep.LaunchExecutable is not null) g.ExecutablePath = Path.Combine(ep.InstallLocation, ep.LaunchExecutable);
                }
                if (g.InstallDirectory is null)
                {
                    foreach (var m in def.UninstallNameMatches)
                    {
                        var hit = uninstall.FirstOrDefault(u => u.Name.Contains(m, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(u.InstallLocation) && Directory.Exists(u.InstallLocation));
                        if (hit.Name is not null) { g.InstallDirectory = hit.InstallLocation; g.DetectedVia = "Installed programs"; break; }
                    }
                }
                if (g.InstallDirectory is null)
                {
                    var known = def.KnownInstallPaths.FirstOrDefault(Directory.Exists);
                    if (known is not null) { g.InstallDirectory = known; g.DetectedVia = "Default install path"; }
                }
                if (g.InstallDirectory is not null && (g.ExecutablePath is null || !File.Exists(g.ExecutablePath)))
                {
                    g.ExecutablePath = def.RelativeExePaths.Select(r => Path.Combine(g.InstallDirectory, r)).FirstOrDefault(File.Exists)
                                       ?? FindExe(g.InstallDirectory, def.ProcessNames);
                }
                g.IsRunning = def.ProcessNames.Any(p => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(p)).Length > 0);
                if (g.IsRunning && g.ExecutablePath is null) g.ExecutablePath = RunningPath(def.ProcessNames);
                if (g.IsRunning && g.DetectedVia.Length == 0) g.DetectedVia = "Running process";
            }
            catch (Exception ex) { _log.Warn("Games", $"Detection of {def.Name} failed", ex); }
            if (g.InstallDirectory is not null || g.IsRunning) games.Add(g);
        }

        foreach (var c in LoadCustom())
        {
            var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(c.ExecutablePath)).Length > 0;
            games.Add(new DetectedGame
            {
                Id = "custom:" + c.ExecutablePath, Name = c.Name, ExecutablePath = c.ExecutablePath, InstallDirectory = Path.GetDirectoryName(c.ExecutablePath),
                DetectedVia = "Added by you", IsCustom = true, IsRunning = running
            });
        }
        return games;
    }

    private static string? FindExe(string dir, IEnumerable<string> names)
    {
        try
        {
            foreach (var n in names)
            {
                var hit = Directory.EnumerateFiles(dir, n, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true }).FirstOrDefault();
                if (hit is not null) return hit;
            }
        }
        catch { }
        return null;
    }

    private static string? RunningPath(IEnumerable<string> names)
    {
        foreach (var n in names)
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(n)))
            {
                try { return p.MainModule?.FileName; } catch { /* protected by anti-cheat */ }
                finally { p.Dispose(); }
            }
        }
        return null;
    }

    public static List<string> GetSteamLibraries()
    {
        var libs = new List<string>();
        try
        {
            var steamPath = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
            if (steamPath is null) return libs;
            steamPath = steamPath.Replace('/', '\\');
            libs.Add(steamPath);
            var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    var p = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (!libs.Contains(p, StringComparer.OrdinalIgnoreCase)) libs.Add(p);
                }
            }
        }
        catch { }
        return libs.Where(Directory.Exists).ToList();
    }

    private sealed record EpicManifest(string InstallLocation, string? LaunchExecutable);

    private static Dictionary<string, EpicManifest> GetEpicManifests()
    {
        var map = new Dictionary<string, EpicManifest>(StringComparer.OrdinalIgnoreCase);
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
        if (!Directory.Exists(dir)) return map;
        foreach (var f in Directory.EnumerateFiles(dir, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var root = doc.RootElement;
                var app = root.TryGetProperty("AppName", out var a) ? a.GetString() : null;
                var loc = root.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
                var exe = root.TryGetProperty("LaunchExecutable", out var e) ? e.GetString() : null;
                if (app is not null && loc is not null) map[app] = new EpicManifest(loc, exe);
            }
            catch { }
        }
        return map;
    }

    // ------------------------------------------------------------------------------------- custom games

    public List<CustomGameEntry> LoadCustom()
    {
        try { return JsonDefaults.ReadFile<List<CustomGameEntry>>(AppPaths.GamesFile) ?? new(); }
        catch { return new(); }
    }

    public void AddCustom(string name, string exePath)
    {
        var list = LoadCustom();
        list.RemoveAll(c => c.ExecutablePath.Equals(exePath, StringComparison.OrdinalIgnoreCase));
        list.Add(new CustomGameEntry { Name = name, ExecutablePath = exePath });
        JsonDefaults.WriteFile(AppPaths.GamesFile, list);
    }

    public void RemoveCustom(string exePath)
    {
        var list = LoadCustom();
        list.RemoveAll(c => c.ExecutablePath.Equals(exePath, StringComparison.OrdinalIgnoreCase));
        JsonDefaults.WriteFile(AppPaths.GamesFile, list);
    }

    // ------------------------------------------------------------------------------------- per-game changes

    /// <summary>Per-app GPU preference (Settings › Display › Graphics): 1 power saving, 2 high performance.</summary>
    public static PlannedChange GpuPreferenceChange(DetectedGame g, int preference) => new()
    {
        Title = $"{g.Name}: GPU preference → {(preference == 2 ? "High performance" : "Power saving")}",
        TweakId = $"game.gpupref:{g.ExecutablePath}",
        Category = "Game profile",
        Risk = RiskLevel.Low,
        Restart = RestartRequirement.AppRestart,
        Spec = new RegistryValueSpec
        {
            Hive = "HKCU", Path = @"Software\Microsoft\DirectX\UserGpuPreferences", Name = g.ExecutablePath!,
            Kind = RegistryValueKindName.String, Value = $"GpuPreference={preference};"
        }
    };

    public static PlannedChange DisableFullscreenOptimizationsChange(DetectedGame g) => new()
    {
        Title = $"{g.Name}: disable fullscreen optimizations",
        TweakId = $"game.fso:{g.ExecutablePath}",
        Category = "Game profile",
        Risk = RiskLevel.Low,
        Restart = RestartRequirement.AppRestart,
        Notes = "On current Windows, fullscreen optimizations usually give equal or lower latency; only disable this if the game has alt-tab or stutter problems.",
        Spec = new AppCompatLayerSpec { ExePath = g.ExecutablePath!, Flag = "DISABLEDXMAXIMIZEDWINDOWEDMODE" }
    };

    /// <summary>
    /// Image File Execution Options\PerfOptions\CpuPriorityClass: 2 Normal, 6 AboveNormal, 3 High. Realtime is never used.
    /// </summary>
    public static PlannedChange CpuPriorityChange(DetectedGame g, int priorityClass) => new()
    {
        Title = $"{g.Name}: CPU priority → {(priorityClass == 3 ? "High" : priorityClass == 6 ? "Above normal" : "Normal")}",
        TweakId = $"game.priority:{Path.GetFileName(g.ExecutablePath)}",
        Category = "Game profile",
        Risk = RiskLevel.Medium,
        Restart = RestartRequirement.AppRestart,
        Notes = "Applied by Windows when the game starts. Higher priority can starve background apps (voice chat, streaming). Some anti-cheat titles reset priority.",
        Spec = new RegistryValueSpec
        {
            Hive = "HKLM", Path = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{Path.GetFileName(g.ExecutablePath)}\PerfOptions",
            Name = "CpuPriorityClass", Kind = RegistryValueKindName.DWord, Value = priorityClass.ToString()
        }
    };

    public static void Launch(DetectedGame g)
    {
        if (g.Id == "valorant" && FindRiotClient() is { } riot)
        {
            // Same command line as the desktop shortcut created by the Riot installer.
            Process.Start(new ProcessStartInfo(riot, "--launch-product=valorant --launch-patchline=live") { UseShellExecute = true });
            return;
        }
        if (g.LaunchUri is not null && !g.IsCustom) Process.Start(new ProcessStartInfo(g.LaunchUri) { UseShellExecute = true });
        else if (g.ExecutablePath is not null) Process.Start(new ProcessStartInfo(g.ExecutablePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(g.ExecutablePath)! });
        else throw new InvalidOperationException("No launch method known for this game.");
    }

    private static string? FindRiotClient()
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Riot Games\RiotClientInstalls.json");
        try
        {
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var key in new[] { "rc_default", "rc_live" })
                if (doc.RootElement.TryGetProperty(key, out var v) && v.GetString() is { } p && File.Exists(p)) return p;
        }
        catch { }
        return null;
    }
}
