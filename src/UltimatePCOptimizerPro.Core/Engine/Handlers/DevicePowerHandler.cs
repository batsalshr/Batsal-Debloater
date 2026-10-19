using System.Management;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

/// <summary>
/// The Device Manager checkbox "Allow the computer to turn off this device to save power", exposed by Windows through
/// root\wmi:MSPower_DeviceEnable.
/// </summary>
public sealed class DevicePowerHandler : ChangeHandler<DevicePowerSpec>
{
    protected override bool RequiresAdmin(DevicePowerSpec s) => true;

    private static ManagementObject? Find(string instanceId)
    {
        using var searcher = new ManagementObjectSearcher(Wmi.RootWmi, "SELECT * FROM MSPower_DeviceEnable");
        foreach (ManagementObject o in searcher.Get())
        {
            var name = o["InstanceName"] as string;
            if (name is not null && name.StartsWith(instanceId, StringComparison.OrdinalIgnoreCase)) return o;
            o.Dispose();
        }
        return null;
    }

    protected override Preflight Preflight(DevicePowerSpec s)
    {
        if (string.IsNullOrEmpty(s.DeviceInstanceId) || s.DeviceInstanceId.StartsWith('{'))
            return Models.Preflight.Unsupported("No matching device was found.");
        if (!Elevation.IsElevated)
            return new Preflight { Current = "Unknown (reading requires administrator rights)", RequiresAdmin = true };
        try
        {
            using var o = Find(s.DeviceInstanceId);
            if (o is null) return Models.Preflight.Unsupported("This device does not expose a power-saving option.");
            bool enabled = (bool)o["Enable"];
            return new Preflight { Current = enabled ? "Windows may turn device off" : "Windows may not turn device off", AlreadyApplied = enabled == s.AllowPowerOff, RequiresAdmin = true };
        }
        catch (ManagementException ex) { return Models.Preflight.Unsupported(ex.Message); }
    }

    protected override BackupState Capture(DevicePowerSpec s)
    {
        using var o = Find(s.DeviceInstanceId) ?? throw new InvalidOperationException("Device not found.");
        bool enabled = (bool)o["Enable"];
        var b = new BackupState { Display = enabled ? "Allowed" : "Not allowed" };
        b.Set("enable", enabled.ToString());
        return b;
    }

    private static void Set(string id, bool value)
    {
        using var o = Find(id) ?? throw new InvalidOperationException("Device not found.");
        o["Enable"] = value;
        o.Put();
    }

    private static bool? Read(string id)
    {
        using var o = Find(id);
        return o is null ? null : (bool)o["Enable"];
    }

    protected override void Apply(DevicePowerSpec s, BackupState backup) => Set(s.DeviceInstanceId, s.AllowPowerOff);

    protected override VerifyResult Verify(DevicePowerSpec s, BackupState backup)
        => Read(s.DeviceInstanceId) == s.AllowPowerOff ? VerifyResult.Match(s.Desired) : VerifyResult.Fail("unchanged");

    protected override void Restore(DevicePowerSpec s, BackupState b) => Set(s.DeviceInstanceId, b.GetBool("enable"));

    protected override VerifyResult VerifyRestored(DevicePowerSpec s, BackupState b)
        => Read(s.DeviceInstanceId) == b.GetBool("enable") ? VerifyResult.Match(b.Display) : VerifyResult.Fail("unchanged");
}
