// TerraAuth — 服务端权威伤害结算：共享战斗判定 / 公式
// 供 WorldSimulator（接触兜底）与 CommandQueue（包 117 区间校验）共用，
// 保证两条路径的接触判定口径、伤害上界计算完全一致。

using TerraAuth.Simulation;

namespace TerraAuth.Simulation;

/// <summary>
/// 游戏难度（对应原版 <c>Main.GameMode</c>）：决定玩家受击公式的**减防系数**
/// （<see cref="CombatResolver.CalculateDamagePlayersTake"/>）与 NPC 生命 / 伤害的难度倍率
/// （<see cref="CombatResolver.ScaleNpcLifeMax"/> / <see cref="CombatResolver.ScaleNpcDamage"/>）。
/// server.json 用字符串枚举读写（"Classic" / "Expert" / "Master"）。
/// </summary>
public enum GameMode
{
    /// <summary>经典：<c>dmg − def×0.5</c>（最低 1），NPC 生命 / 伤害 ×1。</summary>
    Classic = 0,

    /// <summary>专家：<c>dmg − def×0.75</c>（最低 1），NPC 生命 / 伤害 ×2。</summary>
    Expert = 1,

    /// <summary>大师：<c>dmg − def</c>（最低 1），NPC 生命 / 伤害 ×3。</summary>
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
    /// 原版 <c>Main.CalculateDamagePlayersTake</c>（Main.cs L89200），按难度取**减防系数**，最低 1：
    /// <list type="bullet">
    /// <item>经典：<c>dmg − def×0.5</c></item>
    /// <item>专家：<c>dmg − def×0.75</c></item>
    /// <item>大师：<c>dmg − def</c></item>
    /// </list>
    /// **伤害值本身不受本公式影响** —— 专家 / 大师的 NPC 伤害放大发生在生成时
    /// （原版 <c>NPC.ScaleStats_ByDifficulty</c> → <c>GetAttackDamage_ScaledByDifficulty</c>，
    /// 见 <see cref="ScaleNpcDamage"/>）；本公式若再乘一次倍率就是双倍放大。
    /// <paramref name="damage"/> 取 **NPC 实体上的权威伤害**（已含难度与防御前的原始值）。
    /// </summary>
    public static int CalculateDamagePlayersTake(int damage, int defense, GameMode mode = GameMode.Classic)
        => mode switch
        {
            GameMode.Master => Math.Max(1, damage - defense),
            GameMode.Expert => Math.Max(1, damage - (int)Math.Round(defense * 0.75f)),
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
    /// 原版 <c>Main.Difficulty</c> 的难度曲线值（<c>GameDifficultyLevel</c>：经典 1 / 专家 2 / 大师 3）。
    /// 用于包 23 的**难度覆盖段**：客户端收到该值后按同一条曲线缩放 NPC 生命 / 伤害，
    /// 从而与本服务端自算的 <c>lifeMax</c> 一致（否则客户端会按经典缩放，血条与服务端发散）。
    /// 旅途（0.5）未建模 → 按经典 1。
    /// </summary>
    public static float DifficultyValue(int worldDifficulty)
        => worldDifficulty switch
        {
            1 => 2f,
            2 => 3f,
            _ => 1f,
        };

    /// <summary>
    /// 原版 <c>GameDifficultyData.EnemyMaxLifeMultiplier</c>（其曲线在经典~大师区间恰好等于难度值本身）：
    /// 专家 ×2、大师 ×3。旅途 0.5 / 传奇 5.33 未建模。
    /// </summary>
    public static float NpcLifeMultiplier(GameMode mode)
        => mode switch
        {
            GameMode.Master => 3f,
            GameMode.Expert => 2f,
            _ => 1f,
        };

    /// <summary>原版 <c>GameDifficultyData.EnemyDamageMultiplier</c>（同一曲线口径）：专家 ×2、大师 ×3。</summary>
    public static float NpcDamageMultiplier(GameMode mode) => NpcLifeMultiplier(mode);

    /// <summary>原版 <c>NPC.ScaleStats_ByDifficulty</c> 的生命部分：<c>(int)(lifeMax × 倍率)</c>（截断，最低 1）。</summary>
    public static int ScaleNpcLifeMax(int lifeMax, GameMode mode)
        => Math.Max(1, (int)(lifeMax * NpcLifeMultiplier(mode)));

    /// <summary>原版 <c>NPC.GetAttackDamage_ScaledByDifficulty</c>：<c>(int)(damage × 倍率)</c>（截断）。</summary>
    public static int ScaleNpcDamage(int damage, GameMode mode)
        => (int)(damage * NpcDamageMultiplier(mode));

    /// <summary>
    /// 原版 <c>Main.CalculateDamageNPCsTake</c>（Main.cs L89180）：<c>dmg - def×0.5</c>，最低 1。
    /// 供阶段 C（玩家攻击 NPC 弹幕匹配）与阶段 E（近战武器校验）使用。
    /// </summary>
    public static int CalculateDamageNPCsTake(int damage, int defense)
        => Math.Max(1, damage - (int)Math.Round(defense * 0.5f));

    /// <summary>
    /// 某职业的伤害修饰百分比合计（%）：Buff 职业% + Buff 全% + 饰品职业% + 饰品全% + 护甲单件职业%
    /// + 套装职业%（加算，原版口径）。供 <see cref="GetWeaponDamage"/> 与 <see cref="RangedDamageBound"/>
    /// （弹药部分按原版 <c>GetWeaponDamageMultiplier</c> 用同一修饰倍率）共用。
    /// </summary>
    private static double GetClassModifierPercent(PlayerRuntime player, WeaponClass cls)
    {
        double totalPct =
            BuffTable.ClassDamagePercent(player.Buffs, cls)
            + BuffTable.AllDamagePercent(player.Buffs)
            + AccessoryTable.ClassDamagePercent(player.Items, cls)
            + AccessoryTable.AllDamagePercent(player.Items)
            + ArmorPieceBonusTable.ClassDamagePercent(player.Items, cls); // 阶段 E-5：护甲单件（头/胸/腿）职业加成，与套装加成加算

        // 套装职业加成（阶段 E-2）：穿齐熔岩套等 → 对应职业伤害 +%（ArmorSetBonuses 权威）；
        // 全伤害类套装（南瓜/水晶刺客）经 AllDamage 计入所有职业。
        if (ArmorSetBonusTable.BonusForEquipment(player.Items) is { } set)
        {
            totalPct += set.AllDamage + (cls switch
            {
                WeaponClass.Melee => set.MeleePct,
                WeaponClass.Ranged => set.RangedPct,
                WeaponClass.Magic => set.MagicPct,
                _ => 0,
            });
        }

        return totalPct;
    }

    /// <summary>
    /// 阶段 E：玩家**手持武器**的权威伤害（原版 <c>Player.GetWeaponDamage</c> 口径）：
    /// <c>base × (1 + 总修饰%)</c>。各类修饰**加算**累进职业伤害字段（原版无独立 allDamage 字段，
    /// 「全伤害」如 Wrath/复仇者徽章是对四职业字段 += 同一值）：Buff 职业% + Buff 全% + 套装职业% + 饰品职业% + 饰品全%。
    /// 带前缀武器（<paramref name="prefix"/>）先按 <see cref="PrefixDamageTable"/> 修正基础伤害
    /// （向上取整，保证上界不低于客户端任何舍入口径；阶段 E-4 防「+伤害前缀」被误拒）。
    /// 未收录武器（<paramref name="itemId"/> 不在 <see cref="ItemDamageTable.Of"/>）返回 0（调用方失败放行）。
    /// </summary>
    public static int GetWeaponDamage(PlayerRuntime player, int itemId, byte prefix = 0)
    {
        if (!ItemDamageTable.Of.TryGetValue(itemId, out var stats))
            return 0;

        double baseDmg = Math.Ceiling(stats.Damage * PrefixDamageTable.DamageMultiplier(prefix));
        return Math.Max(1, (int)(baseDmg * (1.0 + GetClassModifierPercent(player, stats.Class) / 100.0)));
    }

    /// <summary>
    /// 阶段 E：近战命中的**上报值上界** = <c>ceil(权威伤害 × 1.15) × (crit ? 2 : 1)</c>
    /// （与阶段 C 弹幕匹配同口径：±15% 浮动上界 → 暴击倍率；防御减伤在服务端结算时另行应用）。
    /// <paramref name="prefix"/> 为手持武器前缀（透传 <see cref="GetWeaponDamage"/>）。
    /// </summary>
    public static int WeaponDamageBound(PlayerRuntime player, int itemId, bool crit, byte prefix = 0)
        => (int)Math.Ceiling(GetWeaponDamage(player, itemId, prefix) * 1.15f) * (crit ? 2 : 1);

    /// <summary>
    /// 阶段 F：远程命中的**上报值上界**（含弹药合并，原版 <c>Player.ItemCheck_Shoot</c> 口径）：
    /// <c>Damage = GetWeaponDamage(武器，含前缀+修饰) + ammo.damage × GetWeaponDamageMultiplier(弹药)</c>
    /// （弹药乘本类修饰倍率；枪械 gunProj 四件与原版一致**不合并**弹药，未收录弹药类型同理）。
    /// 上界 = <c>ceil(权威总伤害 × 1.15) × (crit ? 2 : 1)</c>。
    /// 弹药部分取玩家物品栏中与武器同类型弹药的**最高**基础伤害（原版 PickAmmo 扫栏取值 × 修饰；
    /// 未收录于 <see cref="AmmoDamageTable"/> 的同类弹药原版基础伤害为 0，按 0 计，绝不误拒）。
    /// 返回 null（非远程 / 未收录武器）→ 调用方失败放行。
    /// </summary>
    public static int? RangedDamageBound(PlayerRuntime player, int itemId, bool crit, byte prefix = 0)
    {
        if (!ItemDamageTable.Of.TryGetValue(itemId, out var stats) || stats.Class != WeaponClass.Ranged)
            return null;

        int weaponDmg = GetWeaponDamage(player, itemId, prefix);
        int ammoType = WeaponAmmoTypeOf.Of.TryGetValue(itemId, out var at) ? at : 0;

        // 无弹药合并（投掷 / 鱼叉 / gunProj 等）→ 弹幕伤害 = 武器伤害，与近战同口径校验。
        if (ammoType == 0)
            return (int)Math.Ceiling(weaponDmg * 1.15f) * (crit ? 2 : 1);

        int bestAmmo = 0;
        foreach (int slot in player.Items)
        {
            if (slot <= 0 || !AmmoTypeOf.Of.TryGetValue(slot, out int a) || a != ammoType)
                continue;
            if (AmmoDamageTable.Of.TryGetValue(slot, out int ammoDmg) && ammoDmg > bestAmmo)
                bestAmmo = ammoDmg;
        }

        double ammoContribution = bestAmmo > 0
            ? Math.Ceiling(bestAmmo * (1.0 + GetClassModifierPercent(player, WeaponClass.Ranged) / 100.0))
            : 0;
        return (int)Math.Ceiling((weaponDmg + ammoContribution) * 1.15f) * (crit ? 2 : 1);
    }

    /// <summary>
    /// 阶段 G：召唤 / 哨兵命中的**上报值上界**——玩家拥有的存活召唤弹幕**最高**伤害。
    /// 原版召唤物仆从伤害 ≠ 手持武器（召唤后切换武器仍沿用创建时伤害），故按手持武器校验
    /// 会误拒合法命中；改为取该玩家所有存活召唤弹幕（<see cref="SummonProjectileTable"/> 的
    /// 身份集合 = 本体 ∪ 派生弹幕）的最高 Damage 作基准（客户端打中的任意一枚召唤物伤害 ≤ 最高者，永不误拒）。
    /// 弹幕伤害由客户端包 27 创建时上报（已含 minionDamage 修饰，攻击时经 DamageVar ±15% × 暴击），
    /// 上界 = <c>ceil(最高弹幕伤害 × 1.15) × (crit ? 2 : 1)</c>。
    /// 返回 null（无存活召唤弹幕）→ 调用方失败放行，绝不误拒。
    /// </summary>
    /// <param name="excludeBodies">
    /// 是否排除召唤 / 哨兵的**本体**（<see cref="SummonEntityTable"/> 的 62 条）。
    /// <see cref="SummonAuthorityMode.ServerDamage"/> 及以上档位必须为 true：本体的命中伤害已由服务端裁定，
    /// 若仍把本体计入上界，玩家就能手持任意武器、拿高伤本体当"上界"上报伤害。
    /// 本体发射的派生弹幕不在此列（它们仍走客户端上报 + 既有校验）。
    /// </param>
    public static int? SummonDamageBound(WorldState world, int playerId, bool crit, bool excludeBodies = false)
    {
        int best = 0;
        lock (world.ProjectilesLock)
        {
            foreach (var p in world.Projectiles)
            {
                if (!p.Active || p.Owner != playerId || p.Damage <= 0) continue;
                if (!p.IsSummon && !SummonProjectileTable.Of.Contains(p.Type)) continue;
                if (excludeBodies && SummonEntityTable.Of.ContainsKey(p.Type)) continue;
                if (p.Damage > best) best = p.Damage;
            }
        }

        if (best <= 0) return null;
        return (int)Math.Ceiling(best * 1.15f) * (crit ? 2 : 1);
    }

    /// <summary>
    /// 查找与玩家碰撞盒重叠的敌怪伤害（取接触者中的最大值）；无接触返回 0。
    /// 判定口径与原版 <c>Player.Update_NPCCollision</c> 一致：玩家 / NPC 盒各自取整后做 AABB 求交，
    /// **不设最小重叠**（两轴各 1px 即命中），且用逐类型尺寸而非「点 + 半径」。
    /// 伤害取 **NPC 实体上的权威值**（生成时已按世界难度放大，见 <c>WorldSimulator.AddNpc</c>）。
    /// <paramref name="contactNpc"/> 回传实际接触的 NPC（诊断输出用）。
    /// </summary>
    public static int FindContactDamage(WorldState world, PlayerRuntime player, out WorldNpc? contactNpc, out int contactIndex)
    {
        float px = player.AimPosition.X, py = player.AimPosition.Y;
        int best = 0;
        contactNpc = null;
        contactIndex = -1;

        var mode = FromWorldDifficulty(world.GameMode);

        lock (world.NpcsLock)
        {
            for (var i = 0; i < world.Npcs.Count; i++)
            {
                var npc = world.Npcs[i];
                if (!npc.Active || npc.IsTownNpc) continue;

                var (width, height) = NpcSizes.Of(npc.Type);
                if (!PlayerTouchesNpc(px, py, npc.X, npc.Y, width, height))
                    continue;

                int damage = ContactDamageOf(npc, mode);
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

    /// <summary>
    /// 接触伤害取值：优先用 NPC 实体上的权威伤害（生成 / 换型时已按难度缩放）；
    /// 实体尚未回填（直接构造的 NPC）或类型未收录时，退回**同样按难度缩放**的表值 / 兜底 7 ——
    /// 保证专家 / 大师的接触伤害不会因为走了哪条生成路径而不一致。
    /// </summary>
    private static int ContactDamageOf(WorldNpc npc, GameMode mode)
    {
        if (npc.Damage > 0) return npc.Damage;
        int baseDamage = NpcStatsTable.Of.TryGetValue(npc.Type, out var stats) ? stats.Damage : 0;
        if (baseDamage <= 0) baseDamage = DefaultContactDamage;
        return ScaleNpcDamage(baseDamage, mode);
    }

    /// <summary>未收录类型的接触伤害兜底（原版语义：不认识的怪按 7 点处理）。</summary>
    private const int DefaultContactDamage = 7;

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
