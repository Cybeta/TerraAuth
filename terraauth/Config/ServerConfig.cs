// Phase 6 - 服务端配置定义
// 反作弊阈值的唯一来源（架构 §4.5）

namespace TerraAuth.Config;

public record ServerConfig
{
    // ---- 网络 ----
    public int MaxConnections { get; init; } = 64;
    public int HandshakeTimeoutSeconds { get; init; } = 10;
    public int SnapshotRateHz { get; init; } = 20;
    public float ViewportRadius { get; init; } = 2000f;   // 快照视野半径（像素）；0 = 不裁剪

    // ---- 移动权威 (Phase 2 IMovementAuthority) ----
    public float MaxWalkSpeed { get; init; } = 3.6f;      // 单位/秒，对应 terraria 基础移速
    public float MaxFlightSpeed { get; init; } = 8.0f;
    public float MaxFallSpeed { get; init; } = 20.0f;
    public float TeleportTolerance { get; init; } = 4.0f;  // 单 tick 允许最大位移（防瞬移）
    public float ShadowPredictionMaxDeviation { get; init; } = 8.0f; // 影子预测偏差阈值（px）

    // ---- 战斗权威 (Phase 2 ICombatAuthority) ----
    public int MaxSingleDamage { get; init; } = 30000;     // 单次伤害上限
    public int MaxDpsWindowSeconds { get; init; } = 5;     // DPS 统计窗口
    public int MaxDps { get; init; } = 50000;              // 窗口内最大 DPS

    // ---- 世界/交互权威 (Phase 2 IWorldAuthority) ----
    public int MaxTileBreakPerSecond { get; init; } = 60;  // 每秒挖砖上限
    public int MaxTilePlacePerSecond { get; init; } = 40;  // 每秒放砖上限
    public int MaxProjectilesPerSecond { get; init; } = 30;

    // ---- 库存权威 (Phase 2 IInventoryAuthority) ----
    public bool SscEnabled { get; init; } = true;          // Server Side Characters
    public int MaxStackSize { get; init; } = 999;          // 单格最大堆叠

    // ---- 封禁 (Phase 6 IBanManager) ----
    public int MaxViolationsBeforeBan { get; init; } = 10; // 累计违规达此值自动封禁
    public int ViolationWindowMinutes { get; init; } = 60; // 违规时间窗口（滑动）

    // ---- 连接认证 (Phase 5) ----
    /// <summary>玩家名白名单（包 4 SyncPlayer）；为空表示不限制。</summary>
    public string[] PlayerWhitelist { get; init; } = System.Array.Empty<string>();

    // ---- 监控 (Phase 6 IMetrics) ----
    public bool MetricsEnabled { get; init; } = true;
    public int MetricsPort { get; init; } = 9090;          // Prometheus 抓取端口
}
