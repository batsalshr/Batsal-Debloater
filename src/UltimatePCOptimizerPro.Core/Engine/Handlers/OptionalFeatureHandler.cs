using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

/// <summary>Enables/disables an optional Windows feature with DISM. Most changes complete only after a restart.</summary>
public sealed class OptionalFeatureHandler : ChangeHandler<OptionalFeatureSpec>
{
    protected override bool RequiresAdmin(OptionalFeatureSpec s) => true;
    protected override bool HasVerifiedRollback(OptionalFeatureSpec s) => false;

    /// <summary>Win32_OptionalFeature.InstallState: 1 Enabled, 2 Disabled, 3 Absent.</summary>
    public static int? ReadState(string feature)
    {
        var rows = Wmi.TryQuery(Wmi.Cimv2, $"SELECT InstallState FROM Win32_OptionalFeature WHERE Name='{Wmi.WqlEscape(feature)}'", out _, 30);
        return rows.Count == 0 ? null : rows[0].Int("InstallState");
    }

    private static string StateName(int? s) => s switch { 1 => "Enabled", 2 => "Disabled", 3 => "Absent", _ => "Unknown" };

    protected override Preflight Preflight(OptionalFeatureSpec s)
    {
        if (!s.Enabled && SafetyPolicy.ProtectedFeatures.Contains(s.FeatureName)) return Models.Preflight.Unsupported("Protected feature.");
        var st = ReadState(s.FeatureName);
        if (st is null) return Models.Preflight.Unsupported("Feature not available on this edition/version of Windows.");
        return new Preflight { Current = StateName(st), AlreadyApplied = s.Enabled ? st == 1 : st is 2 or 3, RequiresAdmin = true };
    }

    protected override BackupState Capture(OptionalFeatureSpec s)
    {
        var st = ReadState(s.FeatureName);
        var b = new BackupState { Display = StateName(st) };
        b.Set("state", st?.ToString());
        return b;
    }

    private static int RunDism(string feature, bool enable)
    {
        var args = enable
            ? $"/Online /Enable-Feature /FeatureName:{feature} /All /NoRestart /Quiet"
            : $"/Online /Disable-Feature /FeatureName:{feature} /NoRestart /Quiet";
        var r = ProcessRunner.Run(ProcessRunner.System32("dism.exe"), args, 20 * 60_000);
        // 3010 = success, restart required
        if (r.ExitCode is not (0 or 3010)) throw new InvalidOperationException($"DISM exited with {r.ExitCode}: {r.Combined}");
        return r.ExitCode;
    }

    protected override void Apply(OptionalFeatureSpec s, BackupState backup) => backup.Set("applyExit", RunDism(s.FeatureName, s.Enabled).ToString());

    protected override VerifyResult Verify(OptionalFeatureSpec s, BackupState backup)
    {
        var st = ReadState(s.FeatureName);
        bool ok = s.Enabled ? st == 1 : st is 2 or 3;
        if (ok) return VerifyResult.Match(StateName(st));
        return backup.Get("applyExit") == "3010" ? VerifyResult.Pending("Restart required to complete") : VerifyResult.Fail(StateName(st));
    }

    protected override void Restore(OptionalFeatureSpec s, BackupState b)
    {
        bool wasEnabled = b.Get("state") == "1";
        b.Set("restoreExit", RunDism(s.FeatureName, wasEnabled).ToString());
    }

    protected override VerifyResult VerifyRestored(OptionalFeatureSpec s, BackupState b)
    {
        var st = ReadState(s.FeatureName);
        if (st?.ToString() == b.Get("state")) return VerifyResult.Match(StateName(st));
        return b.Get("restoreExit") == "3010" ? VerifyResult.Pending("Restart required to complete") : VerifyResult.Fail(StateName(st));
    }
}
