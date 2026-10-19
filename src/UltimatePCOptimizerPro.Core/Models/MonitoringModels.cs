namespace UltimatePCOptimizerPro.Core.Models;

/// <summary>One sample from the live monitor. Null means the sensor/counter is not available.</summary>
public sealed class MonitorSample
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public double CpuPercent { get; set; }
    /// <summary>"% Processor Performance" — effective frequency relative to base (can exceed 100 with boost).</summary>
    public double? CpuPerformancePercent { get; set; }
    /// <summary>"% Performance Limit" — below 100 means power/thermal/platform limits are active.</summary>
    public double? CpuPerformanceLimitPercent { get; set; }
    public double? CpuEffectiveMHz { get; set; }
    public double RamUsedPercent { get; set; }
    public ulong RamAvailableBytes { get; set; }
    public ulong CommitBytes { get; set; }
    public ulong CommitLimitBytes { get; set; }
    public double? DiskActivePercent { get; set; }
    public double? PagesInputPerSec { get; set; }
    public double NetReceiveBps { get; set; }
    public double NetSendBps { get; set; }
    public double? GpuPercent { get; set; }
    public ulong? GpuDedicatedUsedBytes { get; set; }
    public double? GpuTemperatureC { get; set; }
    public string? GpuTemperatureSource { get; set; }
    public double? GpuCoreClockMHz { get; set; }
    public double? AcpiThermalZoneC { get; set; }
    public bool OnAcPower { get; set; } = true;
    public int? BatteryPercent { get; set; }
}

public sealed class ProcessUsage
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string? Publisher { get; set; }
    public double CpuPercent { get; set; }
    public ulong WorkingSetBytes { get; set; }
    public ulong PrivateBytes { get; set; }
    public double DiskBytesPerSec { get; set; }
    public bool AccessDenied { get; set; }
    public bool IsSystemProcess { get; set; }
    public string? StartupEntryName { get; set; }
}
