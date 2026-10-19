using System.Text.RegularExpressions;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

public static class PowerSchemes
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    public static Guid For(PowerSchemeSpec s) => s.Scheme switch
    {
        PowerSchemeKind.Balanced => Balanced,
        PowerSchemeKind.HighPerformance => HighPerformance,
        PowerSchemeKind.UltimatePerformance => UltimatePerformance,
        _ => s.CustomGuid ?? throw new ArgumentException("Custom scheme requires a GUID.")
    };

    public static string Name(Guid g) => NativeMethods.GetPowerSchemeName(g) ?? g.ToString();

    public sealed record SchemeInfo(Guid Id, string Name, bool IsActive);

    public static List<SchemeInfo> List()
    {
        var active = NativeMethods.GetActivePowerScheme();
        return NativeMethods.EnumeratePowerSchemes().Select(g => new SchemeInfo(g, Name(g), g == active)).ToList();
    }

    public static Guid? Active() => NativeMethods.GetActivePowerScheme();
}

/// <summary>Switches the active power plan using powrprof.dll; creates High/Ultimate Performance from the built-in template when hidden.</summary>
public sealed class PowerSchemeHandler : ChangeHandler<PowerSchemeSpec>
{
    protected override bool RequiresAdmin(PowerSchemeSpec s) => true;

    protected override Preflight Preflight(PowerSchemeSpec s)
    {
        var active = NativeMethods.GetActivePowerScheme();
        var target = PowerSchemes.For(s);
        return new Preflight
        {
            Current = active is null ? "Unknown" : $"{PowerSchemes.Name(active.Value)} ({active})",
            AlreadyApplied = active == target,
            RequiresAdmin = true,
            Message = NativeMethods.EnumeratePowerSchemes().Contains(target) ? "" : "The plan is hidden on this PC and will be created from the Windows template."
        };
    }

    protected override BackupState Capture(PowerSchemeSpec s)
    {
        var active = NativeMethods.GetActivePowerScheme() ?? throw new InvalidOperationException("Cannot read the active power plan.");
        var b = new BackupState { Display = $"{PowerSchemes.Name(active)} ({active})" };
        b.Set("activeScheme", active.ToString());
        return b;
    }

    protected override void Apply(PowerSchemeSpec s, BackupState backup)
    {
        var target = PowerSchemes.For(s);
        if (!NativeMethods.EnumeratePowerSchemes().Contains(target))
        {
            var r = ProcessRunner.Run(ProcessRunner.System32("powercfg.exe"), $"-duplicatescheme {target}", 30000);
            var m = Regex.Match(r.StdOut, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            if (!r.Success || !m.Success)
                throw new InvalidOperationException("Windows did not allow this power plan to be created (common on Modern Standby laptops). " + r.Combined);
            target = Guid.Parse(m.Value);
            backup.Set("createdScheme", target.ToString());
        }
        var rc = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref target);
        if (rc != 0) throw new System.ComponentModel.Win32Exception((int)rc);
        backup.Set("targetScheme", target.ToString());
    }

    protected override VerifyResult Verify(PowerSchemeSpec s, BackupState backup)
    {
        var active = NativeMethods.GetActivePowerScheme();
        var target = Guid.Parse(backup.Get("targetScheme") ?? PowerSchemes.For(s).ToString());
        return active == target ? VerifyResult.Match(PowerSchemes.Name(target)) : VerifyResult.Fail(active?.ToString() ?? "unknown");
    }

    protected override void Restore(PowerSchemeSpec s, BackupState b)
    {
        var original = Guid.Parse(b.Get("activeScheme")!);
        var rc = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref original);
        if (rc != 0) throw new System.ComponentModel.Win32Exception((int)rc);
        if (b.Get("createdScheme") is { } created && Guid.Parse(created) != original)
            ProcessRunner.Run(ProcessRunner.System32("powercfg.exe"), $"-delete {created}", 30000);
    }

    protected override VerifyResult VerifyRestored(PowerSchemeSpec s, BackupState b)
    {
        var active = NativeMethods.GetActivePowerScheme();
        return active?.ToString() == b.Get("activeScheme") ? VerifyResult.Match(b.Display) : VerifyResult.Fail(active?.ToString() ?? "unknown");
    }
}

/// <summary>Writes an AC/DC value index of a setting in the active power plan (equivalent to powercfg /setacvalueindex).</summary>
public sealed class PowerSettingHandler : ChangeHandler<PowerSettingSpec>
{
    protected override bool RequiresAdmin(PowerSettingSpec s) => true;

    private static (uint? Ac, uint? Dc) Read(Guid scheme, PowerSettingSpec s)
    {
        var sub = s.Subgroup; var set = s.Setting;
        uint? ac = NativeMethods.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, out var a) == 0 ? a : null;
        uint? dc = NativeMethods.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, out var d) == 0 ? d : null;
        return (ac, dc);
    }

    private static bool IsTarget(PowerSettingSpec s, (uint? Ac, uint? Dc) v)
        => (s.AcValue is null || v.Ac == s.AcValue) && (s.DcValue is null || v.Dc == s.DcValue);

    protected override Preflight Preflight(PowerSettingSpec s)
    {
        var scheme = NativeMethods.GetActivePowerScheme();
        if (scheme is null) return Models.Preflight.Unsupported("Active power plan unavailable.");
        var v = Read(scheme.Value, s);
        if (v.Ac is null && v.Dc is null) return Models.Preflight.Unsupported("This power setting does not exist on this PC.");
        return new Preflight { Current = $"AC={v.Ac?.ToString() ?? "?"}, DC={v.Dc?.ToString() ?? "?"} in '{PowerSchemes.Name(scheme.Value)}'", AlreadyApplied = IsTarget(s, v), RequiresAdmin = true };
    }

    protected override BackupState Capture(PowerSettingSpec s)
    {
        var scheme = NativeMethods.GetActivePowerScheme() ?? throw new InvalidOperationException("Active power plan unavailable.");
        var v = Read(scheme, s);
        var b = new BackupState { Display = $"AC={v.Ac}, DC={v.Dc} in '{PowerSchemes.Name(scheme)}'" };
        b.Set("scheme", scheme.ToString());
        b.Set("ac", v.Ac?.ToString());
        b.Set("dc", v.Dc?.ToString());
        return b;
    }

    private static void Write(Guid scheme, PowerSettingSpec s, uint? ac, uint? dc)
    {
        var sub = s.Subgroup; var set = s.Setting;
        if (ac is { } a)
        {
            var rc = NativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, a);
            if (rc != 0) throw new System.ComponentModel.Win32Exception((int)rc);
        }
        if (dc is { } d)
        {
            var rc = NativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, d);
            if (rc != 0) throw new System.ComponentModel.Win32Exception((int)rc);
        }
        // Re-activating the scheme makes the new index take effect immediately.
        if (NativeMethods.GetActivePowerScheme() == scheme) NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
    }

    protected override void Apply(PowerSettingSpec s, BackupState b) => Write(Guid.Parse(b.Get("scheme")!), s, s.AcValue, s.DcValue);

    protected override VerifyResult Verify(PowerSettingSpec s, BackupState b)
    {
        var v = Read(Guid.Parse(b.Get("scheme")!), s);
        return IsTarget(s, v) ? VerifyResult.Match($"AC={v.Ac}, DC={v.Dc}") : VerifyResult.Fail($"AC={v.Ac}, DC={v.Dc}");
    }

    protected override void Restore(PowerSettingSpec s, BackupState b)
    {
        uint? ac = uint.TryParse(b.Get("ac"), out var a) ? a : null;
        uint? dc = uint.TryParse(b.Get("dc"), out var d) ? d : null;
        Write(Guid.Parse(b.Get("scheme")!), s, s.AcValue is null ? null : ac, s.DcValue is null ? null : dc);
    }

    protected override VerifyResult VerifyRestored(PowerSettingSpec s, BackupState b)
    {
        var v = Read(Guid.Parse(b.Get("scheme")!), s);
        bool ok = (s.AcValue is null || v.Ac?.ToString() == b.Get("ac")) && (s.DcValue is null || v.Dc?.ToString() == b.Get("dc"));
        return ok ? VerifyResult.Match($"AC={v.Ac}, DC={v.Dc}") : VerifyResult.Fail($"AC={v.Ac}, DC={v.Dc}");
    }
}
