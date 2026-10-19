using UltimatePCOptimizerPro.Core.Engine.Handlers;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Tests;

public class MeasurementTests
{
    [Fact]
    public void Constant_frame_times_give_exact_fps()
    {
        var st = FrameTimeAnalyzer.Compute(Enumerable.Repeat(10.0, 1000).ToList());
        Assert.Equal(100, st.AverageFps, 6);
        Assert.Equal(100, st.OnePercentLowFps, 6);
        Assert.Equal(100, st.PointOnePercentLowFps, 6);
        Assert.Equal(0, st.StutterCount);
    }

    [Fact]
    public void Spikes_lower_the_one_percent_low()
    {
        // 2% of frames are 50 ms spikes, so the 99th percentile falls inside the spikes.
        var frames = Enumerable.Repeat(5.0, 980).Concat(Enumerable.Repeat(50.0, 20)).ToList();
        var st = FrameTimeAnalyzer.Compute(frames);
        Assert.Equal(1000 / ((980 * 5.0 + 20 * 50.0) / 1000), st.AverageFps, 6);
        Assert.Equal(20, st.OnePercentLowFps, 6);
        Assert.Equal(20, st.OnePercentLowAvgMethodFps, 6); // worst 10 frames are 50 ms
        Assert.Equal(20, st.StutterCount);
    }

    [Fact]
    public void Csv_parser_reads_presentmon_columns()
    {
        var path = Path.Combine(TestEnvironment.Root, "pm.csv");
        File.WriteAllLines(path, new[]
        {
            "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,MsBetweenPresents",
            "game.exe,1,0x0,DXGI,0,0,16.6",
            "game.exe,1,0x0,DXGI,0,0,16.8",
            "other.exe,2,0x0,DXGI,0,0,33.3"
        });
        var (frames, proc) = FrameTimeAnalyzer.ParseCsv(path, "game.exe");
        Assert.Equal(new[] { 16.6, 16.8 }, frames);
        Assert.Equal("game.exe", proc);
    }

    [Fact]
    public void Jitter_is_mean_absolute_successive_difference()
    {
        var samples = new[] { 10.0, 14, 10, 14 }.Select((v, i) => new PingSample(i, DateTimeOffset.Now, true, v, "OK")).ToList();
        samples.Add(new PingSample(4, DateTimeOffset.Now, false, null, "TimedOut"));
        var st = NetworkDiagnosticsService.Compute("x", samples);
        Assert.Equal(4, st.JitterMs!.Value, 6);
        Assert.Equal(20, st.LossPercent, 6);
        Assert.Equal(12, st.AverageMs!.Value, 6);
    }

    [Fact]
    public void Dns_query_is_well_formed()
    {
        var q = NetworkDiagnosticsService.BuildDnsQuery("www.example.com", 0x1234);
        Assert.Equal(0x12, q[0]);
        Assert.Equal(0x34, q[1]);
        Assert.Equal(0x01, q[2]); // RD
        Assert.Equal(3, q[12]);   // "www"
        Assert.Equal(new byte[] { 0, 1, 0, 1 }, q[^4..]);

        var response = (byte[])q.Clone();
        response[2] = 0x81; response[3] = 0x80; // QR + RD + RA, RCODE 0
        Assert.True(NetworkDiagnosticsService.IsValidDnsResponse(response, 0x1234));
        Assert.False(NetworkDiagnosticsService.IsValidDnsResponse(response, 0x9999));
        Assert.False(NetworkDiagnosticsService.IsValidDnsResponse(q, 0x1234)); // not a response
    }

    [Fact]
    public void Token_editing_preserves_other_tokens()
    {
        var s = "VRROptimizeEnable=0;AutoHDREnable=1;";
        var updated = RegistryTokenHandler.SetToken(s, "SwapEffectUpgradeEnable", "1");
        Assert.Equal("VRROptimizeEnable=0;AutoHDREnable=1;SwapEffectUpgradeEnable=1;", updated);
        Assert.Equal("VRROptimizeEnable=1;AutoHDREnable=1;SwapEffectUpgradeEnable=1;", RegistryTokenHandler.SetToken(updated, "VRROptimizeEnable", "1"));
        Assert.Equal("AutoHDREnable=1;", RegistryTokenHandler.SetToken("AutoHDREnable=1;SwapEffectUpgradeEnable=1;", "SwapEffectUpgradeEnable", null));
        Assert.Null(RegistryTokenHandler.GetToken("A=1;", "B"));
    }

    [Fact]
    public void Tier_scoring_is_explainable()
    {
        var cpu = new CpuInfo { Name = "AMD Ryzen 7 7800X3D 8-Core Processor", LogicalProcessors = 16, PhysicalCores = 8 };
        var score = HardwareTierService.ScoreCpu(cpu, out var basis);
        Assert.True(score > 70, basis);
        Assert.Contains("X3D", basis);

        var old = new CpuInfo { Name = "Intel(R) Core(TM) i3-4130 CPU @ 3.40GHz", LogicalProcessors = 4, PhysicalCores = 2 };
        Assert.True(HardwareTierService.ScoreCpu(old, out _) < 35);
    }

    [Fact]
    public void Low_ram_caps_tier_to_low_end()
    {
        var s = new SystemSnapshot
        {
            Cpu = new CpuInfo { Name = "Intel(R) Core(TM) i9-14900K", LogicalProcessors = 32, PhysicalCores = 24 },
            Memory = new MemoryInfo { TotalBytes = 6UL << 30 },
        };
        s.Gpus.Add(new GpuInfo { Name = "NVIDIA GeForce RTX 4090", Vendor = GpuVendor.Nvidia, DedicatedVideoMemoryBytes = 24UL << 30 });
        var r = new HardwareTierService().Classify(s);
        Assert.Equal(PcTier.LowEnd, r.Tier);
        Assert.NotEmpty(r.Caps);
        Assert.Equal(PcTier.HighEnd, new HardwareTierService().Classify(s, overrideTier: PcTier.HighEnd).Tier);
    }
}
