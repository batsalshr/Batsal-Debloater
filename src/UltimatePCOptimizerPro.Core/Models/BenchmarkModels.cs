using System.Text.Json.Serialization;

namespace UltimatePCOptimizerPro.Core.Models;

public sealed class FrameStatistics
{
    public int FrameCount { get; set; }
    public double DurationSeconds { get; set; }
    public double AverageFps { get; set; }
    /// <summary>1000 / 99th percentile frame time (percentile method).</summary>
    public double OnePercentLowFps { get; set; }
    /// <summary>1000 / 99.9th percentile frame time.</summary>
    public double PointOnePercentLowFps { get; set; }
    /// <summary>1000 / mean of the slowest 1% of frame times (average method, as used by some tools).</summary>
    public double OnePercentLowAvgMethodFps { get; set; }
    public double AverageFrameTimeMs { get; set; }
    public double MedianFrameTimeMs { get; set; }
    public double P99FrameTimeMs { get; set; }
    public double MaxFrameTimeMs { get; set; }
    /// <summary>Frames taking more than 2x the median frame time.</summary>
    public int StutterCount { get; set; }
    public double FrameTimeStdDevMs { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BenchmarkSource { PresentMon, ImportedCsv, TelemetryOnly }

public sealed class TelemetryPoint
{
    public double T { get; set; }
    public double Cpu { get; set; }
    public double? Gpu { get; set; }
    public double Ram { get; set; }
    public double? PingMs { get; set; }
}

public sealed class BenchmarkRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Game { get; set; }
    public string? ProcessName { get; set; }
    public string Tag { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public BenchmarkSource Source { get; set; }
    public string? SourceFile { get; set; }
    public FrameStatistics? Frames { get; set; }
    public List<float> FrameTimesMs { get; set; } = new();
    public List<TelemetryPoint> Telemetry { get; set; } = new();
    public PingStatistics? Ping { get; set; }
    public string? Notes { get; set; }
    public string? SystemSummary { get; set; }
}

public sealed class BenchmarkComparison
{
    public BenchmarkRun Before { get; set; } = null!;
    public BenchmarkRun After { get; set; } = null!;
    public List<ComparisonRow> Rows { get; set; } = new();
}

public sealed record ComparisonRow(string Metric, string Before, string After, string Delta, bool? Improved);
