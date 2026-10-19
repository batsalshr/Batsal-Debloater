using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Engine.Handlers;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Read-side of the services optimizer. Modifications go through the engine (ServiceStartSpec).</summary>
public sealed class WindowsServiceManager
{
    private readonly LoggingService _log;
    public IReadOnlyList<ServiceCatalogEntry> Catalog { get; }

    public WindowsServiceManager(LoggingService log)
    {
        _log = log;
        Catalog = JsonDefaults.ReadEmbedded<List<ServiceCatalogEntry>>("Services.Services.json") ?? new();
    }

    public Task<List<ServiceEntry>> GetServicesAsync() => Task.Run(GetServices);

    public List<ServiceEntry> GetServices()
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var rows = Wmi.TryQuery(Wmi.Cimv2, "SELECT Name, DisplayName, Description, State, StartMode, PathName, ProcessId FROM Win32_Service", out var err, 60);
        if (err is not null) _log.Warn("Services", err);
        var memByPid = new Dictionary<int, long>();
        foreach (var p in Process.GetProcesses())
        {
            try { memByPid[p.Id] = p.WorkingSet64; } catch { }
            finally { p.Dispose(); }
        }
        var pidCounts = rows.GroupBy(r => r.Int("ProcessId") ?? 0).ToDictionary(g => g.Key, g => g.Count());

        var list = new List<ServiceEntry>();
        foreach (var o in rows)
        {
            var name = o.Str("Name") ?? "";
            var pid = o.Int("ProcessId") ?? 0;
            var mode = ServiceStartHandler.ReadStartMode(name, out _, out var raw);
            var e = new ServiceEntry
            {
                Name = name,
                DisplayName = o.Str("DisplayName") ?? name,
                Description = o.Str("Description"),
                State = o.Str("State") ?? "",
                StartMode = mode,
                StartModeDisplay = mode?.ToString() ?? o.Str("StartMode") ?? $"Start={raw}",
                PathName = o.Str("PathName"),
                ProcessId = pid,
                IsSharedHost = pid != 0 && pidCounts.GetValueOrDefault(pid) > 1,
                WorkingSetBytes = pid != 0 && memByPid.TryGetValue(pid, out var ws) ? (ulong)ws : null
            };
            e.IsPerUserTemplate = IsPerUserService(name);
            e.Catalog = Catalog.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (SafetyPolicy.IsServiceProtected(name))
            {
                e.Category = ServiceCategory.Essential;
                e.IsProtected = true;
                e.RecommendedAction = "Protected — never modified by this app";
            }
            else if (e.IsPerUserTemplate)
            {
                e.Category = ServiceCategory.Unknown;
                e.IsProtected = true;
                e.RecommendedAction = "Per-user service instance — managed by Windows";
            }
            else if (e.Catalog is not null)
            {
                e.Category = e.Catalog.Category;
                e.RecommendedAction = e.Catalog.SuggestedStartMode is { } s && s != mode
                    ? $"Optional: set to {s} if you don't need it"
                    : e.Catalog.Category is ServiceCategory.Recommended ? "Keep enabled" : "Leave unchanged unless you understand the side effects";
            }
            else if (IsThirdParty(e.PathName, windowsDir))
            {
                e.Category = ServiceCategory.ApplicationDependent;
                e.RecommendedAction = "Belongs to installed software — set to Manual only if you no longer use that software";
            }
            else
            {
                e.Category = ServiceCategory.Unknown;
                e.IsProtected = true;
                e.RecommendedAction = "Windows service without curated information — not modified (unknown ≠ safe)";
            }
            list.Add(e);
        }
        return list.OrderBy(s => s.Category).ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsThirdParty(string? pathName, string windowsDir)
    {
        if (string.IsNullOrWhiteSpace(pathName)) return false;
        var exe = StartupManagerService.ExtractExecutable(pathName) ?? pathName;
        return !exe.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Per-user services have SERVICE_USER_SERVICE (0x40) or SERVICE_USERSERVICE_INSTANCE (0x80) in their type.</summary>
    private static bool IsPerUserService(string name)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
            return k?.GetValue("Type") is int t && ((t & 0x40) != 0 || (t & 0x80) != 0);
        }
        catch { return false; }
    }

    public (List<string> DependsOn, List<string> Dependents) GetDependencies(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            var on = sc.ServicesDependedOn.Select(s => $"{s.DisplayName} ({s.ServiceName})").ToList();
            var dep = sc.DependentServices.Select(s => $"{s.DisplayName} ({s.ServiceName}) – {s.Status}").ToList();
            return (on, dep);
        }
        catch (Exception ex)
        {
            _log.Debug("Services", ex.Message);
            return (new(), new());
        }
    }
}
