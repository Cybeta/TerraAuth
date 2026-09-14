// TerraAuth — 服务端权威伤害结算：饰品伤害修饰表（阶段 E-3）
// 数据源：Terraria 1.4.5.8 原版 Player.UpdateEquips 的 currentItem.type 判定（ID 以 ItemID.cs 为准）。
// 原版把各类伤害加成**加算**累进 meleeDamage/rangedDamage/magicDamage 字段——无独立 allDamage 字段，
// 「全伤害」（Wrath、复仇者/毁灭者徽章、天界石）是对四个职业字段 `+=` 同一数值，
// 故 GetWeaponDamage 用加算总百分比（与 BuffTable/ArmorSetBonusTable 同一口径）。
// 只扫饰品槽（3-8）；未收录 → 无加成，绝不误拒。天界石另 +4 防御暂未建模（低估防御为安全方向）。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>单个饰品的伤害修饰：全伤害 + 各类别伤害百分比（%）。</summary>
public readonly record struct AccessoryEffects(
    double AllDamage,
    double Melee,
    double Ranged,
    double Magic);

/// <summary>饰品 ID → 伤害修饰（百分比 = 原版数值 × 100）。仅收「直接给伤害加成」的常见饰品。</summary>
public static class AccessoryTable
{
    /// <summary>饰品 ID → 修饰；未收录 → 无加成。</summary>
    public static readonly IReadOnlyDictionary<int, AccessoryEffects> Of = new Dictionary<int, AccessoryEffects>
    {
        [490]  = new(0, 15, 0, 0),    // 战士徽章 Warrior Emblem：近战 +15%（Player.cs L16370）
        [491]  = new(0, 0, 15, 0),    // 游侠徽章 Ranger Emblem：远程 +15%（L16374）
        [489]  = new(0, 0, 0, 15),    // 巫师徽章 Sorcerer Emblem：魔法 +15%（L16366）
        [935]  = new(12, 0, 0, 0),    // 复仇者徽章 Avenger Emblem：全伤害 +12%（L16382）
        [1301] = new(10, 0, 0, 0),    // 毁灭者徽章 Destroyer Emblem：全伤害 +10%（L15842）
        [936]  = new(0, 12, 0, 0),    // 机械手套 Mechanical Glove：近战 +12%（L16307）
        [1343] = new(0, 12, 0, 0),    // 火焰手套 Fire Gauntlet：近战 +12%（L16244）
        [1865] = new(10, 0, 0, 0),    // 天界石 Celestial Stone：全伤害 +10%（skyStoneEffects，L13831）
    };

    /// <summary>饰品槽合计「全伤害」加成（百分比）。</summary>
    public static double AllDamagePercent(IReadOnlyList<int> equipment)
    {
        double sum = 0;
        for (int i = PlayerRuntime.AccessorySlotStart; i <= PlayerRuntime.EquipmentSlotEnd && i < equipment.Count; i++)
            if (Of.TryGetValue(equipment[i], out var fx))
                sum += fx.AllDamage;
        return sum;
    }

    /// <summary>饰品槽合计指定武器职业的伤害加成（百分比）。</summary>
    public static double ClassDamagePercent(IReadOnlyList<int> equipment, WeaponClass weaponClass)
    {
        double sum = 0;
        for (int i = PlayerRuntime.AccessorySlotStart; i <= PlayerRuntime.EquipmentSlotEnd && i < equipment.Count; i++)
        {
            if (!Of.TryGetValue(equipment[i], out var fx))
                continue;
            sum += weaponClass switch
            {
                WeaponClass.Melee => fx.Melee,
                WeaponClass.Ranged => fx.Ranged,
                WeaponClass.Magic => fx.Magic,
                _ => 0,
            };
        }
        return sum;
    }
}
