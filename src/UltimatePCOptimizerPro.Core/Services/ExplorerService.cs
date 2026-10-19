using System.Diagnostics;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Restarts the Windows shell so Explorer/taskbar settings take effect without signing out.</summary>
public static class ExplorerService
{
    public static async Task RestartAsync()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcessesByName("explorer").Where(p => p.SessionId == sessionId))
        {
            try { p.Kill(); p.WaitForExit(5000); }
            catch { /* access denied or already exited */ }
            finally { p.Dispose(); }
        }
        await Task.Delay(2500);
        // Winlogon normally restarts the shell automatically (AutoRestartShell=1); start it if it did not.
        if (!Process.GetProcessesByName("explorer").Any(p => p.SessionId == sessionId))
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true });
    }
}
