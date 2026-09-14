// TerraAuth — 服务端权威伤害结算：Buff / 药水战斗修饰表
// 阶段 E：117（玩家受击）与 28（玩家攻击 NPC）的服务端校验上界，随玩家 Buff 实时变化。
// Terraria 中药水效果即 Buff（包 50 权威持有），故「药水」天然由 BuffTable 覆盖。
// 数据源：Terraria 1.4.5.8 原版 Player.HasBuff 加成口径（百分比 = 原版数值 × 100）。

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
/// 覆盖早期战斗常用增益；百分比字段 = 原版数值 × 100（如 +10% 记 10）。
/// </summary>
public static class BuffTable
{
    /// <summary>已收录的常见战斗 Buff（ID → 修饰）。</summary>
    public static readonly IReadOnlyDictionary<int, BuffEffects> Of = new Dictionary<int, BuffEffects>
    {
        [14]  = new(8, 0, 0, 0, 0),      // 铁皮 Ironskin：防御 +8
        [25]  = new(0, 0, 10, 0, 0),     // 酒醉 Tipsy：近战伤害 +10%
        [26]  = new(2, 5, 0, 0, 0),      // 吃饱 Well Fed：全伤害 +5%、防御 +2
        [27]  = new(3, 7.5, 0, 0, 0),    // 满足 Plenty Satisfied：全伤害 +7.5%、防御 +3
        [28]  = new(4, 10, 0, 0, 0),     // 精致佳肴 Exquisitely Stuffed：全伤害 +10%、防御 +4
        [73]  = new(0, 0, 0, 20, 0),     // 箭术 Archery：远程伤害 +20%（弓箭）
        [117] = new(0, 10, 0, 0, 0),     // 愤怒 Wrath：全伤害 +10%
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
