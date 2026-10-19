using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

/// <summary>
/// Adds a compatibility layer (e.g. DISABLEDXMAXIMIZEDWINDOWEDMODE = "Disable fullscreen optimizations") for one
/// executable, exactly as the Compatibility tab of the file's Properties dialog does.
/// </summary>
public sealed class AppCompatLayerHandler : ChangeHandler<AppCompatLayerSpec>
{
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    protected override bool RequiresAdmin(AppCompatLayerSpec s) => false;

    private static string? ReadValue(AppCompatLayerSpec s) => RegistryHelper.Read("HKCU", "Default", LayersKey, s.ExePath).Serialized;

    private static bool HasFlag(string? value, string flag)
        => value?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(flag, StringComparer.OrdinalIgnoreCase) == true;

    protected override Preflight Preflight(AppCompatLayerSpec s)
    {
        if (!File.Exists(s.ExePath)) return Models.Preflight.Unsupported($"Executable not found: {s.ExePath}");
        var v = ReadValue(s);
        return new Preflight { Current = v is null ? "(no compatibility flags)" : $"\"{v}\"", AlreadyApplied = HasFlag(v, s.Flag) };
    }

    protected override BackupState Capture(AppCompatLayerSpec s)
    {
        var v = ReadValue(s);
        var b = new BackupState { Display = v is null ? "(no compatibility flags)" : $"\"{v}\"" };
        b.Set("valueExisted", (v is not null).ToString());
        b.Set("data", v);
        return b;
    }

    protected override void Apply(AppCompatLayerSpec s, BackupState backup)
    {
        var v = ReadValue(s);
        var parts = (v ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!parts.Contains("~")) parts.Insert(0, "~");
        if (!parts.Contains(s.Flag, StringComparer.OrdinalIgnoreCase)) parts.Add(s.Flag);
        RegistryHelper.Write("HKCU", "Default", LayersKey, s.ExePath, RegistryValueKind.String, string.Join(' ', parts));
    }

    protected override VerifyResult Verify(AppCompatLayerSpec s, BackupState backup)
    {
        var v = ReadValue(s);
        return HasFlag(v, s.Flag) ? VerifyResult.Match(v ?? "") : VerifyResult.Fail(v ?? "(none)");
    }

    protected override void Restore(AppCompatLayerSpec s, BackupState b)
    {
        if (b.GetBool("valueExisted")) RegistryHelper.Write("HKCU", "Default", LayersKey, s.ExePath, RegistryValueKind.String, b.Get("data") ?? "");
        else RegistryHelper.DeleteValue("HKCU", "Default", LayersKey, s.ExePath);
    }

    protected override VerifyResult VerifyRestored(AppCompatLayerSpec s, BackupState b)
    {
        var v = ReadValue(s);
        bool ok = b.GetBool("valueExisted") ? v == b.Get("data") : v is null;
        return ok ? VerifyResult.Match(v ?? "(none)") : VerifyResult.Fail(v ?? "(none)");
    }
}
