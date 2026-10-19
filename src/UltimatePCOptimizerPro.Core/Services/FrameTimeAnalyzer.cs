using System.Globalization;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Frame-time statistics and CSV parsing for PresentMon / CapFrameX / OCAT style captures.</summary>
public static class FrameTimeAnalyzer
{
    public static FrameStatistics Compute(IReadOnlyList<double> frameTimesMs)
    {
        var valid = frameTimesMs.Where(f => f > 0 && f < 10_000 && !double.IsNaN(f)).ToList();
        var st = new FrameStatistics { FrameCount = valid.Count };
        if (valid.Count == 0) return st;
        var sorted = valid.OrderBy(x => x).ToList();
        double total = valid.Sum();
        st.DurationSeconds = total / 1000.0;
        st.AverageFps = valid.Count / st.DurationSeconds;
        st.AverageFrameTimeMs = total / valid.Count;
        st.MedianFrameTimeMs = NetworkDiagnosticsService.Percentile(sorted, 50);
        st.P99FrameTimeMs = NetworkDiagnosticsService.Percentile(sorted, 99);
        st.MaxFrameTimeMs = sorted[^1];
        st.OnePercentLowFps = 1000.0 / st.P99FrameTimeMs;
        st.PointOnePercentLowFps = 1000.0 / NetworkDiagnosticsService.Percentile(sorted, 99.9);
        int worstCount = Math.Max(1, (int)Math.Ceiling(sorted.Count * 0.01));
        st.OnePercentLowAvgMethodFps = 1000.0 / sorted.Skip(sorted.Count - worstCount).Average();
        st.StutterCount = valid.Count(f => f > 2 * st.MedianFrameTimeMs);
        var mean = st.AverageFrameTimeMs;
        st.FrameTimeStdDevMs = valid.Count > 1 ? Math.Sqrt(valid.Sum(f => (f - mean) * (f - mean)) / (valid.Count - 1)) : 0;
        return st;
    }

    private static readonly string[] FrameTimeColumns = { "MsBetweenPresents", "FrameTime", "msBetweenPresents", "Frame Time", "MsBetweenDisplayChange" };
    private static readonly string[] ProcessColumns = { "Application", "ProcessName", "Process Name" };

    /// <summary>Parses a frame-time CSV. Returns frame times in ms and the most frequent process name if present.</summary>
    public static (List<double> FrameTimes, string? Process) ParseCsv(string path, string? processFilter = null)
    {
        using var reader = new StreamReader(path);
        string? header;
        // CapFrameX/OCAT files may have comment lines before the header.
        do { header = reader.ReadLine(); } while (header is not null && (header.StartsWith("//") || header.Trim().Length == 0));
        if (header is null) throw new InvalidDataException("The file is empty.");
        var cols = SplitCsv(header);
        int ftIdx = -1;
        foreach (var name in FrameTimeColumns)
        {
            ftIdx = cols.FindIndex(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (ftIdx >= 0) break;
        }
        if (ftIdx < 0) throw new InvalidDataException("No frame-time column found (expected MsBetweenPresents or FrameTime).");
        int procIdx = -1;
        foreach (var name in ProcessColumns)
        {
            procIdx = cols.FindIndex(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (procIdx >= 0) break;
        }

        var frames = new List<double>();
        var procCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var parts = SplitCsv(line);
            if (parts.Count <= ftIdx) continue;
            if (procIdx >= 0 && procIdx < parts.Count)
            {
                var proc = parts[procIdx];
                if (processFilter is not null && !proc.Equals(processFilter, StringComparison.OrdinalIgnoreCase)) continue;
                procCounts[proc] = procCounts.GetValueOrDefault(proc) + 1;
            }
            if (double.TryParse(parts[ftIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)) frames.Add(ms);
        }
        var top = procCounts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
        return (frames, top);
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') { quoted = !quoted; continue; }
            if (ch == ',' && !quoted) { result.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        result.Add(sb.ToString().Trim());
        return result;
    }
}
