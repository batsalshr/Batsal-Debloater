using System.Diagnostics;
using System.Security.Principal;

namespace UltimatePCOptimizerPro.Core.Services;

public static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Starts a new elevated instance (UAC prompt). Returns false if the user declined.</summary>
    public static bool RelaunchElevated(string? arguments = null)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe, arguments ?? "") { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
