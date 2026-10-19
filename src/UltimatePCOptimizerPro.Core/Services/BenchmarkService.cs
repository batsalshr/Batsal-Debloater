using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>
/// Benchmarking with real instrumentation only. Frame times come from Intel PresentMon (ETW-based, open source);
/// this app never estimates FPS itself. System telemetry is recorded alongside every capture.
/// </summary>
public sealed class BenchmarkService
{
    private readonly LoggingService _log;
    private readonly HardwareMonitoringService _monitor;
    private readonly SettingsService _settings;

    public BenchmarkService(LoggingService log, HardwareMonitoringService monitor, SettingsService settings)
    {
        _log = log;
        _monitor = monitor;
        _settings = settings;
    }

    public const string PresentMonInfo =
        "Frame-time capture uses Intel PresentMon (free, open source: github.com/GameTechDev/PresentMon). " +
        "Download the console build (PresentMon-2.x-x64.exe), then select it below. PresentMon reads Windows ETW present events, " +
        "requires administrator rights, and works with anti-cheat titles because it does not inject into the game.";

    public string? FindPresentMon()
    {
        if (_settings.Current.PresentMonPath is { } p && File.Exists(p)) return p;
        var candidates = new List<string>();
        foreach (var dir in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Intel", "PresentMon"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                     AppContext.BaseDirectory
                 })
        {
            if (!Directory.Exists(dir)) continue;
            try { candidates.AddRange(Directory.EnumerateFiles(dir, "PresentMon*.exe", SearchOption.AllDirectories).Take(20)); } catch { }
        }
        return candidates.Where(c => !c.Contains("Service", StringComparison.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
    }

    private static bool IsV2(string exe)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exe);
            if (vi.FileMajorPart >= 2) return true;
        }
        catch { }
        return Path.GetFileName(exe).Contains("-2.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Captures frame times for a running process with PresentMon while recording CPU/GPU/RAM/ping telemetry.</summary>
    public async Task<BenchmarkRun> CapturePresentMonAsync(string presentMonExe, string processName, int seconds, string name, string tag, string? pingHost,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!Elevation.IsElevated) throw new InvalidOperationException("PresentMon needs administrator rights to read ETW events. Restart the app as administrator.");
        if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)).Length == 0)
            throw new InvalidOperationException($"{processName} is not running. Start the game, reach a repeatable scene, then start the capture.");

        var csv = Path.Combine(AppPaths.Benchmarks, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var dash = IsV2(presentMonExe) ? "--" : "-";
        var args = $"{dash}process_name {processName} {dash}output_file \"{csv}\" {dash}timed {seconds} {dash}terminate_after_timed {dash}terminate_on_proc_exit";
        if (dash == "--") args += " --stop_existing_session";
        var run = new BenchmarkRun { Name = name, Tag = tag, ProcessName = processName, Game = processName, Source = BenchmarkSource.PresentMon, SourceFile = csv };

        progress?.Report($"Capturing {seconds} s of frame times from {processName}…");
        var telemetryTask = RecordTelemetryAsync(run, seconds, pingHost, ct);
        var pm = await Task.Run(() => ProcessRunner.Run(presentMonExe, args, (seconds + 30) * 1000), ct);
        await telemetryTask;
        if (!File.Exists(csv))
            throw new InvalidOperationException($"PresentMon did not produce a capture (exit {pm.ExitCode}). Output: {pm.Combined}");

        var (frames, _) = FrameTimeAnalyzer.ParseCsv(csv, processName);
        if (frames.Count == 0) throw new InvalidOperationException("The capture contains no frames for this process. Make sure the game is rendering (not minimised).");
        run.FrameTimesMs = frames.Select(f => (float)f).ToList();
        run.Frames = FrameTimeAnalyzer.Compute(frames);
        Save(run);
        return run;
    }

    /// <summary>Imports an existing PresentMon/CapFrameX/OCAT CSV.</summary>
    public BenchmarkRun ImportCsv(string path, string name, string tag)
    {
        var (frames, proc) = FrameTimeAnalyzer.ParseCsv(path);
        if (frames.Count == 0) throw new InvalidDataException("No frames found in the file.");
        var run = new BenchmarkRun
        {
            Name = name, Tag = tag, Source = BenchmarkSource.ImportedCsv, SourceFile = path, ProcessName = proc, Game = proc,
            FrameTimesMs = frames.Select(f => (float)f).ToList(), Frames = FrameTimeAnalyzer.Compute(frames)
        };
        Save(run);
        return run;
    }

    /// <summary>System-only benchmark (no FPS): records CPU/GPU/RAM and ping while the user plays.</summary>
    public async Task<BenchmarkRun> RecordTelemetryOnlyAsync(int seconds, string name, string tag, string? pingHost, CancellationToken ct = default)
    {
        var run = new BenchmarkRun { Name = name, Tag = tag, Source = BenchmarkSource.TelemetryOnly };
        await RecordTelemetryAsync(run, seconds, pingHost, ct);
        Save(run);
        return run;
    }

    private async Task RecordTelemetryAsync(BenchmarkRun run, int seconds, string? pingHost, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var pings = new List<PingSample>();
        using var ping = new System.Net.NetworkInformation.Ping();
        int seq = 0;
        while (sw.Elapsed.TotalSeconds < seconds && !ct.IsCancellationRequested)
        {
            var s = await Task.Run(_monitor.Sample, ct);
            double? rtt = null;
            if (pingHost is not null)
            {
                try
                {
                    var reply = await ping.SendPingAsync(pingHost, 1000);
                    var ok = reply.Status == System.Net.NetworkInformation.IPStatus.Success;
                    if (ok) rtt = reply.RoundtripTime;
                    pings.Add(new PingSample(seq++, DateTimeOffset.Now, ok, rtt, reply.Status.ToString()));
                }
                catch { pings.Add(new PingSample(seq++, DateTimeOffset.Now, false, null, "error")); }
            }
            run.Telemetry.Add(new TelemetryPoint { T = Math.Round(sw.Elapsed.TotalSeconds, 2), Cpu = s.CpuPercent, Gpu = s.GpuPercent, Ram = s.RamUsedPercent, PingMs = rtt });
            try { await Task.Delay(1000, ct); } catch (TaskCanceledException) { break; }
        }
        if (pingHost is not null) run.Ping = NetworkDiagnosticsService.Compute(pingHost, pings);
    }

    // ----------------------------------------------------------------------------------------- persistence

    public void Save(BenchmarkRun run)
    {
        JsonDefaults.WriteFile(Path.Combine(AppPaths.Benchmarks, $"{run.Timestamp:yyyyMMdd-HHmmss}_{run.Id.ToString("N")[..8]}.json"), run);
        var limit = Math.Max(5, _settings.Current.BenchmarkHistoryLimit);
        var files = Directory.GetFiles(AppPaths.Benchmarks, "*.json").OrderByDescending(File.GetCreationTimeUtc).Skip(limit);
        foreach (var f in files) { try { File.Delete(f); } catch { } }
    }

    public List<BenchmarkRun> LoadAll()
        => Directory.EnumerateFiles(AppPaths.Benchmarks, "*.json")
            .Select(f => { try { return JsonDefaults.ReadFile<BenchmarkRun>(f); } catch { return null; } })
            .Where(r => r is not null).Cast<BenchmarkRun>().OrderByDescending(r => r.Timestamp).ToList();

    public void Delete(BenchmarkRun run)
    {
        foreach (var f in Directory.EnumerateFiles(AppPaths.Benchmarks, $"*_{run.Id.ToString("N")[..8]}.json")) File.Delete(f);
    }

    public static BenchmarkComparison Compare(BenchmarkRun before, BenchmarkRun after)
    {
        var c = new BenchmarkComparison { Before = before, After = after };
        void Row(string metric, double? b, double? a, string fmt, bool higherIsBetter)
        {
            if (b is null && a is null) return;
            string delta = b is not null && a is not null && b != 0 ? $"{(a - b) / b * 100:+0.0;-0.0;0.0}%" : "–";
            bool? improved = b is null || a is null || Math.Abs(a.Value - b.Value) < 1e-9 ? null : higherIsBetter ? a > b : a < b;
            c.Rows.Add(new ComparisonRow(metric, b?.ToString(fmt, CultureInfo.CurrentCulture) ?? "–", a?.ToString(fmt, CultureInfo.CurrentCulture) ?? "–", delta, improved));
        }
        Row("Average FPS", before.Frames?.AverageFps, after.Frames?.AverageFps, "0.0", true);
        Row("1% low FPS", before.Frames?.OnePercentLowFps, after.Frames?.OnePercentLowFps, "0.0", true);
        Row("0.1% low FPS", before.Frames?.PointOnePercentLowFps, after.Frames?.PointOnePercentLowFps, "0.0", true);
        Row("Avg frame time (ms)", before.Frames?.AverageFrameTimeMs, after.Frames?.AverageFrameTimeMs, "0.00", false);
        Row("99th pct frame time (ms)", before.Frames?.P99FrameTimeMs, after.Frames?.P99FrameTimeMs, "0.00", false);
        Row("Frame-time std. dev. (ms)", before.Frames?.FrameTimeStdDevMs, after.Frames?.FrameTimeStdDevMs, "0.00", false);
        Row("Stutters (>2× median)", before.Frames?.StutterCount, after.Frames?.StutterCount, "0", false);
        Row("Avg CPU %", Avg(before, t => t.Cpu), Avg(after, t => t.Cpu), "0.0", false);
        Row("Avg GPU %", Avg(before, t => t.Gpu), Avg(after, t => t.Gpu), "0.0", true);
        Row("Avg RAM %", Avg(before, t => t.Ram), Avg(after, t => t.Ram), "0.0", false);
        Row("Ping avg (ms)", before.Ping?.AverageMs, after.Ping?.AverageMs, "0.0", false);
        Row("Jitter (ms)", before.Ping?.JitterMs, after.Ping?.JitterMs, "0.0", false);
        Row("Packet loss %", before.Ping?.LossPercent, after.Ping?.LossPercent, "0.0", false);
        return c;
    }

    private static double? Avg(BenchmarkRun r, Func<TelemetryPoint, double?> f)
    {
        var v = r.Telemetry.Select(f).Where(x => x is not null).Select(x => x!.Value).ToList();
        return v.Count == 0 ? null : v.Average();
    }

    public string ExportHtmlReport(IEnumerable<BenchmarkRun> runs, BenchmarkComparison? comparison, string path, string systemSummary)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>Benchmark report</title><style>")
          .Append("body{font-family:Segoe UI,sans-serif;background:#0b0f14;color:#e6edf5;margin:32px}table{border-collapse:collapse;margin:12px 0}")
          .Append("td,th{border:1px solid #233041;padding:6px 10px;text-align:left}th{background:#141b24}.good{color:#22c55e}.bad{color:#ef4444}h1{color:#00b4ff}</style></head><body>")
          .Append("<h1>ULTIMATE PC OPTIMIZER PRO — Benchmark report</h1>")
          .Append($"<p>Generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}</p><pre>{WebUtility.HtmlEncode(systemSummary)}</pre>");
        if (comparison is not null)
        {
            sb.Append($"<h2>Before / after: {WebUtility.HtmlEncode(comparison.Before.Name)} → {WebUtility.HtmlEncode(comparison.After.Name)}</h2><table><tr><th>Metric</th><th>Before</th><th>After</th><th>Change</th></tr>");
            foreach (var r in comparison.Rows)
                sb.Append($"<tr><td>{r.Metric}</td><td>{r.Before}</td><td>{r.After}</td><td class='{(r.Improved == true ? "good" : r.Improved == false ? "bad" : "")}'>{r.Delta}</td></tr>");
            sb.Append("</table><p>Differences within ~3% are usually run-to-run variance. Use the same scene, settings and duration for both runs.</p>");
        }
        sb.Append("<h2>Runs</h2><table><tr><th>Name</th><th>Tag</th><th>Date</th><th>Source</th><th>Process</th><th>Frames</th><th>Avg FPS</th><th>1% low</th><th>0.1% low</th><th>Ping</th><th>Jitter</th></tr>");
        foreach (var r in runs)
            sb.Append($"<tr><td>{WebUtility.HtmlEncode(r.Name)}</td><td>{WebUtility.HtmlEncode(r.Tag)}</td><td>{r.Timestamp:yyyy-MM-dd HH:mm}</td><td>{r.Source}</td><td>{WebUtility.HtmlEncode(r.ProcessName ?? "")}</td>" +
                      $"<td>{r.Frames?.FrameCount}</td><td>{r.Frames?.AverageFps:0.0}</td><td>{r.Frames?.OnePercentLowFps:0.0}</td><td>{r.Frames?.PointOnePercentLowFps:0.0}</td><td>{r.Ping?.AverageMs:0.0}</td><td>{r.Ping?.JitterMs:0.0}</td></tr>");
        sb.Append("</table><p>1% / 0.1% low = 1000 ÷ 99th / 99.9th percentile frame time (percentile method).</p></body></html>");
        File.WriteAllText(path, sb.ToString());
        return path;
    }
}
