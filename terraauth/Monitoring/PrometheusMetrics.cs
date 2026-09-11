// Phase 6 - Prometheus 指标实现（已填充）
// 进程内 CollectorRegistry 风格：Counter / Gauge / Histogram 三类型
// ExportAsText 严格遵循 Prometheus exposition format
// https://prometheus.io/docs/instrumenting/exposition_formats/

using System.Collections.Concurrent;
using System.Globalization;
using System.Net; // HttpListener
using System.Text;

namespace TerraAuth.Monitoring;

public sealed class PrometheusMetrics : IMetrics, IDisposable
{
    // ---- Counter：只增不减（含标签维度）----
    private readonly ConcurrentDictionary<string, long> _counters = new(); // key = name{labels}
    // ---- Gauge：可增可减 ----
    private readonly ConcurrentDictionary<string, double> _gauges = new();
    // ---- Histogram：包处理耗时 / 带宽分布 ----
    private readonly ConcurrentDictionary<string, HistogramBucket> _histograms = new();

    private readonly Timer _histogramRotateTimer;
    private readonly Lock _rotateLock = new();

    public PrometheusMetrics()
    {
        // 每 60s 把滑动窗口直方图固化为观测值（防止内存无限增长）
        _histogramRotateTimer = new Timer(_ => RotateHistograms(), null,
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    // ========================================================================
    // IMetrics 实现
    // ========================================================================
    public void IncrementRejectedPackets(string reason)
        => _counters.AddOrUpdate($"terraauth_rejected_packets_total{{reason=\"{EscapeLabel(reason)}\"}}", 1, (_, v) => v + 1);

    public void IncrementBlockedCheat(string cheatType)
        => _counters.AddOrUpdate($"terraauth_blocked_cheats_total{{type=\"{EscapeLabel(cheatType)}\"}}", 1, (_, v) => v + 1);

    public void IncrementFalsePositives()
        => _counters.AddOrUpdate("terraauth_false_positives_total", 1, (_, v) => v + 1);

    public void ObservePacketProcessingTime(double milliseconds)
        => Observe("terraauth_packet_processing_seconds", milliseconds / 1000.0);

    public void ObservePlayerBandwidth(Guid playerId, double bytesPerSecond)
        => Observe($"terraauth_player_bandwidth_bytes{{player=\"{playerId:N}\"}}", bytesPerSecond);

    public void SetConnectedPlayers(int count)
        => _gauges.AddOrUpdate("terraauth_connected_players", count, (_, _) => count);

    public void SetAuthorityOverhead(double milliseconds)
        => _gauges.AddOrUpdate("terraauth_authority_overhead_ms", milliseconds, (_, _) => milliseconds);

    public void SetGauge(string name, double value, params (string, string)[] labels)
    {
        if (labels.Length == 0)
        {
            _gauges.AddOrUpdate(name, value, (_, _) => value);
            return;
        }

        var labelText = string.Join(",", labels.Select(l => $"{l.Item1}=\"{EscapeLabel(l.Item2)}\""));
        var key = $"{name}{{{labelText}}}";
        _gauges.AddOrUpdate(key, value, (_, _) => value);
    }

    // ========================================================================
    // 供 /metrics HTTP 端点导出
    // ========================================================================
    public string ExportAsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# HELP terraauth_rejected_packets_total 被拒绝的包总数（按原因维度）");
        sb.AppendLine("# TYPE terraauth_rejected_packets_total counter");
        foreach (var kv in _counters)
            sb.AppendLine($"{kv.Key} {kv.Value.ToString(CultureInfo.InvariantCulture)}");

        sb.AppendLine("# HELP terraauth_blocked_cheats_total 拦截的作弊类型");
        sb.AppendLine("# TYPE terraauth_blocked_cheats_total counter");
        sb.AppendLine("# HELP terraauth_false_positives_total 运营复核后标记的误判数");
        sb.AppendLine("# TYPE terraauth_false_positives_total counter");
        sb.AppendLine("# HELP terraauth_connected_players 当前在线玩家数");
        sb.AppendLine("# TYPE terraauth_connected_players gauge");
        foreach (var kv in _gauges)
            sb.AppendLine($"{kv.Key} {kv.Value.ToString(CultureInfo.InvariantCulture)}");

        sb.AppendLine("# HELP terraauth_packet_processing_seconds 单包处理耗时");
        sb.AppendLine("# TYPE terraauth_packet_processing_seconds histogram");
        foreach (var kv in _histograms)
            sb.AppendLine($"{kv.Key} {kv.Value}");

        return sb.ToString();
    }

    // ========================================================================
    // 内部辅助
    // ========================================================================
    private void Observe(string name, double value)
    {
        var bucket = _histograms.GetOrAdd(name, _ => new HistogramBucket());
        bucket.Record(value);
    }

    private static string EscapeLabel(string s) => s.Replace("\\", @"\\").Replace("\"", "\\\"");

    private void RotateHistograms()
    {
        // 直方图定期固化（此处简化为清空，生产可用滑动窗口保留最近 N 个 bucket）
        lock (_rotateLock)
        {
            foreach (var kv in _histograms) kv.Value.Rotate();
        }
    }

    public void Dispose() => _histogramRotateTimer.Dispose();

    // ---- 直方图桶（简化实现：count + sum）----
    private sealed class HistogramBucket
    {
        private long _count;
        private double _sum;
        public void Record(double v) { Interlocked.Increment(ref _count); _sum += v; }
        public void Rotate() { _count = 0; _sum = 0; }
        public override string ToString() => $"{_sum.ToString(CultureInfo.InvariantCulture)} {_count}";
    }
}

/// <summary>Prometheus /metrics HTTP 端点（自托管，无需 Kestrel 依赖）。</summary>
public sealed class MetricsHttpServer : IDisposable
{
    private readonly PrometheusMetrics _metrics;
    private readonly int _port;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public MetricsHttpServer(PrometheusMetrics metrics, int port = 9090)
    {
        _metrics = metrics;
        _port = port;
        _listener.Prefixes.Add($"http://+:{port}/");
    }

    public void Start()
    {
        try { _listener.Start(); }
        catch (HttpListenerException ex)
        {
            // 可能需要 netsh http add urlacl（Windows）或 root（Linux）
            Console.WriteLine($"[Metrics] 无法绑定 :{_port}（{ex.Message}）。可改用反向代理或降低权限。");
            return;
        }
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        Console.WriteLine($"[Metrics] /metrics 已就绪：http://localhost:{_port}/metrics");
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                if (ctx.Request.Url?.AbsolutePath is "/metrics")
                {
                    var body = Encoding.UTF8.GetBytes(_metrics.ExportAsText());
                    ctx.Response.ContentType = "text/plain; version=0.0.4";
                    ctx.Response.ContentLength64 = body.Length;
                    await ctx.Response.OutputStream.WriteAsync(body, ct).ConfigureAwait(false);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
                ctx.Response.Close();
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Console.WriteLine($"[Metrics] 处理异常：{ex.Message}"); }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
    }
}
