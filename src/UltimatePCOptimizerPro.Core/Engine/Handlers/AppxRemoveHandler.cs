using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;
using Windows.Management.Deployment;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

/// <summary>
/// Removes an Appx/MSIX package for the current user only (no deprovisioning, other users are unaffected).
/// Rollback re-registers the package from its install folder when Windows still has the files; otherwise the app
/// must be reinstalled from the Microsoft Store — therefore this handler never claims a verified rollback.
/// </summary>
public sealed class AppxRemoveHandler : ChangeHandler<AppxRemoveSpec>
{
    protected override bool RequiresAdmin(AppxRemoveSpec s) => false;
    protected override bool HasVerifiedRollback(AppxRemoveSpec s) => false;

    private static Windows.ApplicationModel.Package? Find(AppxRemoveSpec s)
    {
        var pm = new PackageManager();
        return pm.FindPackagesForUser(string.Empty, s.PackageFamilyName).FirstOrDefault();
    }

    protected override Preflight Preflight(AppxRemoveSpec s)
    {
        if (SafetyPolicy.IsPackageProtected(s.PackageFamilyName, out var reason)) return Models.Preflight.Unsupported($"Protected package: {reason}.");
        var p = Find(s);
        if (p is null) return new Preflight { Current = "Not installed", AlreadyApplied = true };
        if (p.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System) return Models.Preflight.Unsupported("System-signed package; Windows does not allow removing it.");
        return new Preflight { Current = $"Installed ({p.Id.FullName})" };
    }

    protected override BackupState Capture(AppxRemoveSpec s)
    {
        var p = Find(s) ?? throw new InvalidOperationException("Package is not installed.");
        string? location = null;
        try { location = p.InstalledLocation?.Path; } catch { }
        var b = new BackupState { Display = $"Installed: {p.Id.FullName}" };
        b.Set("fullName", p.Id.FullName);
        b.Set("installLocation", location);
        return b;
    }

    protected override void Apply(AppxRemoveSpec s, BackupState backup)
    {
        var pm = new PackageManager();
        var result = pm.RemovePackageAsync(backup.Get("fullName") ?? s.PackageFullName).AsTask().GetAwaiter().GetResult();
        if (result.ExtendedErrorCode is not null) throw new InvalidOperationException($"Removal failed: {result.ErrorText}");
    }

    protected override VerifyResult Verify(AppxRemoveSpec s, BackupState backup)
        => Find(s) is null ? VerifyResult.Match("Removed") : VerifyResult.Fail("Still installed");

    protected override void Restore(AppxRemoveSpec s, BackupState b)
    {
        var location = b.Get("installLocation");
        var manifest = location is null ? null : Path.Combine(location, "AppxManifest.xml");
        if (manifest is null || !File.Exists(manifest))
            throw new InvalidOperationException($"The package files are no longer on this PC. Reinstall '{s.DisplayName}' from the Microsoft Store.");
        var pm = new PackageManager();
        var result = pm.RegisterPackageAsync(new Uri(manifest), null, DeploymentOptions.None).AsTask().GetAwaiter().GetResult();
        if (result.ExtendedErrorCode is not null) throw new InvalidOperationException($"Re-registration failed: {result.ErrorText}");
    }

    protected override VerifyResult VerifyRestored(AppxRemoveSpec s, BackupState b)
        => Find(s) is not null ? VerifyResult.Match("Installed") : VerifyResult.Fail("Not installed");
}
