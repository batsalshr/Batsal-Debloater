using System.Diagnostics;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>
/// Reads startup entries and their enabled state. Enabling/disabling is done by the StartupEntry handler using the
/// same "StartupApproved" registry mechanism as Task Manager, so the original Run entry is never deleted.
/// </summary>
public sealed class StartupManagerService
{
    private readonly LoggingService _log;
    public StartupManagerService(LoggingService log) => _log = log;

    public const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public static (RegistryHive Hive, RegistryView View, string Path)? RunKey(StartupLocation loc) => loc switch
    {
        StartupLocation.CurrentUserRun => (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Run"),
        StartupLocation.LocalMachineRun => (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        StartupLocation.LocalMachineRun32 => (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        _ => null
    };

    public static (RegistryHive Hive, string SubKey) ApprovedKey(StartupLocation loc) => loc switch
    {
        StartupLocation.CurrentUserRun => (RegistryHive.CurrentUser, ApprovedRoot + @"\Run"),
        StartupLocation.LocalMachineRun => (RegistryHive.LocalMachine, ApprovedRoot + @"\Run"),
        StartupLocation.LocalMachineRun32 => (RegistryHive.LocalMachine, ApprovedRoot + @"\Run32"),
        StartupLocation.UserStartupFolder => (RegistryHive.CurrentUser, ApprovedRoot + @"\StartupFolder"),
        StartupLocation.CommonStartupFolder => (RegistryHive.LocalMachine, ApprovedRoot + @"\StartupFolder"),
        _ => throw new ArgumentOutOfRangeException(nameof(loc))
    };

    public static string? FolderPath(StartupLocation loc) => loc switch
    {
        StartupLocation.UserStartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        StartupLocation.CommonStartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        _ => null
    };

    public static string LocationDisplay(StartupLocation loc) => loc switch
    {
        StartupLocation.CurrentUserRun => @"Registry: HKCU\...\Run (this user)",
        StartupLocation.LocalMachineRun => @"Registry: HKLM\...\Run (all users)",
        StartupLocation.LocalMachineRun32 => @"Registry: HKLM\WOW6432Node\...\Run (32-bit, all users)",
        StartupLocation.UserStartupFolder => "Startup folder (this user)",
        StartupLocation.CommonStartupFolder => "Startup folder (all users)",
        _ => loc.ToString()
    };

    /// <summary>StartupApproved data: first byte even (02/06) = enabled, odd (03/07) = disabled. Missing = enabled.</summary>
    public static bool IsApproved(StartupLocation loc, string name)
    {
        var (hive, sub) = ApprovedKey(loc);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var k = root.OpenSubKey(sub);
        return k?.GetValue(name) is not byte[] { Length: > 0 } data || (data[0] & 1) == 0;
    }

    public List<StartupEntry> GetEntries()
    {
        var list = new List<StartupEntry>();
        foreach (var loc in new[] { StartupLocation.CurrentUserRun, StartupLocation.LocalMachineRun, StartupLocation.LocalMachineRun32 })
        {
            var rk = RunKey(loc)!.Value;
            try
            {
                using var root = RegistryKey.OpenBaseKey(rk.Hive, rk.View);
                using var k = root.OpenSubKey(rk.Path);
                if (k is null) continue;
                foreach (var name in k.GetValueNames())
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    var cmd = k.GetValue(name)?.ToString() ?? "";
                    list.Add(Build(loc, name, cmd, ExtractExecutable(cmd)));
                }
            }
            catch (Exception ex) { _log.Warn("Startup", $"Cannot read {loc}", ex); }
        }
        foreach (var loc in new[] { StartupLocation.UserStartupFolder, StartupLocation.CommonStartupFolder })
        {
            var folder = FolderPath(loc);
            if (folder is null || !Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(file) ?? file : file;
                list.Add(Build(loc, name, file, target));
            }
        }
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static StartupEntry Build(StartupLocation loc, string name, string command, string? exe)
    {
        var e = new StartupEntry
        {
            Name = name,
            Command = command,
            Location = loc,
            LocationDisplay = LocationDisplay(loc),
            ExecutablePath = exe,
            Enabled = IsApproved(loc, name)
        };
        if (exe is not null && File.Exists(exe))
        {
            e.ExecutableExists = true;
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exe);
                e.Publisher = string.IsNullOrWhiteSpace(vi.CompanyName) ? null : vi.CompanyName.Trim();
                e.Description = string.IsNullOrWhiteSpace(vi.FileDescription) ? null : vi.FileDescription.Trim();
            }
            catch { }
        }
        return e;
    }

    public static string? ExtractExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var cmd = Environment.ExpandEnvironmentVariables(command.Trim());
        if (cmd.StartsWith('"'))
        {
            var end = cmd.IndexOf('"', 1);
            return end > 1 ? cmd[1..end] : null;
        }
        var idx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx > 0) return cmd[..(idx + 4)];
        var space = cmd.IndexOf(' ');
        return space > 0 ? cmd[..space] : cmd;
    }

    private static string? ResolveShortcut(string lnk)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(lnk);
                string target = shortcut.TargetPath;
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
        catch { return null; }
    }
}
