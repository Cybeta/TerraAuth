// TerraAuth — 服务端权威伤害结算：共享战斗判定 / 公式
// 供 WorldSimulator（接触兜底）与 CommandQueue（包 117 区间校验）共用，
// 保证两条路径的接触判定口径、伤害上界计算完全一致。

using TerraAuth.Simulation;

namespace TerraAuth.Simulation;

/// <summary>服务端权威战斗判定与伤害公式（无状态，公式照抄原版）。</summary>
public static class CombatResolver
{
    /// <summary>
    /// 原版 <c>Main.DamageVar</c>（Main.cs L89151）：±15% 整数随机浮动。
    /// 仅用于服务端兜底结算（玩家漏报 117 时）；117 主路径用客户端上报值（含其本地浮动）。
    /// </summary>
    public static int DamageVar(int damage, IRng rng)
    {
        int variance = rng.NextInt32(31) - 15; // [-15, 15]
        return Math.Max(1, (int)Math.Round(damage * (1f + variance * 0.01f)));
    }

    /// <summary>
    /// 原版 <c>Main.CalculateDamagePlayersTake</c>（Main.cs L89200）：经典难度 <c>dmg - def×0.5</c>，最低 1。
    /// 大师模式分支（<c>dmg - def</c>）随阶段 D 的难度配置补入。
    /// </summary>
    public static int CalculateDamagePlayersTake(int damage, int defense)
        => Math.Max(1, damage - (int)Math.Round(defense * 0.5f));

    /// <summary>
    /// 原版 <c>Main.CalculateDamageNPCsTake</c>（Main.cs L89180）：<c>dmg - def×0.5</c>，最低 1。
    /// 供阶段 C（玩家攻击 NPC 弹幕匹配）与后续近战校验使用。
    /// </summary>
    public static int CalculateDamageNPCsTake(int damage, int defense)
        => Math.Max(1, damage - (int)Math.Round(defense * 0.5f));

    /// <summary>
    /// 查找与玩家碰撞盒重叠的敌怪伤害（取接触者中的最大值）；无接触返回 0。
    /// 判定口径与原版 <c>Player.Update_NPCCollision</c> 一致：玩家 / NPC 盒各自取整后做 AABB 求交，
    /// **不设最小重叠**（两轴各 1px 即命中），且用逐类型尺寸而非「点 + 半径」。
    /// <paramref name="contactNpc"/> 回传实际接触的 NPC（诊断输出用）。
    /// </summary>
    public static int FindContactDamage(WorldState world, PlayerRuntime player, out WorldNpc? contactNpc, out int contactIndex)
    {
        float px = player.AimPosition.X, py = player.AimPosition.Y;
        int best = 0;
        contactNpc = null;
        contactIndex = -1;

        lock (world.NpcsLock)
        {
            for (var i = 0; i < world.Npcs.Count; i++)
            {
                var npc = world.Npcs[i];
                if (!npc.Active || npc.IsTownNpc) continue;

                var (width, height) = NpcSizes.Of(npc.Type);
                if (!PlayerTouchesNpc(px, py, npc.X, npc.Y, width, height))
                    continue;

                int damage = NpcStatsTable.Of.TryGetValue(npc.Type, out var stats) ? stats.Damage : 7;
                if (damage > best)
                {
                    best = damage;
                    contactNpc = npc;
                    contactIndex = i;
                }
            }
        }

        return best;
    }

    /// <summary>玩家此刻是否与任一敌怪接触（包 117 区间校验的前置条件，防伪造远程受伤）。</summary>
    public static bool IsPlayerInContact(WorldState world, PlayerRuntime player)
        => FindContactDamage(world, player, out _, out _) > 0;

    /// <summary>
    /// 玩家盒（<see cref="NpcSizes.PlayerWidth"/> × <see cref="NpcSizes.PlayerHeight"/>）与 NPC 盒是否相交。
    /// 原版 <c>Player.Update_NPCCollision</c> 的做法是 <c>new Rectangle((int)position.X, (int)position.Y, width, height)</c>
    /// 对 NPC 同法取整后 <c>Rectangle.Intersects</c> —— **取整后再比、无最小重叠**。
    /// </summary>
    private static bool PlayerTouchesNpc(float px, float py, float nx, float ny, int nw, int nh)
    {
        int px0 = (int)px, py0 = (int)py;
        int nx0 = (int)nx, ny0 = (int)ny;
        return px0 < nx0 + nw && nx0 < px0 + NpcSizes.PlayerWidth
            && py0 < ny0 + nh && ny0 < py0 + NpcSizes.PlayerHeight;
    }
}
