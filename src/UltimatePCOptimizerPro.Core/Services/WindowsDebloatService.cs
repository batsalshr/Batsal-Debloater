using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;
using Windows.Management.Deployment;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Detects installed Appx/MSIX packages, scheduled tasks and optional features for the debloater.</summary>
public sealed class WindowsDebloatService
{
    private readonly LoggingService _log;
    public IReadOnlyList<AppCatalogEntry> AppCatalog { get; }
    public IReadOnlyList<ScheduledTaskCatalogEntry> TaskCatalog { get; }
    public IReadOnlyList<OptionalFeatureCatalogEntry> FeatureCatalog { get; }

    public WindowsDebloatService(LoggingService log)
    {
        _log = log;
        AppCatalog = JsonDefaults.ReadEmbedded<List<AppCatalogEntry>>("Debloat.Apps.json") ?? new();
        TaskCatalog = JsonDefaults.ReadEmbedded<List<ScheduledTaskCatalogEntry>>("Debloat.Tasks.json") ?? new();
        FeatureCatalog = JsonDefaults.ReadEmbedded<List<OptionalFeatureCatalogEntry>>("Debloat.Features.json") ?? new();
    }

    public Task<List<InstalledApp>> GetInstalledAppsAsync() => Task.Run(GetInstalledApps);

    /// <summary>Lists the current user's packages (frameworks/resource packages excluded) with removability.</summary>
    public List<InstalledApp> GetInstalledApps()
    {
        var list = new List<InstalledApp>();
        var pm = new PackageManager();
        foreach (var p in pm.FindPackagesForUser(string.Empty))
        {
            try
            {
                if (p.IsFramework || p.IsResourcePackage) continue;
                var app = new InstalledApp
                {
                    Name = p.Id.Name,
                    FullName = p.Id.FullName,
                    FamilyName = p.Id.FamilyName,
                    Version = $"{p.Id.Version.Major}.{p.Id.Version.Minor}.{p.Id.Version.Build}.{p.Id.Version.Revision}",
                    SignatureKind = p.SignatureKind.ToString()
                };
                try { app.DisplayName = string.IsNullOrWhiteSpace(p.DisplayName) ? p.Id.Name : p.DisplayName; } catch { app.DisplayName = p.Id.Name; }
                try { app.Publisher = p.PublisherDisplayName; } catch { app.Publisher = p.Id.Publisher; }
                try { app.InstallLocation = p.InstalledLocation?.Path; } catch { }
                try { app.InstalledDate = p.InstalledDate; } catch { }

                app.Catalog = AppCatalog.FirstOrDefault(c => p.Id.Name.Equals(c.Match, StringComparison.OrdinalIgnoreCase)
                                                             || p.Id.Name.StartsWith(c.Match + ".", StringComparison.OrdinalIgnoreCase));
                if (app.Catalog is not null && !string.IsNullOrEmpty(app.Catalog.DisplayName) && app.DisplayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                    app.DisplayName = app.Catalog.DisplayName;

                if (p.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System)
                {
                    app.IsProtected = true;
                    app.ProtectionReason = "System component (not removable)";
                }
                else if (SafetyPolicy.IsPackageProtected(p.Id.Name, out var reason))
                {
                    app.IsProtected = true;
                    app.ProtectionReason = reason;
                }
                list.Add(app);
            }
            catch (Exception ex)
            {
                _log.Debug("Debloat", $"Skipping package: {ex.Message}");
            }
        }
        return list.OrderBy(a => a.IsProtected).ThenBy(a => a.Catalog is null).ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool InPreset(InstalledApp app, DebloatPreset preset) => !app.IsProtected && app.Catalog?.Presets.Contains(preset) == true;

    // -------------------------------------------------------------------------------------- scheduled tasks

    public List<ScheduledTaskState> GetTaskStates()
    {
        var list = new List<ScheduledTaskState>();
        foreach (var e in TaskCatalog)
        {
            var en = Engine.Handlers.ScheduledTaskHandler.ReadEnabled(e.TaskPath);
            list.Add(new ScheduledTaskState { Entry = e, Exists = en is not null, Enabled = en });
        }
        return list;
    }

    /// <summary>Third-party tasks triggered at logon (a common hidden startup mechanism).</summary>
    public List<LogonTaskEntry> GetLogonTasks()
    {
        const string script =
            "Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\\Microsoft\\*' -and ($_.Triggers | Where-Object { $_.CimClass.CimClassName -in 'MSFT_TaskLogonTrigger','MSFT_TaskBootTrigger' }) } | " +
            "ForEach-Object { [pscustomobject]@{ TaskPath = ($_.TaskPath + $_.TaskName); Author = $_.Author; Description = $_.Description; Enabled = ($_.State -ne 'Disabled'); Action = (($_.Actions | ForEach-Object { $_.Execute }) -join '; ') } } | ConvertTo-Json -Depth 3";
        var r = ProcessRunner.PowerShell(script, 60000);
        if (!r.Success || string.IsNullOrWhiteSpace(r.StdOut)) return new();
        try
        {
            var text = r.StdOut.Trim();
            if (text.StartsWith('{')) text = "[" + text + "]";
            return System.Text.Json.JsonSerializer.Deserialize<List<LogonTaskEntry>>(text, JsonDefaults.Compact) ?? new();
        }
        catch (Exception ex)
        {
            _log.Warn("Debloat", "Could not parse logon tasks", ex);
            return new();
        }
    }

    // -------------------------------------------------------------------------------------- optional features

    public List<OptionalFeatureState> GetOptionalFeatures(bool includeAll)
    {
        var rows = Wmi.TryQuery(Wmi.Cimv2, "SELECT Name, Caption, InstallState FROM Win32_OptionalFeature", out var err, 60);
        if (err is not null) _log.Warn("Debloat", "Optional features unavailable: " + err);
        var list = new List<OptionalFeatureState>();
        foreach (var o in rows)
        {
            var name = o.Str("Name") ?? "";
            var cat = FeatureCatalog.FirstOrDefault(c => c.FeatureName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (cat is null && !includeAll) continue;
            list.Add(new OptionalFeatureState { FeatureName = name, Caption = o.Str("Caption") ?? name, InstallState = o.Int("InstallState") ?? 4, Catalog = cat });
        }
        return list.OrderBy(f => f.Catalog is null).ThenBy(f => f.Caption).ToList();
    }
}
