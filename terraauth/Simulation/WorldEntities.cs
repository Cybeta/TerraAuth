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

    /// <summary>拾取归属玩家（-1 = 无归属）。</summary>
    public int OwnedBy = -1;

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

    /// <summary>失效发生的 tick（用于延后清理）。</summary>
    public long DeadTick;

    /// <summary>失效是否已下发客户端（由世界同步线程置位，避免重复发包）。</summary>
    public bool RemovalNotified;
}
