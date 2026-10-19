using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

/// <summary>Enables/disables a startup item via the Explorer "StartupApproved" keys, exactly like Task Manager.</summary>
public sealed class StartupEntryHandler : ChangeHandler<StartupEntrySpec>
{
    protected override bool RequiresAdmin(StartupEntrySpec s) => StartupManagerService.ApprovedKey(s.Location).Hive == RegistryHive.LocalMachine;

    private static byte[]? ReadApproved(StartupEntrySpec s)
    {
        var (hive, sub) = StartupManagerService.ApprovedKey(s.Location);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var k = root.OpenSubKey(sub);
        return k?.GetValue(s.EntryName) as byte[];
    }

    private static bool EntryExists(StartupEntrySpec s)
    {
        if (StartupManagerService.RunKey(s.Location) is { } rk)
        {
            using var root = RegistryKey.OpenBaseKey(rk.Hive, rk.View);
            using var k = root.OpenSubKey(rk.Path);
            return k?.GetValue(s.EntryName) is not null;
        }
        var folder = StartupManagerService.FolderPath(s.Location);
        return folder is not null && File.Exists(Path.Combine(folder, s.EntryName));
    }

    protected override Preflight Preflight(StartupEntrySpec s)
    {
        if (!EntryExists(s)) return Models.Preflight.Unsupported("The startup entry no longer exists.");
        bool enabled = StartupManagerService.IsApproved(s.Location, s.EntryName);
        return new Preflight { Current = enabled ? "Enabled" : "Disabled", AlreadyApplied = enabled == s.Enabled, RequiresAdmin = RequiresAdmin(s) };
    }

    protected override BackupState Capture(StartupEntrySpec s)
    {
        var data = ReadApproved(s);
        var b = new BackupState { Display = StartupManagerService.IsApproved(s.Location, s.EntryName) ? "Enabled" : "Disabled" };
        b.Set("approvedExisted", (data is not null).ToString());
        b.Set("approvedData", data is null ? null : Convert.ToHexString(data));
        return b;
    }

    private static void WriteApproved(StartupEntrySpec s, byte[]? data)
    {
        var (hive, sub) = StartupManagerService.ApprovedKey(s.Location);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        if (data is null)
        {
            using var k = root.OpenSubKey(sub, writable: true);
            k?.DeleteValue(s.EntryName, throwOnMissingValue: false);
            return;
        }
        using var key = root.CreateSubKey(sub, writable: true) ?? throw new IOException("Cannot open StartupApproved key.");
        key.SetValue(s.EntryName, data, RegistryValueKind.Binary);
    }

    protected override void Apply(StartupEntrySpec s, BackupState backup)
    {
        var data = new byte[12];
        if (s.Enabled) data[0] = 0x02;
        else
        {
            data[0] = 0x03;
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        }
        WriteApproved(s, data);
    }

    protected override VerifyResult Verify(StartupEntrySpec s, BackupState backup)
    {
        bool enabled = StartupManagerService.IsApproved(s.Location, s.EntryName);
        return enabled == s.Enabled ? VerifyResult.Match(enabled ? "Enabled" : "Disabled") : VerifyResult.Fail(enabled ? "Enabled" : "Disabled");
    }

    protected override void Restore(StartupEntrySpec s, BackupState b)
        => WriteApproved(s, b.GetBool("approvedExisted") ? Convert.FromHexString(b.Get("approvedData")!) : null);

    protected override VerifyResult VerifyRestored(StartupEntrySpec s, BackupState b)
    {
        var data = ReadApproved(s);
        bool ok = b.GetBool("approvedExisted") ? data is not null && Convert.ToHexString(data) == b.Get("approvedData") : data is null;
        return ok ? VerifyResult.Match(b.Display) : VerifyResult.Fail(data is null ? "(none)" : Convert.ToHexString(data));
    }
}
