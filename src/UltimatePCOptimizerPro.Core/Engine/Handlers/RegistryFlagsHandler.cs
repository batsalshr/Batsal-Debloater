using System.Globalization;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

/// <summary>
/// Sets/clears individual bits of a numeric flags value (e.g. Control Panel\Accessibility\StickyKeys\Flags) so that
/// unrelated bits — such as an accessibility feature the user relies on — are preserved.
/// </summary>
public sealed class RegistryFlagsHandler : ChangeHandler<RegistryFlagsSpec>
{
    protected override bool RequiresAdmin(RegistryFlagsSpec s) => s.Hive == "HKLM";

    private static (bool Exists, RegistryValueKind Kind, long Value, string? Raw) Read(RegistryFlagsSpec s)
    {
        var snap = RegistryHelper.Read(s.Hive, "Default", s.Path, s.Name);
        if (!snap.ValueExists) return (false, RegistryValueKind.String, s.DefaultValue, null);
        long v = long.TryParse(snap.Serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : s.DefaultValue;
        return (true, snap.Kind, v, snap.Serialized);
    }

    private static bool IsTarget(RegistryFlagsSpec s, long v) => (v & s.SetBits) == s.SetBits && (v & s.ClearBits) == 0;

    protected override Preflight Preflight(RegistryFlagsSpec s)
    {
        if (!SafetyPolicy.IsRegistryWriteAllowed(s.Hive, s.Path, s.Name, out var reason)) return Models.Preflight.Unsupported(reason);
        var cur = Read(s);
        return new Preflight
        {
            Current = cur.Exists ? $"{cur.Value} (0x{cur.Value:X})" : $"(not set — default {s.DefaultValue})",
            AlreadyApplied = IsTarget(s, cur.Value),
            RequiresAdmin = RequiresAdmin(s)
        };
    }

    protected override BackupState Capture(RegistryFlagsSpec s)
    {
        var cur = Read(s);
        var b = new BackupState { Display = cur.Exists ? $"{cur.Raw} ({cur.Kind})" : "(value not set)" };
        b.Set("valueExisted", cur.Exists.ToString());
        b.Set("kind", cur.Kind.ToString());
        b.Set("data", cur.Raw);
        return b;
    }

    protected override void Apply(RegistryFlagsSpec s, BackupState backup)
    {
        var cur = Read(s);
        long next = (cur.Value | s.SetBits) & ~s.ClearBits;
        if (cur.Exists && cur.Kind == RegistryValueKind.DWord)
            RegistryHelper.Write(s.Hive, "Default", s.Path, s.Name, RegistryValueKind.DWord, unchecked((int)next));
        else
            RegistryHelper.Write(s.Hive, "Default", s.Path, s.Name, RegistryValueKind.String, next.ToString(CultureInfo.InvariantCulture));
    }

    protected override VerifyResult Verify(RegistryFlagsSpec s, BackupState backup)
    {
        var cur = Read(s);
        return IsTarget(s, cur.Value) ? VerifyResult.Match(cur.Raw ?? "") : VerifyResult.Fail(cur.Raw ?? "");
    }

    protected override void Restore(RegistryFlagsSpec s, BackupState b)
    {
        if (b.GetBool("valueExisted"))
        {
            var kind = Enum.Parse<RegistryValueKind>(b.Get("kind")!);
            RegistryHelper.Write(s.Hive, "Default", s.Path, s.Name, kind, RegistryHelper.Deserialize(kind, b.Get("data") ?? "0"));
        }
        else RegistryHelper.DeleteValue(s.Hive, "Default", s.Path, s.Name);
    }

    protected override VerifyResult VerifyRestored(RegistryFlagsSpec s, BackupState b)
    {
        var snap = RegistryHelper.Read(s.Hive, "Default", s.Path, s.Name);
        bool ok = b.GetBool("valueExisted") ? snap.Serialized == b.Get("data") : !snap.ValueExists;
        return ok ? VerifyResult.Match(snap.Display) : VerifyResult.Fail(snap.Display);
    }
}
