using System.Globalization;
using System.Net.NetworkInformation;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>
/// Periodically samples real system counters on a background thread. Sensors that Windows does not expose
/// are reported as null — never estimated.
/// </summary>
public sealed class HardwareMonitoringService : IDisposable
{
    private readonly LoggingService _log;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private NativeMethods.FILETIME _prevIdle, _prevKernel, _prevUser;
    private long _prevRx, _prevTx;
    private DateTime _prevNetTime;
    private int _tick;
    private double? _lastNvTemp;
    private List<GpuInfo> _gpus = new();
    private int? _baseClockMHz;

    public int IntervalMs { get; set; } = 1500;
    public MonitorSample? Latest { get; private set; }
    public event Action<MonitorSample>? SampleReady;
    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>Last 240 samples (6 minutes at the default interval).</summary>
    public IReadOnlyList<MonitorSample> History { get { lock (_gate) return _history.ToList(); } }
    private readonly Queue<MonitorSample> _history = new();

    public HardwareMonitoringService(LoggingService log) => _log = log;

    public void UpdateHardware(SystemSnapshot snapshot)
    {
        _gpus = snapshot.Gpus.Where(g => !g.IsSoftware).ToList();
        _baseClockMHz = snapshot.Cpu.BaseClockMHz;
    }

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        NativeMethods.GetSystemTimes(out _prevIdle, out _prevKernel, out _prevUser);
        _loop = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var sample = Sample();
                    Latest = sample;
                    lock (_gate)
                    {
                        _history.Enqueue(sample);
                        while (_history.Count > 240) _history.Dequeue();
                    }
                    SampleReady?.Invoke(sample);
                }
                catch (Exception ex)
                {
                    _log.Warn("Monitor", "Sampling failed", ex);
                }
                try { await Task.Delay(Math.Max(500, IntervalMs), ct); }
                catch (TaskCanceledException) { break; }
            }
        }, ct);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();

    /// <summary>Collects one sample synchronously (also used by the benchmark recorder).</summary>
    public MonitorSample Sample()
    {
        _tick++;
        var s = new MonitorSample { Timestamp = DateTimeOffset.Now };

        // CPU utilisation from system times (same basis as Task Manager's "Utility"-independent total).
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong idleD = idle.Value - _prevIdle.Value;
            ulong kernelD = kernel.Value - _prevKernel.Value;
            ulong userD = user.Value - _prevUser.Value;
            ulong total = kernelD + userD; // kernel time includes idle
            s.CpuPercent = total == 0 ? 0 : Math.Clamp(100.0 * (total - idleD) / total, 0, 100);
            _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
        }

        var mem = NativeMethods.GetMemoryStatus();
        s.RamAvailableBytes = mem.ullAvailPhys;
        s.RamUsedPercent = mem.ullTotalPhys == 0 ? 0 : 100.0 * (mem.ullTotalPhys - mem.ullAvailPhys) / mem.ullTotalPhys;
        if (NativeMethods.GetPerformanceInfo(out var perf, (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.PERFORMANCE_INFORMATION>()))
        {
            s.CommitBytes = (ulong)perf.CommitTotal * (ulong)perf.PageSize;
            s.CommitLimitBytes = (ulong)perf.CommitLimit * (ulong)perf.PageSize;
        }

        foreach (var o in Wmi.TryQuery(Wmi.Cimv2,
                     "SELECT PercentProcessorPerformance, PercentPerformanceLimit, ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'", out _, 5))
        {
            s.CpuPerformancePercent = o.Long("PercentProcessorPerformance");
            s.CpuPerformanceLimitPercent = o.Long("PercentPerformanceLimit");
            var freq = o.Long("ProcessorFrequency");
            if (s.CpuPerformancePercent is { } pp && (freq ?? _baseClockMHz) is { } baseMhz && baseMhz > 0)
                s.CpuEffectiveMHz = baseMhz * pp / 100.0;
        }

        foreach (var o in Wmi.TryQuery(Wmi.Cimv2, "SELECT PercentIdleTime FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'", out _, 5))
        {
            if (o.Long("PercentIdleTime") is { } idlePct) s.DiskActivePercent = Math.Clamp(100 - idlePct, 0, 100);
        }

        foreach (var o in Wmi.TryQuery(Wmi.Cimv2, "SELECT PagesInputPersec FROM Win32_PerfFormattedData_PerfOS_Memory", out _, 5))
        {
            s.PagesInputPerSec = o.Long("PagesInputPersec");
        }

        SampleNetwork(s);
        SampleGpu(s);

        if (NativeMethods.GetSystemPowerStatus(out var ps))
        {
            s.OnAcPower = ps.ACLineStatus != 0;
            if (ps.BatteryFlag != 128 && ps.BatteryLifePercent <= 100) s.BatteryPercent = ps.BatteryLifePercent;
        }
        return s;
    }

    private void SampleNetwork(MonitorSample s)
    {
        long rx = 0, tx = 0;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try
            {
                var st = ni.GetIPStatistics();
                rx += st.BytesReceived;
                tx += st.BytesSent;
            }
            catch (NetworkInformationException) { }
        }
        var now = DateTime.UtcNow;
        if (_prevNetTime != default)
        {
            var dt = (now - _prevNetTime).TotalSeconds;
            if (dt > 0)
            {
                s.NetReceiveBps = Math.Max(0, (rx - _prevRx) / dt);
                s.NetSendBps = Math.Max(0, (tx - _prevTx) / dt);
            }
        }
        _prevRx = rx; _prevTx = tx; _prevNetTime = now;
    }

    private void SampleGpu(MonitorSample s)
    {
        var primary = _gpus.OrderByDescending(g => g.IsDedicated).ThenByDescending(g => g.DedicatedVideoMemoryBytes).FirstOrDefault();

        // Utilisation: per LUID, sum each engine type across processes, then take the busiest engine type (Task Manager method).
        var engines = Wmi.TryQuery(Wmi.Cimv2, "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine", out var engErr, 5);
        if (engErr is null && engines.Count > 0)
        {
            var perType = new Dictionary<string, double>();
            foreach (var e in engines)
            {
                var name = e.Str("Name") ?? "";
                if (primary is not null && !name.Contains(primary.LuidKey, StringComparison.OrdinalIgnoreCase)) continue;
                var typeIdx = name.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
                var type = typeIdx >= 0 ? name[(typeIdx + 8)..] : "unknown";
                perType[type] = perType.GetValueOrDefault(type) + (e.Long("UtilizationPercentage") ?? 0);
            }
            if (perType.Count > 0) s.GpuPercent = Math.Clamp(perType.Values.Max(), 0, 100);
        }

        foreach (var m in Wmi.TryQuery(Wmi.Cimv2, "SELECT Name, DedicatedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory", out _, 5))
        {
            var name = m.Str("Name") ?? "";
            if (primary is null || name.Contains(primary.LuidKey, StringComparison.OrdinalIgnoreCase))
                s.GpuDedicatedUsedBytes = (s.GpuDedicatedUsedBytes ?? 0) + (m.ULong("DedicatedUsage") ?? 0);
        }

        if (primary is null) return;
        var perfData = D3dkmt.QueryAdapterPerf(primary.LuidLow, primary.LuidHigh);
        if (perfData?.TemperatureC is { } t)
        {
            s.GpuTemperatureC = t;
            s.GpuTemperatureSource = "WDDM kernel graphics (same as Task Manager)";
        }
        var (cur, _) = D3dkmt.QueryNodeFrequency(primary.LuidLow, primary.LuidHigh);
        s.GpuCoreClockMHz = cur;

        if (s.GpuTemperatureC is null && primary.Vendor == GpuVendor.Nvidia)
        {
            // nvidia-smi ships with the NVIDIA driver; query it at a lower rate because it spawns a process.
            if (_tick % 4 == 1) _lastNvTemp = QueryNvidiaSmiTemperature();
            if (_lastNvTemp is { } nv)
            {
                s.GpuTemperatureC = nv;
                s.GpuTemperatureSource = "nvidia-smi (NVIDIA driver)";
            }
        }
    }

    private static double? QueryNvidiaSmiTemperature()
    {
        var exe = ProcessRunner.System32("nvidia-smi.exe");
        if (!File.Exists(exe)) return null;
        try
        {
            var r = ProcessRunner.Run(exe, "--query-gpu=temperature.gpu --format=csv,noheader,nounits", 4000);
            if (r.Success && double.TryParse(r.StdOut.Split('\n')[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) return t;
        }
        catch { }
        return null;
    }
}
