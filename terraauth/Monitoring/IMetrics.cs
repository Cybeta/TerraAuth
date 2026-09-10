// Phase 6 - 监控指标接口（Prometheus 风格）
// 对应架构文档 §7.2 量化指标

namespace TerraAuth.Monitoring;

/// <summary>
/// 指标收集器：供运营监控 + Phase 7 压测数据采集。
/// 真实实现可用 prometheus-net（Histogram/Counter/Gauge）。
/// </summary>
public interface IMetrics
{
    // ---- 计数器（只增不减）----
    void IncrementRejectedPackets(string reason);   // 被拒绝的包总数（按原因维度）
    void IncrementBlockedCheat(string cheatType);   // 拦截的作弊类型（内存篡改/瞬移/刷物品...）
    void IncrementFalsePositives();                 // 误判数（运营复核后手动标记）

    // ---- 直方图 ----
    void ObservePacketProcessingTime(double milliseconds); // 单包处理耗时
    void ObservePlayerBandwidth(Guid playerId, double bytesPerSecond);

    // ---- 仪表盘 ----
    void SetConnectedPlayers(int count);
    void SetAuthorityOverhead(double milliseconds); // 权威层额外开销
}
