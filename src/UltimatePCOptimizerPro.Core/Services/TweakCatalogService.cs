using UltimatePCOptimizerPro.Core.Engine;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Loads the tweak database (Data/Tweaks/*.json) and optimisation profiles (Profiles/*.json).</summary>
public sealed class TweakCatalogService
{
    private readonly LoggingService _log;
    public IReadOnlyList<TweakDefinition> Tweaks { get; }
    public IReadOnlyList<OptimizationProfile> Profiles { get; }
    public IReadOnlyList<string> LoadErrors { get; }

    public TweakCatalogService(LoggingService log)
    {
        _log = log;
        var errors = new List<string>();
        var tweaks = new List<TweakDefinition>();
        foreach (var name in JsonDefaults.EmbeddedNames(".Data.Tweaks."))
        {
            try
            {
                var list = JsonDefaults.ReadEmbedded<List<TweakDefinition>>(name) ?? new();
                tweaks.AddRange(list);
            }
            catch (Exception ex)
            {
                errors.Add($"{name}: {ex.Message}");
                _log.Error("Catalog", $"Failed to load {name}", ex);
            }
        }
        Tweaks = tweaks.OrderBy(t => t.Category).ThenBy(t => t.Name).ToList();

        var profiles = new List<OptimizationProfile>();
        foreach (var name in JsonDefaults.EmbeddedNames(".Profiles."))
        {
            try
            {
                if (JsonDefaults.ReadEmbedded<OptimizationProfile>(name) is { } p) profiles.Add(p);
            }
            catch (Exception ex) { errors.Add($"{name}: {ex.Message}"); }
        }
        string[] order = { "safe", "lowend", "balanced", "highend", "extreme" };
        Profiles = profiles.OrderBy(p => Array.IndexOf(order, p.Id) is var i && i < 0 ? 99 : i).ToList();
        LoadErrors = errors.Concat(Validate()).ToList();
        foreach (var e in LoadErrors) _log.Warn("Catalog", e);
    }

    public TweakDefinition? Get(string id) => Tweaks.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    public OptimizationProfile? GetProfile(string id) => Profiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Structural validation, also exercised by the unit tests.</summary>
    public IEnumerable<string> Validate()
    {
        foreach (var dup in Tweaks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            yield return $"Duplicate tweak id {dup.Key}";
        foreach (var t in Tweaks)
        {
            if (string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Description) || string.IsNullOrWhiteSpace(t.Mechanism)
                || string.IsNullOrWhiteSpace(t.Benefit) || string.IsNullOrWhiteSpace(t.Drawback))
                yield return $"{t.Id}: missing documentation fields";
            if (t.Actions.Count == 0) yield return $"{t.Id}: no actions";
            foreach (var a in t.Actions)
            {
                switch (a)
                {
                    case RegistryValueSpec r:
                        if (!SafetyPolicy.IsRegistryWriteAllowed(r.Hive, r.Path, r.Name, out var why)) yield return $"{t.Id}: {why}";
                        if (!r.DeleteValue && r.Value is null) yield return $"{t.Id}: registry action without value";
                        break;
                    case RegistryTokenSpec tk:
                        if (!SafetyPolicy.IsRegistryWriteAllowed(tk.Hive, tk.Path, tk.Name, out var why2)) yield return $"{t.Id}: {why2}";
                        break;
                    case ServiceStartSpec s when SafetyPolicy.IsServiceProtected(s.ServiceName):
                        yield return $"{t.Id}: targets protected service {s.ServiceName}";
                        break;
                }
            }
        }
        foreach (var p in Profiles)
            foreach (var id in p.Tweaks.Where(id => Get(id) is null))
                yield return $"Profile {p.Id} references unknown tweak {id}";
    }

    // ---------------------------------------------------------------------------- custom user profiles

    public sealed class CustomProfile
    {
        public string Name { get; set; } = "";
        public List<string> Tweaks { get; set; } = new();
        public DateTimeOffset Saved { get; set; } = DateTimeOffset.Now;
    }

    public void SaveCustomProfile(string name, IEnumerable<string> tweakIds)
    {
        var safe = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        if (safe.Length == 0) throw new ArgumentException("Profile name is empty.");
        JsonDefaults.WriteFile(Path.Combine(AppPaths.UserProfiles, safe + ".json"), new CustomProfile { Name = name, Tweaks = tweakIds.ToList() });
    }

    public List<CustomProfile> LoadCustomProfiles()
        => Directory.EnumerateFiles(AppPaths.UserProfiles, "*.json")
            .Select(f => { try { return JsonDefaults.ReadFile<CustomProfile>(f); } catch { return null; } })
            .Where(p => p is not null).Cast<CustomProfile>().OrderBy(p => p.Name).ToList();
}

/// <summary>Decides whether a tweak applies to this PC and turns definitions into concrete engine changes.</summary>
public static class TweakResolver
{
    public static (bool Applicable, string Reason) CheckApplicability(TweakDefinition t, SystemSnapshot? s)
    {
        var c = t.Conditions;
        if (s is null) return (true, "");
        if (c.MinBuild > 0 && s.Os.Build < c.MinBuild) return (false, $"Requires Windows build {c.MinBuild} or newer (this PC: {s.Os.Build}).");
        if (c.MaxBuild > 0 && s.Os.Build > c.MaxBuild) return (false, $"Only applies up to Windows build {c.MaxBuild} (this PC: {s.Os.Build}); newer Windows handles this differently.");
        if (c.DesktopOnly && s.IsLaptop) return (false, "Desktop-only: on laptops this would reduce battery life or conflict with OEM power management.");
        if (c.LaptopOnly && !s.IsLaptop) return (false, "Laptop-only.");
        if (c.RequiresDedicatedGpu && !s.HasDedicatedGpu) return (false, "Requires a dedicated GPU.");
        if (c.RequiresSsdSystemDrive && s.Storage.SystemDisk?.MediaKind != StorageMediaKind.Ssd) return (false, "Only recommended when Windows is on an SSD.");
        if (c.RequiresHybridCpu && !s.Cpu.IsHybrid) return (false, "Requires a hybrid (P-core/E-core) CPU.");
        if (c.RequiresActiveNetworkAdapter && s.Network.Active is null) return (false, "No active network adapter.");
        if (c.RequiresGpuVendor is { } v && s.Gpus.All(g => g.Vendor != v)) return (false, $"Requires a {v} GPU.");
        if (c.RequiresSoftware is { } sw && !HardwareDetectionService.EnumerateInstalledPrograms().Any(p => p.Name.Contains(sw, StringComparison.OrdinalIgnoreCase)))
            return (false, $"'{sw}' is not installed.");
        return (true, "");
    }

    /// <summary>Clones the tweak's action specs and resolves tokens such as {ActiveInterfaceGuid}.</summary>
    public static List<ChangeSpec>? ResolveActions(TweakDefinition t, SystemSnapshot? s, out string? error)
    {
        error = null;
        var list = new List<ChangeSpec>();
        var active = s?.Network.Active;
        foreach (var a in t.Actions)
        {
            var spec = a.Clone();
            switch (spec)
            {
                case RegistryValueSpec r when r.Path.Contains("{ActiveInterfaceGuid}"):
                    if (active is null) { error = "No active network adapter."; return null; }
                    r.Path = r.Path.Replace("{ActiveInterfaceGuid}", active.Id);
                    break;
                case DevicePowerSpec d when d.DeviceInstanceId == "{ActiveNetworkAdapter}":
                    if (active?.DeviceInstanceId is null) { error = "The active network adapter's device id is unknown."; return null; }
                    d.DeviceInstanceId = active.DeviceInstanceId;
                    d.DeviceName = active.Description;
                    break;
            }
            list.Add(spec);
        }
        return list;
    }

    public static List<PlannedChange>? ToPlannedChanges(TweakDefinition t, SystemSnapshot? s, out string? error)
    {
        var specs = ResolveActions(t, s, out error);
        return specs?.Select((spec, i) => new PlannedChange
        {
            Title = specs.Count > 1 ? $"{t.Name} ({i + 1}/{specs.Count})" : t.Name,
            TweakId = t.Id,
            Category = t.Category.ToString(),
            Risk = t.Risk,
            Restart = t.Restart,
            Spec = spec
        }).ToList();
    }
}

/// <summary>Reads the live state of tweaks so cards show the real current value.</summary>
public sealed class TweakStatusService
{
    private readonly HandlerRegistry _handlers;
    private readonly BackupService _backups;

    public TweakStatusService(HandlerRegistry handlers, BackupService backups)
    {
        _handlers = handlers;
        _backups = backups;
    }

    public Dictionary<string, TweakStatus> EvaluateAll(IEnumerable<TweakDefinition> tweaks, SystemSnapshot? s)
    {
        var backed = _backups.TweaksWithActiveBackups();
        return tweaks.ToDictionary(t => t.Id, t =>
        {
            var st = Evaluate(t, s);
            st.HasAppBackup = backed.Contains(t.Id);
            return st;
        });
    }

    public TweakStatus Evaluate(TweakDefinition t, SystemSnapshot? s)
    {
        var status = new TweakStatus();
        var (ok, reason) = TweakResolver.CheckApplicability(t, s);
        var specs = TweakResolver.ResolveActions(t, s, out var err);
        if (specs is not null)
        {
            foreach (var spec in specs)
            {
                status.Locations.Add(spec.Target);
                status.TargetValues.Add(spec.Desired);
            }
        }
        if (!ok || specs is null)
        {
            status.State = TweakState.Unsupported;
            status.Message = ok ? err ?? "Not applicable" : reason;
            return status;
        }
        try
        {
            int applied = 0, unsupported = 0;
            foreach (var spec in specs)
            {
                var pre = _handlers.Get(spec).Preflight(spec);
                status.CurrentValues.Add(pre.Current);
                if (!pre.Supported) { unsupported++; status.Message = pre.Message; }
                else if (pre.AlreadyApplied) applied++;
            }
            status.State = unsupported > 0 ? TweakState.Unsupported
                : applied == specs.Count ? TweakState.Applied
                : applied == 0 ? TweakState.NotApplied : TweakState.Partial;
        }
        catch (Exception ex)
        {
            status.State = TweakState.Error;
            status.Message = ex.Message;
        }
        return status;
    }
}
