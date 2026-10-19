using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

public sealed class RegistryValueHandler : ChangeHandler<RegistryValueSpec>
{
    protected override bool RequiresAdmin(RegistryValueSpec s) => s.Hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase);

    protected override Preflight Preflight(RegistryValueSpec s)
    {
        if (!SafetyPolicy.IsRegistryWriteAllowed(s.Hive, s.Path, s.Name, out var reason)) return Models.Preflight.Unsupported(reason);
        var cur = RegistryHelper.Read(s.Hive, s.View, s.Path, s.Name);
        return new Preflight
        {
            Current = !cur.ValueExists && s.WindowsDefault is not null ? $"{cur.Display} — Windows default behaves as {s.WindowsDefault}" : cur.Display,
            AlreadyApplied = IsTarget(s, cur),
            RequiresAdmin = RequiresAdmin(s)
        };
    }

    private static bool IsTarget(RegistryValueSpec s, RegistryHelper.ValueSnapshot cur)
    {
        if (s.DeleteValue) return !cur.ValueExists;
        if (s.Value is null) return false;
        var kind = RegistryHelper.ToKind(s.Kind);
        if (!cur.ValueExists)
            return s.WindowsDefault is not null && RegistryHelper.Normalize(kind, s.WindowsDefault) == RegistryHelper.Normalize(kind, s.Value);
        if (cur.Kind != kind && !(kind is RegistryValueKind.String or RegistryValueKind.ExpandString && cur.Kind is RegistryValueKind.String or RegistryValueKind.ExpandString))
            return false;
        return string.Equals(cur.Serialized, RegistryHelper.Normalize(kind, s.Value), StringComparison.Ordinal);
    }

    protected override BackupState Capture(RegistryValueSpec s)
    {
        var cur = RegistryHelper.Read(s.Hive, s.View, s.Path, s.Name);
        var b = new BackupState { Display = cur.Display };
        b.Set("keyExisted", cur.KeyExists.ToString());
        b.Set("valueExisted", cur.ValueExists.ToString());
        b.Set("kind", cur.ValueExists ? cur.Kind.ToString() : null);
        b.Set("data", cur.Serialized);
        b.Set("createdFrom", RegistryHelper.FirstMissingKey(s.Hive, s.View, s.Path));
        return b;
    }

    protected override void Apply(RegistryValueSpec s, BackupState backup)
    {
        if (!SafetyPolicy.IsRegistryWriteAllowed(s.Hive, s.Path, s.Name, out var reason)) throw new UnauthorizedAccessException(reason);
        if (s.DeleteValue)
        {
            RegistryHelper.DeleteValue(s.Hive, s.View, s.Path, s.Name);
        }
        else
        {
            var kind = RegistryHelper.ToKind(s.Kind);
            RegistryHelper.Write(s.Hive, s.View, s.Path, s.Name, kind, RegistryHelper.Deserialize(kind, RegistryHelper.Normalize(kind, s.Value ?? "")));
        }
        RunRefresh(s.Refresh);
    }

    protected override VerifyResult Verify(RegistryValueSpec s, BackupState backup)
    {
        var cur = RegistryHelper.Read(s.Hive, s.View, s.Path, s.Name);
        return IsTarget(s, cur) ? VerifyResult.Match(cur.Display) : VerifyResult.Fail(cur.Display);
    }

    protected override void Restore(RegistryValueSpec s, BackupState b)
    {
        if (b.GetBool("valueExisted"))
        {
            var kind = Enum.Parse<RegistryValueKind>(b.Get("kind")!);
            RegistryHelper.Write(s.Hive, s.View, s.Path, s.Name, kind, RegistryHelper.Deserialize(kind, b.Get("data") ?? ""));
        }
        else
        {
            RegistryHelper.DeleteValue(s.Hive, s.View, s.Path, s.Name);
            RegistryHelper.DeleteCreatedKeysIfEmpty(s.Hive, s.View, s.Path, b.Get("createdFrom"));
        }
        RunRefresh(s.Refresh);
    }

    protected override VerifyResult VerifyRestored(RegistryValueSpec s, BackupState b)
    {
        var cur = RegistryHelper.Read(s.Hive, s.View, s.Path, s.Name);
        bool ok = b.GetBool("valueExisted")
            ? cur.ValueExists && cur.Serialized == b.Get("data") && cur.Kind.ToString() == b.Get("kind")
            : !cur.ValueExists;
        return ok ? VerifyResult.Match(cur.Display) : VerifyResult.Fail(cur.Display);
    }

    /// <summary>Applies settings that Windows only reads at sign-in to the running session where a documented API exists.</summary>
    private static void RunRefresh(string? refresh)
    {
        if (refresh != "Mouse") return;
        using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
        int Parse(string n) => int.TryParse(k?.GetValue(n) as string, out var v) ? v : 0;
        var values = new[] { Parse("MouseThreshold1"), Parse("MouseThreshold2"), Parse("MouseSpeed") };
        NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETMOUSE, 0, values, NativeMethods.SPIF_SENDCHANGE);
    }
}
