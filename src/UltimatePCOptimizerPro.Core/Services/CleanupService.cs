using System.Diagnostics;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>
/// Scans well-known cache/temp locations and deletes only the files the user reviewed and selected.
/// Personal folders, game saves and Windows servicing data are never included.
/// </summary>
public sealed class CleanupService
{
    private readonly LoggingService _log;
    private const int MaxFilesPerCategory = 200_000;
    public CleanupService(LoggingService log) => _log = log;

    public static List<CleanupCategory> DefineCategories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var localLow = Path.Combine(Path.GetDirectoryName(local)!, "LocalLow");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return new List<CleanupCategory>
        {
            new() { Id = "user-temp", Name = "User temporary files", SelectedByDefault = true,
                Description = "Files in your %TEMP% folder older than 24 hours.",
                Warning = "Files younger than 24 hours are skipped because running installers may still need them.",
                Roots = { Path.GetTempPath() } },
            new() { Id = "windows-temp", Name = "Windows temporary files", SelectedByDefault = true, RequiresAdmin = true,
                Description = @"Files in C:\Windows\Temp older than 24 hours.",
                Warning = "Requires administrator rights. Recent files are skipped.",
                Roots = { Path.Combine(windows, "Temp") } },
            new() { Id = "crash-dumps", Name = "Application crash dumps",
                Description = "Memory dumps written when applications crash.",
                Warning = "Delete only if you don't need to send crash reports to a developer.",
                Roots = { Path.Combine(local, "CrashDumps") } },
            new() { Id = "wer", Name = "Windows Error Reporting archive", RequiresAdmin = true,
                Description = "Archived and queued error reports.",
                Warning = "Removes diagnostic information about past crashes.",
                Roots = { Path.Combine(programData, @"Microsoft\Windows\WER\ReportArchive"), Path.Combine(programData, @"Microsoft\Windows\WER\ReportQueue") } },
            new() { Id = "d3d-shader", Name = "DirectX shader cache",
                Description = "Compiled DirectX shaders stored by Windows.",
                Warning = "Games will recompile shaders on next launch, which causes stutter and longer loading until the cache is rebuilt. Only clear it after driver issues or to free space.",
                Roots = { Path.Combine(local, "D3DSCache") } },
            new() { Id = "nvidia-shader", Name = "NVIDIA shader cache",
                Description = "Shader caches written by the NVIDIA driver.",
                Warning = "Same trade-off as the DirectX shader cache: temporary stutter while shaders are rebuilt.",
                Roots = { Path.Combine(local, @"NVIDIA\DXCache"), Path.Combine(local, @"NVIDIA\GLCache"), Path.Combine(localLow, @"NVIDIA\PerDriverVersion\DXCache") } },
            new() { Id = "amd-shader", Name = "AMD shader cache",
                Description = "Shader caches written by the AMD driver.",
                Warning = "Same trade-off as the DirectX shader cache: temporary stutter while shaders are rebuilt.",
                Roots = { Path.Combine(local, @"AMD\DxCache"), Path.Combine(local, @"AMD\DxcCache"), Path.Combine(local, @"AMD\GLCache"), Path.Combine(local, @"AMD\VkCache") } },
            new() { Id = "intel-shader", Name = "Intel shader cache",
                Description = "Shader caches written by the Intel graphics driver.",
                Warning = "Same trade-off as the DirectX shader cache: temporary stutter while shaders are rebuilt.",
                Roots = { Path.Combine(localLow, @"Intel\ShaderCache") } },
        };
    }

    public Task<List<CleanupCategory>> ScanAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var cats = DefineCategories();
        foreach (var c in cats)
        {
            ct.ThrowIfCancellationRequested();
            bool ageFilter = c.Id is "user-temp" or "windows-temp";
            var cutoff = DateTime.Now.AddHours(-24);
            foreach (var root in c.Roots.Where(Directory.Exists))
            {
                foreach (var file in SafeEnumerate(root, c))
                {
                    if (c.Files.Count >= MaxFilesPerCategory) break;
                    try
                    {
                        var fi = new FileInfo(file);
                        if (ageFilter && fi.LastWriteTime > cutoff) { c.SkippedRecent++; continue; }
                        c.Files.Add(new CleanupFile(fi.FullName, fi.Length, fi.LastWriteTime));
                    }
                    catch { c.InaccessibleCount++; }
                }
            }
        }
        return cats;
    }, ct);

    private static IEnumerable<string> SafeEnumerate(string root, CleanupCategory c)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] files, dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs = Directory.GetDirectories(dir);
            }
            catch { c.InaccessibleCount++; continue; }
            foreach (var f in files) yield return f;
            foreach (var d in dirs)
            {
                try
                {
                    // Never follow junctions/symlinks out of the cache folder.
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { continue; }
                stack.Push(d);
            }
        }
    }

    public Task<CleanupResult> DeleteAsync(IEnumerable<CleanupCategory> categories, CancellationToken ct = default) => Task.Run(() =>
    {
        var result = new CleanupResult();
        foreach (var c in categories)
        {
            foreach (var f in c.Files)
            {
                ct.ThrowIfCancellationRequested();
                // Defence in depth: only delete inside the category's roots.
                if (!c.Roots.Any(r => f.Path.StartsWith(Path.GetFullPath(r), StringComparison.OrdinalIgnoreCase))) continue;
                try
                {
                    File.SetAttributes(f.Path, FileAttributes.Normal);
                    File.Delete(f.Path);
                    result.Deleted++;
                    result.BytesFreed += f.SizeBytes;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    if (result.Errors.Count < 50) result.Errors.Add($"{f.Path}: {ex.Message}");
                }
            }
            foreach (var root in c.Roots.Where(Directory.Exists)) RemoveEmptyDirectories(root, isRoot: true);
            _log.Info("Cleanup", $"{c.Name}: deleted {result.Deleted} files so far, {result.Failed} failed");
        }
        return result;
    }, ct);

    private static void RemoveEmptyDirectories(string dir, bool isRoot)
    {
        try
        {
            foreach (var d in Directory.GetDirectories(dir))
            {
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                RemoveEmptyDirectories(d, false);
            }
            if (!isRoot && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch { /* in use */ }
    }

    public static (long Bytes, long Items) QueryRecycleBin()
    {
        var info = new NativeMethods.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
        return NativeMethods.SHQueryRecycleBin(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    public static bool EmptyRecycleBin()
        => NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND) is 0 or unchecked((int)0x8000FFFF);

    public static void OpenDiskCleanup() => Process.Start(new ProcessStartInfo(ProcessRunner.System32("cleanmgr.exe")) { UseShellExecute = true });
    public static void OpenStorageSettings() => Process.Start(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true });
}
