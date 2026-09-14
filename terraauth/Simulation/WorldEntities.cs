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

    /// <summary>剩余生存 tick；归零即失效（默认 300 ≈ 5 秒 @60Hz）。</summary>
    public int TimeLeft = 300;

    public bool Active = true;

    /// <summary>
    /// 服务端永久销毁标记（如「移除召唤武器即销毁」/ 断线清场）。
    /// 置位后该弹幕不可被客户端包 27 更新复活，由世界同步广播包 29 后清理。
    /// </summary>
    public bool Destroyed;

    /// <summary>失效发生的 tick（用于延后清理）。</summary>
    public long DeadTick;

    /// <summary>失效是否已下发客户端（由世界同步线程置位，避免重复发包）。</summary>
    public bool RemovalNotified;

    /// <summary>生成是否已下发客户端（包 27）：false → 世界同步循环负责推送新增弹幕。</summary>
    public bool NewNotified;
}
