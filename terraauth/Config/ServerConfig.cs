// Phase 6 - 服务端配置定义
// 反作弊阈值的唯一来源（架构 §4.5）

using TerraAuth.ModCompat;
using TerraAuth.Simulation;

namespace TerraAuth.Config;

public record ServerConfig
{
    // ---- 网络 ----
    public int MaxConnections { get; init; } = 64;
    public int HandshakeTimeoutSeconds { get; init; } = 10;
    public int SnapshotRateHz { get; init; } = 20;
    public float ViewportRadius { get; init; } = 2000f;   // 快照视野半径（像素）；0 = 不裁剪

    // ---- 世界 ----
    /// <summary>基准世界文件路径（.wld）；为空或文件不存在则按 <see cref="WorldSize"/> 程序化生成。</summary>
    public string WorldPath { get; init; } = "";

    /// <summary>
    /// 程序化生成的世界尺寸档（server.json 用字符串枚举："Small" / "Medium" / "Large"），
    /// 对应原版三档：小 4200×1200、中 6400×1800、大 8400×2400。
    /// 仅在 <see cref="WorldPath"/> 为空或文件不存在时生效（指定真实 .wld 时以文件尺寸为准）。
    /// </summary>
    public WorldSize WorldSize { get; init; } = WorldSize.Small;

    /// <summary>
    /// 程序化生成的随机种子（决定地形 / 群系选择 / 矿脉分布）。**改种子 = 换一张新地图**，重启生效。
    /// 仅在 <see cref="WorldPath"/> 为空或文件不存在时生效。
    /// 换种子后请同时把 <see cref="ResetWorldChangesOnStart"/> 置 true 跑一次，清掉旧地图坐标上的改动。
    /// </summary>
    public int WorldSeed { get; init; } = 20260909;

    /// <summary>
    /// 启动时清空持久化的世界改动（图格 + 箱子内容）——换种子 / 换 .wld 重开地图时置 true 用一次，
    /// 避免旧地图坐标上的改动叠加到新地形。**用完请改回 false**：置 true 期间每次重启都会丢弃玩家改动。
    /// </summary>
    public bool ResetWorldChangesOnStart { get; init; }

    /// <summary>
    /// 世界导出路径（.wld）；为空则不导出。设置后：停机时导出一次，且在**无人在线**时按
    /// <see cref="WorldExportIntervalSeconds"/> 周期导出（全量遍历 O(世界大小)，故避开在线时段）。
    /// 与 <see cref="WorldPath"/> 相同即「原地保存」（导出前旧文件滚动为 .bak）。
    /// </summary>
    public string WorldExportPath { get; init; } = "";

    /// <summary>空服导出世界的间隔（秒）；仅在 <see cref="WorldExportPath"/> 非空且无人在线时生效。</summary>
    public int WorldExportIntervalSeconds { get; init; } = 600;

    /// <summary>
    /// 同屏敌怪上限（达到后停止刷新）；只统计存活且**非城镇**的 NPC，普通情况即史莱姆。
    /// 调小便于单人测试（例如 2）。默认 8。
    /// </summary>
    public int MaxEnemies { get; init; } = 8;

    /// <summary>
    /// 会话恢复宽限期（秒）：玩家断线后在此时长内以**同一玩家名**重连，服务端把原运行时（位置 / 血量 / 增益）
    /// 交还给他，而不是当作新玩家从头进服。0 = 关闭（断线即回收）。
    /// 注意：原版客户端断线只会退回主菜单、手动重进，故这是「手动重进的会话接管」，非自动重连。
    /// </summary>
    public int SessionResumeGraceSeconds { get; init; } = 60;

    // ---- 玩家属性权威 (Phase 2 IPlayerAuthority) ----
    public int MaxPlayerHp { get; init; } = 500;           // 玩家生命上限（客户端不得抬高，超出即纠正）
    public int MaxPlayerMana { get; init; } = 200;         // 玩家法力上限

    // ---- 移动权威 (Phase 2 IMovementAuthority) ----
    // 单一速度上限：飞行上限同时覆盖步行/冲刺以降低误判（见 GameHost.AuthorityThresholds.From）。
    public float MaxFlightSpeed { get; init; } = 8.0f;
    public float MaxFallSpeed { get; init; } = 20.0f;
    public float TeleportTolerance { get; init; } = 4.0f;  // 单 tick 允许最大位移（防瞬移）
    public float ShadowPredictionMaxDeviation { get; init; } = 8.0f; // 影子预测偏差阈值（px）

    // ---- 战斗权威 (Phase 2 ICombatAuthority) ----
    /// <summary>
    /// 游戏难度（server.json 用字符串枚举："Classic" / "Expert" / "Master"）。
    /// 决定玩家受击伤害公式（原版 <c>Main.CalculateDamagePlayersTake</c>）：
    /// 经典 <c>dmg−def×0.5</c>、专家 <c>dmg×2−def×0.75</c>、大师 <c>dmg×3−def</c>（最低 1）。
    /// 包 117 区间校验与接触兜底结算均按此取分支，保证服务端权威口径 = 客户端显示。
    /// </summary>
    public GameMode GameMode { get; init; } = GameMode.Classic;

    public int MaxSingleDamage { get; init; } = 30000;     // 单次伤害上限
    public int MaxDpsWindowSeconds { get; init; } = 5;     // DPS 统计窗口
    public int MaxDps { get; init; } = 50000;              // 窗口内最大 DPS

    // ---- 世界/交互权威 (Phase 2 IWorldAuthority) ----
    public int MaxTileBreakPerSecond { get; init; } = 60;  // 每秒挖砖上限
    public int MaxTilePlacePerSecond { get; init; } = 40;  // 每秒放砖上限
    public int MaxProjectilesPerSecond { get; init; } = 30;

    // ---- 限流权威 (Phase 2 IRateAuthority) ----
    public int MaxPacketsPerSecond { get; init; } = 120;   // 单玩家每秒上行包总量上限
    public int MaxChatPerMinute { get; init; } = 30;       // 单玩家每分钟聊天上限
    public int MaxLiquidPerSecond { get; init; } = 60;     // 单玩家每秒液体编辑帧上限（包 82 模块 0）

    // ---- 库存权威 (Phase 2 IInventoryAuthority) ----
    public bool SscEnabled { get; init; } = true;          // Server Side Characters
    public int MaxStackSize { get; init; } = 999;          // 单格最大堆叠

    /// <summary>
    /// 玩家从背包移除（清空 / 换出）召唤武器后，是否立即销毁该玩家已召唤的弹幕。
    /// false（默认）= 原版行为：召唤物不随武器移除而消失，继续攻击至自然消失 / 替换 / 掉线；
    /// true = 武器移除即销毁对应召唤弹幕（服务端置 Active=false，由世界同步补发包 29 广播销毁）。
    /// </summary>
    public bool DestroySummonsOnWeaponRemoval { get; init; } = false;

    // ---- 封禁 / 违规处置 (Phase 6 IBanManager + Phase 5 连接处置) ----
    public int MaxViolationsBeforeBan { get; init; } = 10; // 窗口内累计违规达此值 → 封禁记录 + 踢出连接
    public int ViolationWindowMinutes { get; init; } = 60; // 违规时间窗口（滑动，分钟）

    // ---- 连接认证 (Phase 5) ----
    /// <summary>玩家名白名单（包 4 SyncPlayer）；为空表示不限制。</summary>
    public string[] PlayerWhitelist { get; init; } = System.Array.Empty<string>();

    // ---- Mod 兼容层 (需求 2) ----
    /// <summary>
    /// Mod 策略（server.json 的 ModPolicy 节）。默认 VanillaOnly = 仅允许原版客户端；
    /// Mode 支持字符串枚举（"VanillaOnly" / "Whitelist" / "Blacklist" / "AllowAll"）。
    /// </summary>
    public ModPolicy ModPolicy { get; init; } = new();

    // ---- 监控 (Phase 6 IMetrics) ----
    public bool MetricsEnabled { get; init; } = true;
    public int MetricsPort { get; init; } = 9090;          // Prometheus 抓取端口

    // ---- 诊断 ----
    /// <summary>
    /// 热路径诊断日志开关（真机排障用）：开启后输出逐包 / 逐次受伤 / 逐次命中 / 召唤销毁等明细，
    /// 供定位「没碰到却掉血」「掉落物拾取」类问题。**默认关闭**——这些输出正常游玩时是纯噪声。
    /// 生产部署保持默认即可；排障时置 true（支持热重载，见 <c>DiagnosticLog</c>）。
    /// </summary>
    public bool VerboseDiagnostics { get; init; }
}
