namespace UltimatePCOptimizerPro.Core.Models;

public enum StorageMediaKind { Unknown, Hdd, Ssd, Scm }

public sealed class StorageDevice
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "Unknown disk";
    public StorageMediaKind MediaKind { get; set; }
    public string BusType { get; set; } = "Unknown";
    public ulong SizeBytes { get; set; }
    public string Health { get; set; } = "Unknown";
    public bool IsSystemDisk { get; set; }
    public List<char> DriveLetters { get; set; } = new();

    public bool IsNvme => BusType.Equals("NVMe", StringComparison.OrdinalIgnoreCase);

    public string KindDisplay => MediaKind switch
    {
        StorageMediaKind.Ssd when IsNvme => "NVMe SSD",
        StorageMediaKind.Ssd => "SSD",
        StorageMediaKind.Hdd => "HDD",
        StorageMediaKind.Scm => "Storage-class memory",
        _ => IsNvme ? "NVMe (media type not reported)" : "Unknown media type"
    };
}

public sealed class VolumeInfo
{
    public string Name { get; set; } = "";
    public string? Label { get; set; }
    public string? FileSystem { get; set; }
    public string DriveType { get; set; } = "";
    public ulong TotalBytes { get; set; }
    public ulong FreeBytes { get; set; }
    public bool IsSystemVolume { get; set; }

    public double FreePercent => TotalBytes == 0 ? 0 : 100.0 * FreeBytes / TotalBytes;
}

public sealed class StorageInfo
{
    public List<StorageDevice> Disks { get; set; } = new();
    public List<VolumeInfo> Volumes { get; set; } = new();
    /// <summary>From HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\DisableDeleteNotification (0 = TRIM enabled).</summary>
    public bool? TrimEnabled { get; set; }

    public StorageDevice? SystemDisk => Disks.FirstOrDefault(d => d.IsSystemDisk);
}
