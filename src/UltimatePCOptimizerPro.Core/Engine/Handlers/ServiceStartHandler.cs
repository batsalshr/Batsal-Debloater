using System.ComponentModel;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

/// <summary>Changes a Win32 service's startup type through the Service Control Manager (ChangeServiceConfig).</summary>
public sealed class ServiceStartHandler : ChangeHandler<ServiceStartSpec>
{
    protected override bool RequiresAdmin(ServiceStartSpec s) => true;

    public static ServiceStartMode? ReadStartMode(string name, out bool exists, out int rawStart)
    {
        using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        exists = k is not null;
        rawStart = k?.GetValue("Start") is int st ? st : -1;
        if (k is null) return null;
        bool delayed = k.GetValue("DelayedAutostart") is int d && d == 1;
        return rawStart switch
        {
            2 => delayed ? ServiceStartMode.AutomaticDelayed : ServiceStartMode.Automatic,
            3 => ServiceStartMode.Manual,
            4 => ServiceStartMode.Disabled,
            _ => null
        };
    }

    protected override Preflight Preflight(ServiceStartSpec s)
    {
        if (SafetyPolicy.IsServiceProtected(s.ServiceName)) return Models.Preflight.Unsupported($"'{s.ServiceName}' is a protected service and will not be modified.");
        var mode = ReadStartMode(s.ServiceName, out var exists, out var raw);
        if (!exists) return Models.Preflight.Unsupported($"Service '{s.ServiceName}' is not installed on this PC.");
        if (mode is null) return Models.Preflight.Unsupported($"Service '{s.ServiceName}' has a boot/system start type ({raw}) and is not managed by this tool.");
        return new Preflight { Current = mode.ToString()!, AlreadyApplied = mode == s.StartMode, RequiresAdmin = true };
    }

    protected override BackupState Capture(ServiceStartSpec s)
    {
        var mode = ReadStartMode(s.ServiceName, out _, out _) ?? throw new InvalidOperationException("Service start type unreadable.");
        var b = new BackupState { Display = mode.ToString() };
        b.Set("startMode", mode.ToString());
        return b;
    }

    public static void SetStartMode(string serviceName, ServiceStartMode mode)
    {
        if (SafetyPolicy.IsServiceProtected(serviceName)) throw new UnauthorizedAccessException($"'{serviceName}' is protected.");
        var scm = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) throw new Win32Exception();
        try
        {
            var svc = NativeMethods.OpenService(scm, serviceName, NativeMethods.SERVICE_QUERY_CONFIG | NativeMethods.SERVICE_CHANGE_CONFIG);
            if (svc == IntPtr.Zero) throw new Win32Exception();
            try
            {
                uint start = mode switch
                {
                    ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed => NativeMethods.SERVICE_AUTO_START,
                    ServiceStartMode.Manual => NativeMethods.SERVICE_DEMAND_START,
                    _ => NativeMethods.SERVICE_DISABLED
                };
                if (!NativeMethods.ChangeServiceConfig(svc, NativeMethods.SERVICE_NO_CHANGE, start, NativeMethods.SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null))
                    throw new Win32Exception();
                if (start == NativeMethods.SERVICE_AUTO_START)
                {
                    var info = new NativeMethods.SERVICE_DELAYED_AUTO_START_INFO { fDelayedAutostart = mode == ServiceStartMode.AutomaticDelayed ? 1 : 0 };
                    if (!NativeMethods.ChangeServiceConfig2(svc, NativeMethods.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref info))
                        throw new Win32Exception();
                }
            }
            finally { NativeMethods.CloseServiceHandle(svc); }
        }
        finally { NativeMethods.CloseServiceHandle(scm); }
    }

    protected override void Apply(ServiceStartSpec s, BackupState backup) => SetStartMode(s.ServiceName, s.StartMode);

    protected override VerifyResult Verify(ServiceStartSpec s, BackupState backup)
    {
        var mode = ReadStartMode(s.ServiceName, out _, out _);
        // The new start type is persisted immediately; a running service keeps running until it stops or the PC restarts.
        return mode == s.StartMode ? VerifyResult.Match(mode.ToString()!) : VerifyResult.Fail(mode?.ToString() ?? "unknown");
    }

    protected override void Restore(ServiceStartSpec s, BackupState b) => SetStartMode(s.ServiceName, Enum.Parse<ServiceStartMode>(b.Get("startMode")!));

    protected override VerifyResult VerifyRestored(ServiceStartSpec s, BackupState b)
    {
        var mode = ReadStartMode(s.ServiceName, out _, out _);
        return mode?.ToString() == b.Get("startMode") ? VerifyResult.Match(mode.ToString()!) : VerifyResult.Fail(mode?.ToString() ?? "unknown");
    }
}
