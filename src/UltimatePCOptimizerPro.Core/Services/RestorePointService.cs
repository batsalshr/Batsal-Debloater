using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>
/// Creates and verifies Windows System Restore points through the documented WMI provider
/// (root\default:SystemRestore.CreateRestorePoint, the same call used by Checkpoint-Computer).
///
/// Important: Windows returns success even when it silently skips creation because a restore point already exists
/// within SystemRestorePointCreationFrequency (default 1440 minutes). A request is therefore only reported as
/// "Created" after the new point has been found by enumeration. This service never changes the frequency setting.
/// </summary>
public sealed class RestorePointService
{
    private const int MODIFY_SETTINGS = 12;
    private const int BEGIN_SYSTEM_CHANGE = 100;
    private const string SrKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    private const string SrPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";

    private readonly LoggingService _log;
    public RestorePointService(LoggingService log) => _log = log;

    /// <summary>ULTIMATE-PC-OPTIMIZER | Registry Tweaks | 2026-10-19 | 18-45-32 (local time of the PC).</summary>
    public static string BuildName(string operationLabel, DateTimeOffset localTime)
    {
        var label = string.IsNullOrWhiteSpace(operationLabel) ? "Optimization" : operationLabel.Replace('|', '/').Trim();
        var name = $"{AppPaths.RestorePointPrefix} | {label} | {localTime:yyyy-MM-dd} | {localTime:HH-mm-ss}";
        // SRSetRestorePointW accepts up to 256 characters (MAX_DESC_W).
        return name.Length <= 255 ? name : name[..255];
    }

    public SystemProtectionStatus GetProtectionStatus()
    {
        var st = new SystemProtectionStatus();
        try
        {
            using var policy = Registry.LocalMachine.OpenSubKey(SrPolicyKey);
            if (policy?.GetValue("DisableSR") is int dsr && dsr == 1) st.DisabledByPolicy = true;
            using var k = Registry.LocalMachine.OpenSubKey(SrKey);
            if (k is null)
            {
                st.Supported = false;
                st.Message = "System Restore is not available on this edition of Windows.";
                return st;
            }
            st.Supported = true;
            if (k.GetValue("SystemRestorePointCreationFrequency") is int freq)
            {
                st.FrequencyMinutes = freq;
                st.FrequencyIsDefault = false;
            }
            // RPSessionInterval is 1 when protection is turned on for at least one drive.
            if (k.GetValue("RPSessionInterval") is int interval) st.Enabled = interval != 0;
        }
        catch (Exception ex)
        {
            st.Message = "Could not read System Restore configuration: " + ex.Message;
        }

        try
        {
            st.LastRestorePoint = ListRestorePoints().OrderByDescending(p => p.CreationTime).FirstOrDefault();
            if (st.LastRestorePoint is not null) st.Enabled ??= true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ManagementException or System.Runtime.InteropServices.COMException)
        {
            // Enumeration requires administrator rights.
        }

        if (st.DisabledByPolicy) { st.Enabled = false; st.Message = "System Restore is disabled by Group Policy (DisableSR)."; }
        else if (st.Enabled == false) st.Message = "System Protection is turned off for all drives.";
        else if (st.Enabled == true)
            st.Message = st.FrequencyMinutes <= 0
                ? "System Protection is on. No creation frequency limit is configured (custom setting), so every operation can get its own restore point."
                : $"System Protection is on. Windows allows one new restore point every {st.FrequencyMinutes} minutes" + (st.FrequencyIsDefault ? " (Windows default)." : " (custom setting).");
        return st;
    }

    /// <summary>Enumerates existing restore points (requires administrator rights).</summary>
    public List<SystemRestorePointInfo> ListRestorePoints()
    {
        var list = new List<SystemRestorePointInfo>();
        foreach (var o in Wmi.Query(Wmi.Default, "SELECT * FROM SystemRestore", 30))
        {
            var created = o.Str("CreationTime");
            list.Add(new SystemRestorePointInfo
            {
                SequenceNumber = (uint)(o.Long("SequenceNumber") ?? 0),
                Description = o.Str("Description") ?? "",
                CreationTime = created is null ? DateTime.MinValue : ManagementDateTimeConverter.ToDateTime(created),
                RestorePointType = o.Int("RestorePointType") ?? -1,
                EventType = o.Int("EventType") ?? -1
            });
        }
        return list.OrderByDescending(p => p.CreationTime).ToList();
    }

    public Task<RestorePointAttempt> CreateAsync(string operationLabel, CancellationToken ct = default)
        => Task.Run(() => Create(operationLabel), ct);

    public RestorePointAttempt Create(string operationLabel)
    {
        var now = DateTimeOffset.Now;
        var attempt = new RestorePointAttempt { RequestedAt = now, Description = BuildName(operationLabel, now) };

        if (!Elevation.IsElevated)
        {
            attempt.Status = RestorePointStatus.AccessDenied;
            attempt.Message = "Creating a restore point requires administrator rights. Restart the application as administrator.";
            return attempt;
        }
        var status = GetProtectionStatus();
        if (!status.Supported)
        {
            attempt.Status = RestorePointStatus.Unsupported;
            attempt.Message = status.Message;
            return attempt;
        }
        if (status.DisabledByPolicy)
        {
            attempt.Status = RestorePointStatus.Disabled;
            attempt.Message = status.Message;
            return attempt;
        }

        HashSet<uint> before;
        try { before = ListRestorePoints().Select(p => p.SequenceNumber).ToHashSet(); }
        catch (Exception ex)
        {
            before = new HashSet<uint>();
            _log.Warn("RestorePoint", "Could not enumerate existing restore points before creation", ex);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var cls = new ManagementClass(new ManagementScope(Wmi.Default), new ManagementPath("SystemRestore"), null);
            using var inParams = cls.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = attempt.Description;
            inParams["RestorePointType"] = MODIFY_SETTINGS;
            inParams["EventType"] = BEGIN_SYSTEM_CHANGE;
            using var outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null);
            attempt.ReturnCode = Convert.ToInt32(outParams?["ReturnValue"] ?? -1);
        }
        catch (ManagementException ex)
        {
            attempt.Status = RestorePointStatus.Failed;
            attempt.Message = "Windows rejected the restore point request: " + ex.Message;
            _log.Error("RestorePoint", attempt.Message, ex);
            return attempt;
        }
        catch (UnauthorizedAccessException ex)
        {
            attempt.Status = RestorePointStatus.AccessDenied;
            attempt.Message = ex.Message;
            return attempt;
        }
        _log.Info("RestorePoint", $"CreateRestorePoint returned {attempt.ReturnCode} after {sw.ElapsedMilliseconds} ms for '{attempt.Description}'");

        if (attempt.ReturnCode == 1058)
        {
            attempt.Status = RestorePointStatus.Disabled;
            attempt.Message = "System Protection is turned off (error 1058). Turn it on for the Windows drive in System Properties → System Protection.";
            return attempt;
        }
        if (attempt.ReturnCode != 0)
        {
            attempt.Status = RestorePointStatus.Failed;
            attempt.Message = $"Windows returned error code {attempt.ReturnCode} ({new System.ComponentModel.Win32Exception(attempt.ReturnCode ?? 0).Message}).";
            return attempt;
        }

        // Verify: the new point must exist with our exact description.
        SystemRestorePointInfo? created = null;
        for (int i = 0; i < 5 && created is null; i++)
        {
            try
            {
                created = ListRestorePoints().FirstOrDefault(p => !before.Contains(p.SequenceNumber) && p.Description == attempt.Description);
            }
            catch (Exception ex) { _log.Warn("RestorePoint", "Verification enumeration failed", ex); }
            if (created is null) Thread.Sleep(1000);
        }

        if (created is not null)
        {
            attempt.Status = RestorePointStatus.Created;
            attempt.Verified = true;
            attempt.SequenceNumber = created.SequenceNumber;
            attempt.CreationTime = new DateTimeOffset(created.CreationTime);
            attempt.Message = $"Restore point #{created.SequenceNumber} created and verified.";
            return attempt;
        }

        var last = status.LastRestorePoint;
        bool withinWindow = last is not null && status.FrequencyMinutes > 0 && last.CreationTime.AddMinutes(status.FrequencyMinutes) > DateTime.Now;
        if (withinWindow)
        {
            attempt.Status = RestorePointStatus.Throttled;
            attempt.Message = $"Windows skipped creation: a restore point already exists from {last!.CreationTime:yyyy-MM-dd HH:mm:ss} and Windows only allows one every " +
                              $"{status.FrequencyMinutes} minutes (next allowed after {last.CreationTime.AddMinutes(status.FrequencyMinutes):yyyy-MM-dd HH:mm}).";
        }
        else if (status.Enabled == false)
        {
            attempt.Status = RestorePointStatus.Disabled;
            attempt.Message = "No restore point was created. System Protection appears to be turned off for the Windows drive.";
        }
        else
        {
            attempt.Status = RestorePointStatus.Failed;
            attempt.Message = "Windows reported success but no new restore point could be found. It may have been skipped or creation is still pending.";
        }
        return attempt;
    }

    public static void OpenSystemRestore() => Process.Start(new ProcessStartInfo(ProcessRunner.System32("rstrui.exe")) { UseShellExecute = true });
    public static void OpenSystemProtectionSettings() => Process.Start(new ProcessStartInfo(ProcessRunner.System32("SystemPropertiesProtection.exe")) { UseShellExecute = true });

    public const string EnableInstructions =
        "To turn on System Protection:\n" +
        "1. Press Win+R, type SystemPropertiesProtection and press Enter (or use the button below).\n" +
        "2. Select the Windows drive (usually C:) and click Configure.\n" +
        "3. Choose 'Turn on system protection', set Max Usage to 5-10%, and click OK.\n" +
        "Windows allows one new restore point per 24 hours by default.";
}
