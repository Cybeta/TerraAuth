// TerraAuth — 服务端权威伤害结算：Buff / 药水战斗修饰表
// 阶段 E：117（玩家受击）与 28（玩家攻击 NPC）的服务端校验上界，随玩家 Buff 实时变化。
// Terraria 中药水效果即 Buff（包 50 权威持有），故「药水」天然由 BuffTable 覆盖。
// 数据源：Terraria 1.4.5.8 原版 Player.UpdateBuffs 巨型 buffType[j] 链（百分比 = 原版数值 × 100，
// 0.051f → 5.1 保留小数，避免 0.05 截断为 5 之类精度丢失）。
// 语义映射：melee/ranged/magic/minion 四职业修饰完全相等且非零 → 记 AllDamage（原版对四职业 += 同一值），
// 否则按职业分别记入 Melee/Ranged/Magic（minion 不参与近战武器校验，忽略）。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>武器职业：决定取哪一列的伤害修饰（原版 <c>Player.meleeDamage/rangedDamage/magicDamage</c>）。</summary>
public enum WeaponClass
{
    Melee,
    Ranged,
    Magic,
    Summon,
    None,
}

/// <summary>单个 Buff 的战斗修饰：防御加成 + 各类别伤害百分比（%）。</summary>
public readonly record struct BuffEffects(
    int Defense,
    double AllDamage,
    double Melee,
    double Ranged,
    double Magic)
{
    /// <summary>按武器职业取对应类别伤害加成（百分比）。</summary>
    public double ClassDamage(WeaponClass weaponClass)
        => weaponClass switch
        {
            WeaponClass.Melee => Melee,
            WeaponClass.Ranged => Ranged,
            WeaponClass.Magic => Magic,
            WeaponClass.Summon => 0,
            _ => 0,
        };
}

/// <summary>
/// Buff ID → 战斗修饰（未收录的 Buff 按无加成处理，不影响已有行为）。
/// 覆盖原版 1.4.5.8 全部带伤害/防御修饰的 Buff；百分比字段 = 原版数值 × 100（如 +10% 记 10）。
/// 动态层数 Buff（甲虫 Might 1-3、星云伤害 1-3）按各自档位收录（互斥升级，不会叠加双计）。
/// 箭术（16）仅乘箭伤（arrowDamage）字段、不进入职业伤害字段 → 不收录（远程武器本就失败放行）。
/// </summary>
public static class BuffTable
{
    /// <summary>已收录的带战斗修饰 Buff（ID → 修饰）。</summary>
    public static readonly IReadOnlyDictionary<int, BuffEffects> Of = new Dictionary<int, BuffEffects>
    {
        [5]   = new(8, 0, 0, 0, 0),       // 铁皮 Ironskin：防御 +8
        [7]   = new(0, 0, 0, 0, 20),      // 魔力药水 Magic Power：魔法伤害 +20%
        [25]  = new(-4, 0, 10, 10, 0),    // 酒醉 Tipsy：防御 -4、近战 +10%（远程 +10% 仅手持 3821，按上界收录）
        [26]  = new(2, 5, 0, 0, 0),       // 吃饱 Well Fed：全伤害 +5%、防御 +2
        [28]  = new(3, 0, 5.1, 0, 0),     // 狼人 Werewolf：防御 +3、近战 +5.1%（夜间+狼人饰品，按上界收录）
        [29]  = new(0, 0, 0, 0, 5),       // 天眼药水 Clairvoyance：魔法伤害 +5%
        [33]  = new(-4, 0, -5.1, 0, 0),   // 虚弱 Weak：防御 -4、近战 -5.1%
        [69]  = new(-15, 0, 0, 0, 0),     // 灵液 Ichor：防御 -15
        [98]  = new(0, 0, 10, 0, 0),      // 甲虫力量 Beetle Might 1：近战 +10%（1 层）
        [99]  = new(0, 0, 20, 0, 0),      // 甲虫力量 Beetle Might 2：近战 +20%（2 层）
        [100] = new(0, 0, 30, 0, 0),      // 甲虫力量 Beetle Might 3：近战 +30%（3 层）
        [117] = new(0, 10, 0, 0, 0),      // 愤怒 Wrath：全伤害 +10%
        [148] = new(0, 20, 0, 0, 0),      // 狂犬病 Rabies：全伤害 +20%
        [165] = new(8, 0, 0, 0, 0),       // 树妖守卫 Dryad's Ward：防御 +8
        [179] = new(0, 15, 0, 0, 0),      // 星云伤害增幅 1 Nebula Up Dmg 1：全伤害 +15%
        [180] = new(0, 30, 0, 0, 0),      // 星云伤害增幅 2 Nebula Up Dmg 2：全伤害 +30%
        [181] = new(0, 45, 0, 0, 0),      // 星云伤害增幅 3 Nebula Up Dmg 3：全伤害 +45%
        [206] = new(3, 7.5, 0, 0, 0),     // 满足 Plenty Satisfied：全伤害 +7.5%、防御 +3
        [207] = new(4, 10, 0, 0, 0),      // 精致佳肴 Exquisitely Stuffed：全伤害 +10%、防御 +4
        [215] = new(5, 0, 0, 0, 0),       // 猫神像 Cat Bast：防御 +5
        [333] = new(-2, -5, 0, 0, 0),     // 饥饿 Hunger：防御 -2、全伤害 -5%
        [334] = new(-4, -10, 0, 0, 0),    // 极度饥饿 Starving：防御 -4、全伤害 -10%
    };

    /// <summary>Buff 列表合计防御加成（原版 <c>Player.statDefense</c> 的 buff 部分）。</summary>
    public static int DefenseOf(IReadOnlyList<int> buffs)
    {
        int sum = 0;
        foreach (var id in buffs)
            if (Of.TryGetValue(id, out var fx))
                sum += fx.Defense;
        return sum;
    }

    /// <summary>Buff 列表合计「全伤害」加成（百分比）。</summary>
    public static double AllDamagePercent(IReadOnlyList<int> buffs)
    {
        double sum = 0;
        foreach (var id in buffs)
            if (Of.TryGetValue(id, out var fx))
                sum += fx.AllDamage;
        return sum;
    }

    /// <summary>Buff 列表合计指定武器职业的伤害加成（百分比）。</summary>
    public static double ClassDamagePercent(IReadOnlyList<int> buffs, WeaponClass weaponClass)
    {
        double sum = 0;
        foreach (var id in buffs)
            if (Of.TryGetValue(id, out var fx))
                sum += fx.ClassDamage(weaponClass);
        return sum;
    }
}
