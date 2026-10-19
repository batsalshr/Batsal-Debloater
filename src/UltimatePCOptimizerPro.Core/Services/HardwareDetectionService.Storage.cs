using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

public sealed partial class HardwareDetectionService
{
    private static StorageInfo DetectStorage(List<ScanWarning> warnings)
    {
        var info = new StorageInfo();
        var systemLetter = char.ToUpperInvariant((Environment.GetEnvironmentVariable("SystemDrive") ?? "C:")[0]);

        var disks = Wmi.TryQuery(Wmi.Storage, "SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk", out var err);
        foreach (var o in disks)
        {
            info.Disks.Add(new StorageDevice
            {
                DeviceId = o.Str("DeviceId") ?? "",
                Name = o.Str("FriendlyName") ?? "Disk",
                MediaKind = (o.Int("MediaType") ?? 0) switch { 3 => StorageMediaKind.Hdd, 4 => StorageMediaKind.Ssd, 5 => StorageMediaKind.Scm, _ => StorageMediaKind.Unknown },
                BusType = BusTypeName(o.Int("BusType") ?? 0),
                SizeBytes = o.ULong("Size") ?? 0,
                Health = (o.Int("HealthStatus") ?? -1) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" }
            });
        }
        if (err is not null) warnings.Add(new ScanWarning("Storage", "Storage namespace unavailable: " + err));

        foreach (var o in Wmi.TryQuery(Wmi.Storage, "SELECT DriveLetter, DiskNumber FROM MSFT_Partition", out _))
        {
            var letterObj = o.Raw("DriveLetter");
            char letter = letterObj is char c ? c : letterObj is ushort u ? (char)u : '\0';
            if (letter == '\0') continue;
            var diskNumber = o.Long("DiskNumber")?.ToString();
            var disk = info.Disks.FirstOrDefault(d => d.DeviceId == diskNumber);
            if (disk is null) continue;
            disk.DriveLetters.Add(char.ToUpperInvariant(letter));
            if (char.ToUpperInvariant(letter) == systemLetter) disk.IsSystemDisk = true;
        }

        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                info.Volumes.Add(new VolumeInfo
                {
                    Name = d.Name,
                    Label = d.VolumeLabel,
                    FileSystem = d.DriveFormat,
                    DriveType = d.DriveType.ToString(),
                    TotalBytes = (ulong)d.TotalSize,
                    FreeBytes = (ulong)d.AvailableFreeSpace,
                    IsSystemVolume = char.ToUpperInvariant(d.Name[0]) == systemLetter
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
            if (k?.GetValue("DisableDeleteNotification") is int ddn) info.TrimEnabled = ddn == 0;
        }
        catch (Exception ex) { warnings.Add(new ScanWarning("Storage", "TRIM state unreadable: " + ex.Message)); }

        return info;
    }

    private static string BusTypeName(int bus) => bus switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "IEEE 1394", 5 => "SSA", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID",
        9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC", 15 => "File-backed virtual", 16 => "Storage Spaces",
        17 => "NVMe", 18 => "SCM", 19 => "UFS", _ => "Unknown"
    };
}
