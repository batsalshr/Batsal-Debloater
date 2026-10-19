using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Measures per-process CPU, memory and I/O over a short interval. Never terminates anything on its own.</summary>
public sealed class ProcessMonitorService
{
    private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost", "dwm",
        "explorer", "fontdrvhost", "sihost", "ctfmon", "MsMpEng", "NisSrv", "SecurityHealthService", "audiodg", "Memory Compression",
        "spoolsv", "SearchIndexer", "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "conhost", "dllhost"
    };

    private readonly ConcurrentDictionary<string, string?> _publisherCache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Raw(int Pid, string Name, ulong Cpu100ns, ulong IoBytes, string? Path, bool Denied, long WorkingSet, long Private, int SessionId);

    public async Task<List<ProcessUsage>> SampleAsync(int intervalMs = 1000, CancellationToken ct = default)
    {
        var first = await Task.Run(Snapshot, ct);
        var sw = Stopwatch.StartNew();
        await Task.Delay(intervalMs, ct);
        var second = await Task.Run(Snapshot, ct);
        double seconds = sw.Elapsed.TotalSeconds;
        int cores = Environment.ProcessorCount;
        var before = first.ToDictionary(r => r.Pid);
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var list = new List<ProcessUsage>();
        foreach (var r in second)
        {
            if (r.Pid == 0) continue;
            before.TryGetValue(r.Pid, out var b);
            double cpu = b is null || r.Denied ? 0 : (r.Cpu100ns - b.Cpu100ns) / 1e7 / seconds / cores * 100.0;
            double io = b is null || r.Denied ? 0 : (r.IoBytes - b.IoBytes) / seconds;
            list.Add(new ProcessUsage
            {
                Pid = r.Pid,
                Name = r.Name,
                Path = r.Path,
                Publisher = r.Path is null ? null : _publisherCache.GetOrAdd(r.Path, ReadPublisher),
                CpuPercent = Math.Max(0, Math.Round(cpu, 1)),
                WorkingSetBytes = (ulong)Math.Max(0, r.WorkingSet),
                PrivateBytes = (ulong)Math.Max(0, r.Private),
                DiskBytesPerSec = Math.Max(0, io),
                AccessDenied = r.Denied,
                IsSystemProcess = r.SessionId == 0 || CriticalNames.Contains(r.Name) || (r.Path?.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase) ?? r.Denied)
            });
        }
        return list.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes).ToList();
    }

    private static List<Raw> Snapshot()
    {
        var list = new List<Raw>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                ulong cpu = 0, io = 0;
                string? path = null;
                bool denied = true;
                var h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (h != IntPtr.Zero)
                {
                    try
                    {
                        if (NativeMethods.GetProcessTimes(h, out _, out _, out var k, out var u)) { cpu = k.Value + u.Value; denied = false; }
                        if (NativeMethods.GetProcessIoCounters(h, out var c)) io = c.ReadTransferCount + c.WriteTransferCount + c.OtherTransferCount;
                        var sb = new StringBuilder(1024);
                        int size = sb.Capacity;
                        if (NativeMethods.QueryFullProcessImageName(h, 0, sb, ref size)) path = sb.ToString();
                    }
                    finally { NativeMethods.CloseHandle(h); }
                }
                long ws = 0, priv = 0;
                try { ws = p.WorkingSet64; priv = p.PrivateMemorySize64; } catch { }
                list.Add(new Raw(p.Id, p.ProcessName, cpu, io, path, denied, ws, priv, p.SessionId));
            }
            catch { /* process exited */ }
            finally { p.Dispose(); }
        }
        return list;
    }

    private static string? ReadPublisher(string path)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(vi.CompanyName) ? null : vi.CompanyName.Trim();
        }
        catch { return null; }
    }

    public static bool CanTerminate(ProcessUsage p) => !p.IsSystemProcess && !p.AccessDenied && p.Pid != Environment.ProcessId;

    /// <summary>Ends a user process after the UI has obtained explicit confirmation.</summary>
    public static void Terminate(ProcessUsage p)
    {
        if (!CanTerminate(p)) throw new InvalidOperationException("This process is protected or belongs to Windows.");
        using var proc = Process.GetProcessById(p.Pid);
        if (!string.Equals(proc.ProcessName, p.Name, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The process has exited and its id was reused.");
        proc.CloseMainWindow();
        if (!proc.WaitForExit(3000)) proc.Kill();
    }
}
