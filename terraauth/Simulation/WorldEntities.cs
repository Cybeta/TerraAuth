// TerraAuth — 服务端权威的实体模型（掉落物 / 弹幕）
// 对应原版 Main.item[] / Main.projectile[]：位置与生命周期由服务端推进，
// 客户端上报仅作为「生成意图」，权威状态以服务端为准。

using TerraAuth.Protocol;

namespace TerraAuth.Simulation;

/// <summary>服务端权威的掉落物实体（对应原版 <c>Main.item[slot]</c>）。</summary>
public sealed class WorldItemEntity
{
    /// <summary>世界物品槽位（服务端分配，全局唯一）。</summary>
    public int Slot;

    public int ItemId;
    public int Stack;
    public Vector2 Position;
    public Vector2 Velocity;
    public byte Prefix;

    public bool Active = true;

    /// <summary>拾取归属玩家（-1 = 无归属，谁都能拾取；/give 专属掉落物 = 目标玩家）。</summary>
    public int OwnedBy = -1;

    /// <summary>原版 <c>WorldItem.DefaultGrabDelay</c>：丢弃后拾取延迟 100 tick ≈ 1.67s。</summary>
    public const int DefaultGrabDelay = 100;

    /// <summary>
    /// 当前动态归属（原版 <c>FindOwner</c> 结果，包 22 同步给客户端）：
    /// 原版 1.4.5.8 客户端 <c>Player.GrabItems</c> 只拾取 <c>playerIndexTheItemIsReservedFor == 自己</c> 的物品，
    /// 无主（255）物品反而**不可拾取**。255 = 无主，-1 = 尚未搜索（首次立即搜索并广播）。
    /// </summary>
    public int ReservedFor = -1;

    /// <summary>距上次 FindOwner 归属搜索的 tick 数（-1 = 尚未搜索）。</summary>
    public long OwnerSearchAge = -1;

    /// <summary>丢弃者玩家 ID（-1 = 非玩家丢弃，如 Boss 掉落 / 服务端生成）。</summary>
    public int DroppedBy = -1;

    /// <summary>
    /// 拾取延迟到期 tick（原版 <c>DefaultGrabDelay = 100</c> tick ≈ 1.67s，仅玩家丢弃设置）：
    /// 丢弃后延迟期间，丢弃者不能立即重新拾取（原版 ApplySpawnOwnership 设
    /// <c>grabDelayTime=100</c> / <c>grabDelayPlayer=丢弃者</c>，FindOwner 同时跳过丢弃者）。
    /// 0 = 无延迟。延迟通过包 22 的 grabDelayPlayer / grabDelayTime 字段带给客户端强制执行。
    /// </summary>
    public long GrabDelayExpireTick;

    /// <summary>剩余拾取延迟（tick）：延迟未激活时返回 0。</summary>
    public int RemainingGrabDelayTicks(long nowTick) =>
        (int)Math.Max(0, GrabDelayExpireTick - nowTick);

    /// <summary>失效是否已下发给客户端（由世界同步线程置位，避免重复发包）。</summary>
    public bool RemovalNotified;

    /// <summary>
    /// 是否为已知物品：由客户端上报生成的掉落物为 <c>true</c>（玩家自己已看到），
    /// 服务端主动生成的（如 Boss 掉落）为 <c>false</c>，需由世界同步补发包 21。
    /// </summary>
    public bool NewNotified = true;

    /// <summary>失效发生的 tick（用于延后清理）。</summary>
    public long DeadTick;

    /// <summary>
    /// 生成 tick（服务端时钟）：用于「本结算窗口内该玩家丢出了什么」的判定
    /// （宝袋的净减少需要被同窗口的掉落解释，见 <c>WorldState.CollectWindowWorldDrops</c>）。
    /// </summary>
    public long SpawnedTick;
}

/// <summary>服务端权威的弹幕实体（对应原版 <c>Main.projectile[key]</c>）。</summary>
public sealed class ProjectileEntity
{
    /// <summary>全局唯一索引（原版 <c>projectile.whoAmI</c>）。</summary>
    public int Key;

    /// <summary>归属玩家（仅归属者可销毁）。</summary>
    public int Owner;

    public int Type;
    public Vector2 Position;
    public Vector2 Velocity;
    public int Damage;

    /// <summary>创建该弹幕时使用的服务端物品；未知来源为 0。</summary>
    public int SourceItem;

    /// <summary>创建该弹幕时锁定的来源武器物品；实体创建后不随玩家换武器变化。</summary>
    public int SourceWeaponItem;

    /// <summary>创建该弹幕时锁定的来源武器前缀。</summary>
    public byte SourceWeaponPrefix;

    /// <summary>服务端为本次弹幕创建分配的来源事务标识；不是网络协议事务号。</summary>
    public long SourceTransactionId;
    public long FireTransactionId;

    /// <summary>服务端创建 tick，用于诊断和生命周期判断。</summary>
    public long SpawnTick;

    /// <summary>是否为召唤物实体。</summary>
    public bool IsSummon;

    /// <summary>服务端分配的稳定召唤实体标识；普通弹幕为 0。</summary>
    public long SummonEntityId;

    /// <summary>召唤实体分类；普通弹幕为 <see cref="SummonKind.None"/>。</summary>
    public SummonKind SummonKind;

    /// <summary>创建 Minion 的召唤 Buff；Sentry 和未知来源为 0。</summary>
    public int SourceSummonBuffId;

    /// <summary>剩余 NPC 穿透次数；-1 表示不因命中消耗。</summary>
    public int Penetrate = 1;

    /// <summary>普通弹幕对同一 NPC 的下一次可命中 tick。</summary>
    public Dictionary<int, long> NpcHitCooldownUntil { get; } = new();

    /// <summary>召唤弹幕对同一 NPC 的下一次可命中 tick。</summary>
    public Dictionary<int, long> SummonNpcHitCooldownUntil { get; } = new();

    /// <summary>
    /// 服务端**自算**的权威位置（**判定用**）；null = 该本体不由服务端持有位置（判定退回 <see cref="Position"/>）。
    /// 与 <see cref="Position"/> 的分工（W-2 第三档 `ServerAi` 的**混合模型**）：
    /// <see cref="Position"/> 始终是客户端包 27 上报的**表现用**坐标、原样广播给所有客户端（含主人），主人视角无抖动；
    /// 本字段只用于**存活 / 命中几何**判定，客户端无法影响它。
    /// 目前仅 AI_062 族（373 / 375 / 407 / 423 / 613 / 963）在 `ServerAi` 及以上被维护。
    /// </summary>
    public Vector2? ServerPosition { get; set; }

    /// <summary>服务端自算位置的速度状态（配合 <see cref="ServerPosition"/> 做惯性插值）。</summary>
    public Vector2? ServerVelocity { get; set; }

    /// <summary>碰撞盒尺寸（按原版 Projectile.SetDefaults 逐类型 width/height；未登记类型取 16 近似）。</summary>
    public float Width = 16f;
    public float Height = 16f;

    /// <summary>剩余生存 tick；归零即失效（默认 300 ≈ 5 秒 @60Hz）。</summary>
    public int TimeLeft = 300;

    public bool Active = true;

    /// <summary>
    /// 服务端永久销毁标记（如「移除召唤武器即销毁」/ 断线清场）。
    /// 置位后该弹幕不可被客户端包 27 更新复活，由世界同步广播包 29 后清理。
    /// </summary>
    public bool Destroyed;

    /// <summary>
    /// 图格反弹剩余次数（-1 = 未初始化 / 该型无反弹）。由行为表首次积分时填充
    /// （<see cref="WorldSimulator.ProjectileBehavior"/>）为原版 <c>penetrate/bounce</c> 预算，
    /// 撞图格反弹时递减；归零后再撞即失效。
    /// </summary>
    public int BouncesLeft = -1;

    /// <summary>失效发生的 tick（用于延后清理）。</summary>
    public long DeadTick;

    /// <summary>失效是否已下发客户端（由世界同步线程置位，避免重复发包）。</summary>
    public bool RemovalNotified;

    /// <summary>生成是否已下发客户端（包 27）：false → 世界同步循环负责推送新增弹幕。</summary>
    public bool NewNotified;
}
