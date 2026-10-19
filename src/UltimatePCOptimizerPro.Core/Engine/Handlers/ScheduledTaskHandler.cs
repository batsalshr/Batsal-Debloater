using System.Xml.Linq;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

/// <summary>Enables/disables a scheduled task with schtasks.exe; state is read from the task's XML (language independent).</summary>
public sealed class ScheduledTaskHandler : ChangeHandler<ScheduledTaskSpec>
{
    private static readonly string[] ProtectedTaskPrefixes =
    {
        @"\Microsoft\Windows\Windows Defender", @"\Microsoft\Windows\WindowsUpdate", @"\Microsoft\Windows\UpdateOrchestrator",
        @"\Microsoft\Windows\WaaSMedic", @"\Microsoft\Windows\SystemRestore", @"\Microsoft\Windows\Servicing", @"\Microsoft\Windows\TPM",
        @"\Microsoft\Windows\BitLocker", @"\Microsoft\Windows\Time Synchronization", @"\Microsoft\Windows\Defrag", @"\Microsoft\Windows\TaskScheduler",
        @"\Microsoft\Windows\SoftwareProtectionPlatform", @"\Microsoft\Windows\Plug and Play", @"\Microsoft\Windows\Chkdsk"
    };

    public static bool IsProtected(string path) => ProtectedTaskPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    protected override bool RequiresAdmin(ScheduledTaskSpec s) => s.TaskPath.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns null when the task does not exist.</summary>
    public static bool? ReadEnabled(string taskPath)
    {
        var r = ProcessRunner.Run(ProcessRunner.System32("schtasks.exe"), $"/Query /TN \"{taskPath}\" /XML", 20000);
        if (!r.Success || !r.StdOut.Contains("<Task", StringComparison.Ordinal)) return null;
        try
        {
            var xml = r.StdOut[r.StdOut.IndexOf('<')..];
            var doc = XDocument.Parse(xml);
            var enabled = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Enabled" && e.Parent?.Name.LocalName == "Settings");
            return enabled is null || !enabled.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
        }
        catch { return null; }
    }

    protected override Preflight Preflight(ScheduledTaskSpec s)
    {
        if (IsProtected(s.TaskPath)) return Models.Preflight.Unsupported("This task belongs to security, update or maintenance infrastructure and is protected.");
        var en = ReadEnabled(s.TaskPath);
        if (en is null) return Models.Preflight.Unsupported("Task not present on this version of Windows.");
        return new Preflight { Current = en.Value ? "Enabled" : "Disabled", AlreadyApplied = en == s.Enabled, RequiresAdmin = RequiresAdmin(s) };
    }

    protected override BackupState Capture(ScheduledTaskSpec s)
    {
        var en = ReadEnabled(s.TaskPath) ?? throw new InvalidOperationException("Task not found.");
        var b = new BackupState { Display = en ? "Enabled" : "Disabled" };
        b.Set("enabled", en.ToString());
        return b;
    }

    private static void Set(string path, bool enabled)
    {
        var r = ProcessRunner.Run(ProcessRunner.System32("schtasks.exe"), $"/Change /TN \"{path}\" {(enabled ? "/ENABLE" : "/DISABLE")}", 20000);
        if (!r.Success) throw new InvalidOperationException($"schtasks failed ({r.ExitCode}): {r.Combined}");
    }

    protected override void Apply(ScheduledTaskSpec s, BackupState backup) => Set(s.TaskPath, s.Enabled);

    protected override VerifyResult Verify(ScheduledTaskSpec s, BackupState backup)
    {
        var en = ReadEnabled(s.TaskPath);
        return en == s.Enabled ? VerifyResult.Match(en.Value ? "Enabled" : "Disabled") : VerifyResult.Fail(en?.ToString() ?? "missing");
    }

    protected override void Restore(ScheduledTaskSpec s, BackupState b) => Set(s.TaskPath, b.GetBool("enabled"));

    protected override VerifyResult VerifyRestored(ScheduledTaskSpec s, BackupState b)
    {
        var en = ReadEnabled(s.TaskPath);
        return en == b.GetBool("enabled") ? VerifyResult.Match(b.Display) : VerifyResult.Fail(en?.ToString() ?? "missing");
    }
}
