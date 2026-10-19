namespace UltimatePCOptimizerPro.Core.Models;

public sealed class OsInfo
{
    public string ProductName { get; set; } = "Windows";
    public string? EditionId { get; set; }
    public string? DisplayVersion { get; set; }
    public int Build { get; set; }
    public int Ubr { get; set; }
    public string Architecture { get; set; } = "";
    public bool IsWindows11 => Build >= 22000;
    public bool IsElevated { get; set; }
    public TimeSpan Uptime { get; set; }
    public bool? GameModeEnabled { get; set; }
    public string HagsState { get; set; } = "Unknown";
    public bool? GameDvrEnabled { get; set; }
    public bool? BackgroundRecordingEnabled { get; set; }
    public string VbsStatus { get; set; } = "Unknown";
    public bool? MemoryIntegrityRunning { get; set; }
    public List<string> AntivirusProducts { get; set; } = new();
    public int StartupEntryCount { get; set; }
    public int RunningServiceCount { get; set; }
    public int ProcessCount { get; set; }

    public string BuildDisplay => Ubr > 0 ? $"{Build}.{Ubr}" : Build.ToString();
}

public sealed class SoftwareEntry
{
    public string Name { get; set; } = "";
    public string? Publisher { get; set; }
    public string? Version { get; set; }
    public string Category { get; set; } = "";
    public string Note { get; set; } = "";
}
