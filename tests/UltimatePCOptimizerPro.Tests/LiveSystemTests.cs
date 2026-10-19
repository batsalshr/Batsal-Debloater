using System.Text.Json;
using UltimatePCOptimizerPro.Core.Engine;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Tests;

/// <summary>Read-only checks against the machine running the tests. Nothing is modified.</summary>
public class LiveSystemTests
{
    [Fact]
    public async Task Hardware_scan_reads_real_values()
    {
        var log = new LoggingService();
        var snapshot = await new HardwareDetectionService(log).ScanAsync();
        Assert.True(snapshot.Cpu.LogicalProcessors > 0);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Cpu.Name));
        Assert.True(snapshot.Memory.TotalBytes > 0);
        Assert.True(snapshot.Os.Build > 17000);

        var tier = new HardwareTierService().Classify(snapshot);
        var monitor = new HardwareMonitoringService(log);
        monitor.UpdateHardware(snapshot);
        monitor.Sample();
        await Task.Delay(1000);
        var sample = monitor.Sample();
        Assert.InRange(sample.CpuPercent, 0, 100);

        var dump = Path.Combine(TestEnvironment.Root, "live-scan.json");
        File.WriteAllText(dump, JsonSerializer.Serialize(new { snapshot, tier, sample }, JsonDefaults.Indented));
    }

    [Fact]
    public void Tweak_status_evaluates_on_this_pc_without_errors()
    {
        var log = new LoggingService();
        var catalog = new TweakCatalogService(log);
        var status = new TweakStatusService(HandlerRegistry.CreateDefault(log), new BackupService(log));
        var results = status.EvaluateAll(catalog.Tweaks, null);
        Assert.DoesNotContain(results, r => r.Value.State == Core.Models.TweakState.Error);
        File.WriteAllText(Path.Combine(TestEnvironment.Root, "live-status.json"),
            JsonSerializer.Serialize(results.ToDictionary(r => r.Key, r => new { r.Value.State, r.Value.Message, r.Value.CurrentValues }), JsonDefaults.Indented));
    }
}
