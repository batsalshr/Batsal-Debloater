using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Real network measurements: ICMP latency/jitter/loss, direct DNS resolver timing, traceroute and bufferbloat.</summary>
public sealed class NetworkDiagnosticsService
{
    private readonly LoggingService _log;
    public NetworkDiagnosticsService(LoggingService log) => _log = log;

    public static readonly (string Server, string Label)[] PublicResolvers =
    {
        ("1.1.1.1", "Cloudflare"), ("8.8.8.8", "Google"), ("9.9.9.9", "Quad9"), ("208.67.222.222", "OpenDNS")
    };

    // ------------------------------------------------------------------------------------------- ping

    public async Task<PingStatistics> PingSeriesAsync(string host, int count, int intervalMs, IProgress<PingSample>? progress = null, CancellationToken ct = default, int timeoutMs = 2000)
    {
        var samples = new List<PingSample>();
        using var ping = new Ping();
        var buffer = new byte[32];
        for (int i = 0; i < count && !ct.IsCancellationRequested; i++)
        {
            var sw = Stopwatch.StartNew();
            PingSample sample;
            try
            {
                var reply = await ping.SendPingAsync(host, timeoutMs, buffer);
                sample = reply.Status == IPStatus.Success
                    ? new PingSample(i, DateTimeOffset.Now, true, reply.RoundtripTime, "OK")
                    : new PingSample(i, DateTimeOffset.Now, false, null, reply.Status.ToString());
            }
            catch (PingException ex)
            {
                sample = new PingSample(i, DateTimeOffset.Now, false, null, ex.InnerException?.Message ?? ex.Message);
            }
            samples.Add(sample);
            progress?.Report(sample);
            var wait = intervalMs - (int)sw.ElapsedMilliseconds;
            if (wait > 0 && i < count - 1)
            {
                try { await Task.Delay(wait, ct); } catch (TaskCanceledException) { break; }
            }
        }
        return Compute(host, samples);
    }

    public static PingStatistics Compute(string target, IReadOnlyList<PingSample> samples)
    {
        var ok = samples.Where(s => s.Success && s.RoundTripMs is not null).Select(s => s.RoundTripMs!.Value).ToList();
        var st = new PingStatistics { Target = target, Sent = samples.Count, Received = ok.Count };
        if (ok.Count == 0) return st;
        var sorted = ok.OrderBy(x => x).ToList();
        st.MinMs = sorted[0];
        st.MaxMs = sorted[^1];
        st.AverageMs = ok.Average();
        st.MedianMs = Percentile(sorted, 50);
        st.P95Ms = Percentile(sorted, 95);
        if (ok.Count > 1)
        {
            double sum = 0;
            for (int i = 1; i < ok.Count; i++) sum += Math.Abs(ok[i] - ok[i - 1]);
            st.JitterMs = sum / (ok.Count - 1);
            var avg = st.AverageMs.Value;
            st.StdDevMs = Math.Sqrt(ok.Sum(x => (x - avg) * (x - avg)) / (ok.Count - 1));
        }
        return st;
    }

    public static double Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        if (sortedAscending.Count == 0) return 0;
        double rank = p / 100.0 * (sortedAscending.Count - 1);
        int lo = (int)Math.Floor(rank), hi = (int)Math.Ceiling(rank);
        return lo == hi ? sortedAscending[lo] : sortedAscending[lo] + (sortedAscending[hi] - sortedAscending[lo]) * (rank - lo);
    }

    // ------------------------------------------------------------------------------------------- DNS

    /// <summary>Builds a standard recursive DNS query for an A record.</summary>
    public static byte[] BuildDnsQuery(string domain, ushort id)
    {
        var labels = domain.TrimEnd('.').Split('.');
        var packet = new List<byte>(64);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header[0..], id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0100); // RD
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);      // QDCOUNT
        packet.AddRange(header.ToArray());
        foreach (var l in labels)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(l);
            if (bytes.Length is 0 or > 63) throw new ArgumentException("Invalid domain label.", nameof(domain));
            packet.Add((byte)bytes.Length);
            packet.AddRange(bytes);
        }
        packet.Add(0);
        packet.AddRange(new byte[] { 0, 1, 0, 1 }); // QTYPE A, QCLASS IN
        return packet.ToArray();
    }

    /// <summary>True if the response matches the id and has RCODE 0 (NOERROR) or 3 (NXDOMAIN, still a valid answer).</summary>
    public static bool IsValidDnsResponse(ReadOnlySpan<byte> response, ushort id)
    {
        if (response.Length < 12) return false;
        if (BinaryPrimitives.ReadUInt16BigEndian(response) != id) return false;
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(response[2..]);
        bool isResponse = (flags & 0x8000) != 0;
        int rcode = flags & 0x000F;
        return isResponse && rcode is 0 or 3;
    }

    public async Task<double?> TimeDnsQueryAsync(string server, string domain, int timeoutMs = 2000, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(server, out var ip)) return null;
        using var udp = new UdpClient(ip.AddressFamily);
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var query = BuildDnsQuery(domain, id);
        var sw = Stopwatch.StartNew();
        await udp.SendAsync(query, query.Length, new IPEndPoint(ip, 53));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            while (true)
            {
                var r = await udp.ReceiveAsync(cts.Token);
                if (IsValidDnsResponse(r.Buffer, id)) return sw.Elapsed.TotalMilliseconds;
            }
        }
        catch (OperationCanceledException) { return null; }
    }

    public async Task<List<DnsServerResult>> BenchmarkDnsAsync(IEnumerable<string> currentServers, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        string[] domains = { "www.google.com", "www.microsoft.com", "store.steampowered.com", "www.epicgames.com", "www.riotgames.com", "www.ea.com", "discord.com", "www.cloudflare.com" };
        var servers = currentServers.Where(s => IPAddress.TryParse(s, out var a) && a.AddressFamily == AddressFamily.InterNetwork)
            .Select(s => (s, "Current (adapter)", true)).ToList();
        foreach (var (s, l) in PublicResolvers)
            if (!servers.Any(x => x.s == s)) servers.Add((s, l, false));

        var results = new List<DnsServerResult>();
        foreach (var (server, label, current) in servers)
        {
            progress?.Report($"Testing {label} ({server})…");
            var times = new List<double>();
            int failures = 0;
            // Two rounds: the first warms the resolver's cache so the second measures resolver latency.
            for (int round = 0; round < 2; round++)
            {
                foreach (var d in domains)
                {
                    ct.ThrowIfCancellationRequested();
                    var t = await TimeDnsQueryAsync(server, d, 2000, ct);
                    if (round == 1) { if (t is null) failures++; else times.Add(t.Value); }
                }
            }
            results.Add(new DnsServerResult
            {
                Server = server, Label = label, IsCurrent = current, Queries = domains.Length, Failures = failures,
                AverageMs = times.Count > 0 ? times.Average() : null, MinMs = times.Count > 0 ? times.Min() : null, MaxMs = times.Count > 0 ? times.Max() : null
            });
        }
        return results.OrderBy(r => r.AverageMs ?? double.MaxValue).ToList();
    }

    // ------------------------------------------------------------------------------------------- traceroute

    public async Task<List<TraceHop>> TracerouteAsync(string host, int maxHops = 30, IProgress<TraceHop>? progress = null, CancellationToken ct = default)
    {
        var hops = new List<TraceHop>();
        IPAddress target;
        try { target = IPAddress.TryParse(host, out var ip) ? ip : (await Dns.GetHostAddressesAsync(host, ct)).First(a => a.AddressFamily == AddressFamily.InterNetwork); }
        catch (Exception ex) { hops.Add(new TraceHop(0, null, null, "Cannot resolve: " + ex.Message)); return hops; }

        using var ping = new Ping();
        for (int ttl = 1; ttl <= maxHops && !ct.IsCancellationRequested; ttl++)
        {
            var sw = Stopwatch.StartNew();
            PingReply reply;
            try { reply = await ping.SendPingAsync(target, 3000, new byte[32], new PingOptions(ttl, true)); }
            catch (PingException ex) { var h = new TraceHop(ttl, null, null, ex.Message); hops.Add(h); progress?.Report(h); continue; }
            sw.Stop();
            var hop = reply.Status switch
            {
                IPStatus.TtlExpired or IPStatus.TimeExceeded => new TraceHop(ttl, reply.Address?.ToString(), sw.Elapsed.TotalMilliseconds, "Transit"),
                IPStatus.Success => new TraceHop(ttl, reply.Address?.ToString(), reply.RoundtripTime, "Destination"),
                _ => new TraceHop(ttl, reply.Address?.ToString(), null, reply.Status.ToString())
            };
            hops.Add(hop);
            progress?.Report(hop);
            if (reply.Status == IPStatus.Success) break;
        }
        return hops;
    }

    // ------------------------------------------------------------------------------------------- bufferbloat

    /// <summary>
    /// Measures idle latency, then latency while saturating the download link with a large HTTP transfer.
    /// The increase indicates queueing delay in the router/modem ("bufferbloat"). Upload load is not tested.
    /// </summary>
    public async Task<BufferbloatResult> BufferbloatTestAsync(string pingHost, string downloadUrl, int phaseSeconds = 10, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var result = new BufferbloatResult();
        int interval = 250, count = phaseSeconds * 1000 / interval;
        progress?.Report("Measuring idle latency…");
        result.Idle = await PingSeriesAsync(pingHost, count, interval, null, ct);

        progress?.Report("Measuring latency while downloading…");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        long bytes = 0;
        var sw = Stopwatch.StartNew();
        var download = Task.Run(async () =>
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(phaseSeconds + 30) };
            var tasks = Enumerable.Range(0, 4).Select(async _ =>
            {
                try
                {
                    using var resp = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    resp.EnsureSuccessStatusCode();
                    await using var s = await resp.Content.ReadAsStreamAsync(cts.Token);
                    var buf = new byte[81920];
                    int n;
                    while ((n = await s.ReadAsync(buf, cts.Token)) > 0) Interlocked.Add(ref bytes, n);
                }
                catch (OperationCanceledException) { }
            });
            await Task.WhenAll(tasks);
        }, cts.Token);

        await Task.Delay(1500, ct); // let TCP ramp up
        result.Loaded = await PingSeriesAsync(pingHost, count, interval, null, ct);
        var elapsed = sw.Elapsed.TotalSeconds;
        cts.Cancel();
        try { await download; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { result.Error = ex is HttpRequestException ? ex.Message : null; }

        result.DownloadMbps = elapsed > 0 ? Interlocked.Read(ref bytes) * 8 / elapsed / 1_000_000 : 0;
        if (result.DownloadMbps < 1 && result.Error is null) result.Error = "The download did not generate meaningful load; the result is not reliable.";
        var inc = result.LatencyIncreaseMs ?? double.NaN;
        (result.Grade, result.Interpretation) = double.IsNaN(inc) ? ("?", "Latency could not be measured.")
            : inc < 5 ? ("A+", "Excellent: almost no queueing delay under load.")
            : inc < 30 ? ("A", "Good: minor latency increase under load.")
            : inc < 60 ? ("B", "Fair: noticeable lag spikes when others download. Consider SQM/QoS on the router.")
            : inc < 200 ? ("C", "Poor: heavy bufferbloat. Enable SQM (fq_codel/CAKE) or 'Smart Queue' on the router, set to ~90% of line speed.")
            : inc < 400 ? ("D", "Bad: severe bufferbloat during downloads.")
            : ("F", "Very bad: latency under load is unusable for online games.");
        return result;
    }
}
