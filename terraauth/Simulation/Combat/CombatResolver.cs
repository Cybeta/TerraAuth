// TerraAuth — 服务端权威伤害结算：共享战斗判定 / 公式
// 供 WorldSimulator（接触兜底）与 CommandQueue（包 117 区间校验）共用，
// 保证两条路径的接触判定口径、伤害上界计算完全一致。

using TerraAuth.Simulation;

namespace TerraAuth.Simulation;

/// <summary>
/// 游戏难度（对应原版 <c>Main.GameMode</c>）：决定玩家受击伤害公式
/// （<see cref="CombatResolver.CalculateDamagePlayersTake"/>）与 NPC 对玩家的伤害倍率。
/// server.json 用字符串枚举读写（"Classic" / "Expert" / "Master"）。
/// </summary>
public enum GameMode
{
    /// <summary>经典：<c>dmg − def×0.5</c>（最低 1）。</summary>
    Classic = 0,

    /// <summary>专家：<c>dmg×2 − def×0.75</c>（最低 1）。</summary>
    Expert = 1,

    /// <summary>大师：<c>dmg×3 − def</c>（最低 1）。</summary>
    Master = 2,
}

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
    /// 原版 <c>Main.CalculateDamagePlayersTake</c>（Main.cs L89200），按难度取分支，最低 1：
    /// <list type="bullet">
    /// <item>经典：<c>dmg − def×0.5</c></item>
    /// <item>专家：<c>dmg×2 − def×0.75</c></item>
    /// <item>大师：<c>dmg×3 − def</c></item>
    /// </list>
    /// <paramref name="damage"/> 为 NPC 基础伤害（<see cref="NpcStatsTable"/>，原版正常模式数值）——
    /// 难度倍率内置于本公式（原版 1.4 起不再单独放大 <c>npc.damage</c>），
    /// 服务端上界与客户端 <c>Player.Hurt</c> 显示口径完全一致。
    /// </summary>
    public static int CalculateDamagePlayersTake(int damage, int defense, GameMode mode = GameMode.Classic)
        => mode switch
        {
            GameMode.Expert => Math.Max(1, damage * 2 - (int)Math.Round(defense * 0.75f)),
            GameMode.Master => Math.Max(1, damage * 3 - defense),
            _ => Math.Max(1, damage - (int)Math.Round(defense * 0.5f)),
        };

    /// <summary>
    /// 世界难度 int（<c>WorldState.GameMode</c>：0=普通、1=专家、2=大师、3=旅途）→ 战斗难度枚举。
    /// 3（旅途）未建模难度滑杆 → 按经典战斗公式兜底。
    /// </summary>
    public static GameMode FromWorldDifficulty(int difficulty)
        => difficulty switch
        {
            1 => GameMode.Expert,
            2 => GameMode.Master,
            _ => GameMode.Classic,
        };

    /// <summary>
    /// 原版 <c>Main.CalculateDamageNPCsTake</c>（Main.cs L89180）：<c>dmg - def×0.5</c>，最低 1。
    /// 供阶段 C（玩家攻击 NPC 弹幕匹配）与阶段 E（近战武器校验）使用。
    /// </summary>
    public static int CalculateDamageNPCsTake(int damage, int defense)
        => Math.Max(1, damage - (int)Math.Round(defense * 0.5f));

    /// <summary>
    /// 阶段 E：玩家**手持武器**的权威伤害（原版 <c>Player.GetWeaponDamage</c> 口径）：
    /// <c>base × (1 + 职业伤害%) × (1 + 全伤害%)</c>（Buff/药水实时生效，装备区穿齐套装另加职业加成）。
    /// 未收录武器（<paramref name="itemId"/> 不在 <see cref="ItemDamageTable.Of"/>）返回 0（调用方失败放行）。
    /// </summary>
    public static int GetWeaponDamage(PlayerRuntime player, int itemId)
    {
        if (!ItemDamageTable.Of.TryGetValue(itemId, out var stats))
            return 0;

        double damage = stats.Damage;
        damage *= 1.0 + BuffTable.ClassDamagePercent(player.Buffs, stats.Class) / 100.0;
        damage *= 1.0 + BuffTable.AllDamagePercent(player.Buffs) / 100.0;

        // 套装职业加成（阶段 E-2）：穿齐熔岩套等 → 对应职业伤害 +%（ArmorSetBonuses 权威）。
        if (ArmorSetBonusTable.BonusForEquipment(player.Items) is { } set)
        {
            int setPct = stats.Class switch
            {
                WeaponClass.Melee => set.MeleePct,
                WeaponClass.Ranged => set.RangedPct,
                WeaponClass.Magic => set.MagicPct,
                _ => 0,
            };
            damage *= 1.0 + setPct / 100.0;
        }
        return Math.Max(1, (int)damage);
    }

    /// <summary>
    /// 阶段 E：近战命中的**上报值上界** = <c>ceil(权威伤害 × 1.15) × (crit ? 2 : 1)</c>
    /// （与阶段 C 弹幕匹配同口径：±15% 浮动上界 → 暴击倍率；防御减伤在服务端结算时另行应用）。
    /// </summary>
    public static int WeaponDamageBound(PlayerRuntime player, int itemId, bool crit)
        => (int)Math.Ceiling(GetWeaponDamage(player, itemId) * 1.15f) * (crit ? 2 : 1);

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
