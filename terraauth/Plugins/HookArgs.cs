// TerraAuth — Hook 事件参数
// 覆盖 Phase 2 六种权威子系统（Player/Movement/Combat/Inventory/World/Rate）+ 服务器生命周期
// 每个 Hook 标注：触发阶段、是否可取消、是否可修改 —— 供插件开发者参考

using System;
using System.Collections.Generic;

namespace TerraAuth.Plugins;

#region 玩家生命周期
/// <summary>玩家连接前（可取消）。触发于 ConnectionState 阶段之后、鉴权前。</summary>
public sealed class PlayerConnectingArgs : HookArgs
{
    public string IpAddress { get; set; } = "";
    public int Port { get; set; }
    public string ClientVersion { get; set; } = "";
    public ClientCapabilities Capabilities { get; set; } = new();
}
/// <summary>玩家成功加入（不可取消）。</summary>
public sealed class PlayerJoinedArgs : HookArgs
{
    public PlayerStateSnapshot State { get; set; } = null!;
}
/// <summary>玩家离开。</summary>
public sealed class PlayerLeftArgs : HookArgs
{
    public string Reason { get; set; } = "";
    public TimeSpan SessionDuration { get; set; }
}
#endregion

#region 玩家属性（对接 PlayerAuthority）
/// <summary>玩家移动（可取消 / 可修改位置速度）。触发于 MovementAuthorityStage。</summary>
/// <remarks>
/// 已填充：<see cref="ToX"/> / <see cref="ToY"/> / <see cref="VelocityX"/> / <see cref="VelocityY"/>
/// （取自包 13 客户端上报值；速度仅在该包携带时非 0）。
/// 未填充（保持默认值）：<see cref="FromX"/> / <see cref="FromY"/>（触发点早于权威层，拿不到上一位置）、
/// <see cref="IsFlying"/> / <see cref="IsUsingHook"/>（控制位语义未解析）。
/// </remarks>
public sealed class PlayerMovingArgs : HookArgs
{
    public float FromX { get; set; }
    public float FromY { get; set; }
    public float ToX { get; set; }
    public float ToY { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public bool IsFlying { get; set; }
    public bool IsUsingHook { get; set; }
}
/// <summary>玩家 HP/MP 变化（可取消 / 可修改）。触发于 PlayerAuthorityStage。</summary>
public sealed class PlayerStatChangedArgs : HookArgs
{
    public int OldHp { get; set; }
    public int NewHp { get; set; }
    public int MaxHp { get; set; }
    public int OldMp { get; set; }
    public int NewMp { get; set; }
    public int MaxMp { get; set; }
}
/// <summary>玩家死亡。</summary>
public sealed class PlayerDiedArgs : HookArgs
{
    public string Source { get; set; } = "";
    public int LossCoins { get; set; }
}
#endregion

#region 物品与经济（对接 InventoryAuthority）
/// <summary>物品拾取（可取消）。</summary>
public sealed class ItemPickupArgs : HookArgs
{
    public int ItemId { get; set; }
    public int Stack { get; set; }
    public int Prefix { get; set; }
}
/// <summary>物品丢弃（可取消）。</summary>
/// <remarks>
/// 已填充：包 21（丢弃）填 <see cref="ItemId"/> / <see cref="Stack"/> / <see cref="X"/> / <see cref="Y"/>；
/// 包 31（请求开箱）当前也映射到本类型，仅填 <see cref="X"/> / <see cref="Y"/>（箱子图格坐标），
/// 此时 <see cref="ItemId"/> / <see cref="Stack"/> 为 0。
/// </remarks>
public sealed class ItemDropArgs : HookArgs
{
    public int ItemId { get; set; }
    public int Stack { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}
#endregion

#region 战斗（对接 CombatAuthority）
/// <summary>NPC 被攻击（可取消 / 可修改伤害）。触发于 CombatAuthorityStage。</summary>
/// <remarks>
/// 已填充：<see cref="NpcId"/> / <see cref="Damage"/>（取自包 28 解码结果）。
/// 未填充（保持默认值）：<see cref="NpcType"/> / <see cref="Knockback"/> / <see cref="Direction"/> /
/// <see cref="Critical"/> / <see cref="WeaponItemId"/> —— 解码器尚未提取这些字段。
/// </remarks>
public sealed class NpcStrikeArgs : HookArgs
{
    public int NpcId { get; set; }
    public int NpcType { get; set; }
    public int Damage { get; set; }
    public float Knockback { get; set; }
    public int Direction { get; set; }
    public bool Critical { get; set; }
    public int WeaponItemId { get; set; }
}
/// <summary>抛射物生成（可取消 / 可修改）。</summary>
/// <remarks>
/// 已填充：<see cref="ProjectileId"/>（实体 key）/ <see cref="Type"/> / <see cref="X"/> / <see cref="Y"/> /
/// <see cref="VelocityX"/> / <see cref="VelocityY"/> / <see cref="Damage"/>。
/// 未填充（保持默认值）：<see cref="OwnerId"/> —— 包 27 未携带所有者信息（服务端应以发送者为准）。
/// </remarks>
public sealed class ProjectileSpawnArgs : HookArgs
{
    public int ProjectileId { get; set; }
    public int Type { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public int OwnerId { get; set; }
    public float Damage { get; set; }
}
#endregion

#region 世界（对接 WorldAuthority / RateAuthority）
/// <summary>方块放置（可取消）。</summary>
public sealed class TilePlaceArgs : HookArgs
{
    public int X { get; set; }
    public int Y { get; set; }
    public int TileType { get; set; }
}
/// <summary>方块破坏（可取消）。</summary>
public sealed class TileBreakArgs : HookArgs
{
    public int X { get; set; }
    public int Y { get; set; }
    public int TileType { get; set; }
}
/// <summary>NPC 生成（可取消）。</summary>
public sealed class NpcSpawnArgs : HookArgs
{
    public int NpcType { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}
#endregion

#region 服务器生命周期
/// <summary>服务器已启动。</summary>
public sealed class ServerStartedArgs : HookArgs { }
/// <summary>服务器关闭中。</summary>
public sealed class ServerStoppingArgs : HookArgs
{
    public string Reason { get; set; } = "";
}
/// <summary>每 Tick 触发（不可取消，用于周期性任务）。</summary>
public sealed class ServerTickArgs : HookArgs
{
    public long Tick { get; set; }
    public double DeltaTime { get; set; }
}
/// <summary>命令执行（可取消）。</summary>
public sealed class CommandExecutingArgs : HookArgs
{
    public string Command { get; set; } = "";
    public IReadOnlyList<string> Args { get; set; } = Array.Empty<string>();
    public string Source { get; set; } = ""; // Console / Chat / Rcon
}
#endregion
