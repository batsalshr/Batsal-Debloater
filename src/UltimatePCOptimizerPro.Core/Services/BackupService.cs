using System.IO.Compression;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Persists operation records (with captured original values) and optional .reg exports.</summary>
public sealed class BackupService
{
    private readonly LoggingService _log;
    private readonly object _lock = new();
    public BackupService(LoggingService log) => _log = log;

    public string CreateFolder(OperationRecord op)
    {
        var folder = Path.Combine(AppPaths.Operations, $"{op.StartedAt:yyyyMMdd-HHmmss}_{op.Kind}_{op.Id.ToString("N")[..8]}");
        Directory.CreateDirectory(folder);
        op.FolderPath = folder;
        return folder;
    }

    public void Save(OperationRecord op)
    {
        lock (_lock)
        {
            var folder = op.FolderPath ?? CreateFolder(op);
            JsonDefaults.WriteFile(Path.Combine(folder, "operation.json"), op);
        }
    }

    public List<OperationRecord> LoadAll()
    {
        var list = new List<OperationRecord>();
        foreach (var dir in Directory.EnumerateDirectories(AppPaths.Operations))
        {
            var file = Path.Combine(dir, "operation.json");
            try
            {
                var op = JsonDefaults.ReadFile<OperationRecord>(file);
                if (op is null) continue;
                op.FolderPath = dir;
                list.Add(op);
            }
            catch (Exception ex) { _log.Warn("Backup", $"Unreadable operation record {file}", ex); }
        }
        return list.OrderByDescending(o => o.StartedAt).ToList();
    }

    public OperationRecord? Load(Guid id) => LoadAll().FirstOrDefault(o => o.Id == id);

    /// <summary>Exports registry keys that will be touched as human readable .reg files (supplementary backup).</summary>
    public void ExportRegistryKeys(OperationRecord op)
    {
        var folder = op.FolderPath ?? CreateFolder(op);
        var keys = op.Changes.Select(c => c.Spec switch
        {
            RegistryValueSpec r => $@"{r.Hive}\{r.Path}",
            RegistryTokenSpec t => $@"{t.Hive}\{t.Path}",
            RegistryFlagsSpec f => $@"{f.Hive}\{f.Path}",
            AppCompatLayerSpec => @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
            _ => null
        }).Where(k => k is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int i = 0;
        foreach (var key in keys)
        {
            var file = Path.Combine(folder, $"registry-{++i:00}.reg");
            var r = ProcessRunner.Run(ProcessRunner.System32("reg.exe"), $"export \"{key}\" \"{file}\" /y", 15000);
            if (r.Success) op.AddLog($"Exported {key} to {Path.GetFileName(file)}");
            else op.AddLog($"Key {key} did not exist before the change (nothing to export).");
        }
    }

    public string ExportZip(OperationRecord op, string destinationZip)
    {
        if (op.FolderPath is null || !Directory.Exists(op.FolderPath)) throw new DirectoryNotFoundException("The backup folder for this operation no longer exists.");
        if (File.Exists(destinationZip)) File.Delete(destinationZip);
        ZipFile.CreateFromDirectory(op.FolderPath, destinationZip, CompressionLevel.Optimal, includeBaseDirectory: true);
        return destinationZip;
    }

    public string ExportAll(string destinationZip)
    {
        if (File.Exists(destinationZip)) File.Delete(destinationZip);
        ZipFile.CreateFromDirectory(AppPaths.Operations, destinationZip, CompressionLevel.Optimal, includeBaseDirectory: true);
        return destinationZip;
    }

    /// <summary>Most recent active (not yet restored) change for a tweak, used by tweak cards' Restore buttons.</summary>
    public (OperationRecord Op, List<ChangeRecord> Changes)? FindActiveChangesForTweak(string tweakId)
    {
        foreach (var op in LoadAll().Where(o => o.Kind == OperationKind.Apply))
        {
            var changes = op.Changes.Where(c => c.TweakId == tweakId && c.IsActive).ToList();
            if (changes.Count > 0) return (op, changes);
        }
        return null;
    }

    public HashSet<string> TweaksWithActiveBackups()
        => LoadAll().Where(o => o.Kind == OperationKind.Apply).SelectMany(o => o.Changes).Where(c => c.IsActive && c.TweakId is not null)
            .Select(c => c.TweakId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
