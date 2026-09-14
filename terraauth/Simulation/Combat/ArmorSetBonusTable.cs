// TerraAuth — 服务端权威伤害结算：套装加成表（阶段 E-2）
// 数据源：Terraria 1.4.5.8 原版 ArmorSetBonuses.cs（套装 ID 组合与 Benefits）：
//   - Molten 熔岩套（231/232/233）：meleeDamage +10%
//   - MetalTier1（铜 89/80/76、锡 687/688/689、铁 90/81/77）：statDefense +2
//   - MetalTier2（银 91/82/78、金 92/83/79）：statDefense +3
//   - Platinum（696/697/698）：statDefense +4
//   - Wood（727/728/729）：statDefense +1
// 套装判定：装备区（0-8）同时持有该套装 3 件物品 → 应用加成（原版 ArmorSetBonus.PartType.Head/Body/Legs 全齐）。
// 未收录套装（逐件伤害加成尚未建模，如秘银/钴蓝护甲单件属性）→ 无加成，不误拒。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>套装加成：各类别伤害百分比（%）+ 额外防御。</summary>
public readonly record struct ArmorSetBonus(
    int MeleePct = 0,
    int RangedPct = 0,
    int MagicPct = 0,
    int DefenseBonus = 0);

/// <summary>套装加成表：装备区穿齐三件即生效。</summary>
public static class ArmorSetBonusTable
{
    /// <summary>(套装三件物品 ID, 加成)。未收录 → 不参与判定。</summary>
    private static readonly (int[] Set, ArmorSetBonus Bonus)[] Sets =
    {
        (new[] { 231, 232, 233 }, new ArmorSetBonus(MeleePct: 10)),        // 熔岩套 Molten：近战 +10%
        (new[] { 89, 80, 76 },    new ArmorSetBonus(DefenseBonus: 2)),     // 铜套（MetalTier1）
        (new[] { 687, 688, 689 }, new ArmorSetBonus(DefenseBonus: 2)),     // 锡套（MetalTier1）
        (new[] { 90, 81, 77 },    new ArmorSetBonus(DefenseBonus: 2)),     // 铁套（MetalTier1）
        (new[] { 91, 82, 78 },    new ArmorSetBonus(DefenseBonus: 3)),     // 银套（MetalTier2）
        (new[] { 92, 83, 79 },    new ArmorSetBonus(DefenseBonus: 3)),     // 金套（MetalTier2）
        (new[] { 696, 697, 698 }, new ArmorSetBonus(DefenseBonus: 4)),     // 铂金套 Platinum
        (new[] { 727, 728, 729 }, new ArmorSetBonus(DefenseBonus: 1)),     // 木套 Wood
    };

    /// <summary>
    /// 玩家穿齐某套装三件 → 返回其加成；否则 null。
    /// 只扫描装备区（0..8，原版头/胸/腿 + 饰品槽），不扫描整个物品栏。
    /// </summary>
    public static ArmorSetBonus? BonusForEquipment(IReadOnlyList<int> items)
    {
        foreach (var (set, bonus) in Sets)
        {
            bool complete = true;
            foreach (var part in set)
            {
                if (!IsEquipped(items, part))
                {
                    complete = false;
                    break;
                }
            }
            if (complete)
                return bonus;
        }
        return null;
    }

    private static bool IsEquipped(IReadOnlyList<int> items, int itemId)
    {
        for (int i = PlayerRuntime.EquipmentSlotStart; i <= PlayerRuntime.EquipmentSlotEnd && i < items.Count; i++)
        {
            if (items[i] == itemId)
                return true;
        }
        return false;
    }
}
