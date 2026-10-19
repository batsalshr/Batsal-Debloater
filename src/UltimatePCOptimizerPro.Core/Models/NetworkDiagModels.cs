namespace UltimatePCOptimizerPro.Core.Models;

public sealed record PingSample(int Sequence, DateTimeOffset Timestamp, bool Success, double? RoundTripMs, string Status);

public sealed class PingStatistics
{
    public string Target { get; set; } = "";
    public int Sent { get; set; }
    public int Received { get; set; }
    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;
    public double? MinMs { get; set; }
    public double? MaxMs { get; set; }
    public double? AverageMs { get; set; }
    public double? MedianMs { get; set; }
    public double? P95Ms { get; set; }
    /// <summary>Mean absolute difference between consecutive successful samples.</summary>
    public double? JitterMs { get; set; }
    public double? StdDevMs { get; set; }
}

public sealed class DnsServerResult
{
    public string Server { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsCurrent { get; set; }
    public int Queries { get; set; }
    public int Failures { get; set; }
    public double? AverageMs { get; set; }
    public double? MinMs { get; set; }
    public double? MaxMs { get; set; }
}

public sealed record TraceHop(int Hop, string? Address, double? RoundTripMs, string Status);

public sealed class BufferbloatResult
{
    public PingStatistics Idle { get; set; } = new();
    public PingStatistics Loaded { get; set; } = new();
    public double DownloadMbps { get; set; }
    public double? LatencyIncreaseMs => Idle.MedianMs is { } i && Loaded.MedianMs is { } l ? l - i : null;
    public string Grade { get; set; } = "?";
    public string Interpretation { get; set; } = "";
    public string? Error { get; set; }
}
