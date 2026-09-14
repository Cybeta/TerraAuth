// TerraAuth — 服务端权威伤害结算：套装加成表（阶段 E-2，数据表补齐）
// 数据源：Terraria 1.4.5.8 原版 ArmorSetBonuses.Initialize() 全量套装注册（Head/Body/Legs 物品组合 + Benefits 效果）：
//   - 防御套：Wood(+1)、MetalTier1 铜/锡/铁(+2)、MetalTier2 银/金/铂金变体(+3)、Platinum(+4)
//   - 伤害套：Molten 熔岩(近战+10%)、Frost 霜冻(近战/远程+10%)、Pumpkin 南瓜(全+10%)、CrystalAssassin(全+10%)
// 未建模效果（暴击/攻速/减伤/召唤/生命回复等，或动态层数如 Beetle）→ 不参与伤害/防御上界，不误拒。
// 套装判定：装备区（0-8）同时持有该套装 3 件物品 → 应用加成（原版 ArmorSetBonus.QueryCount 全齐判定）。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>套装加成：全伤害 + 各类别伤害百分比（%）+ 额外防御。</summary>
public readonly record struct ArmorSetBonus(
    int AllDamage = 0,
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
        (new[] { 727, 728, 729 }, new ArmorSetBonus(DefenseBonus: 1)),   // 木套 Wood
        (new[] { 733, 734, 735 }, new ArmorSetBonus(DefenseBonus: 1)),   // 红木套 Rich Mahogany（Wood）
        (new[] { 730, 731, 732 }, new ArmorSetBonus(DefenseBonus: 1)),   // 乌木套 Ebonwood（Wood）
        (new[] { 736, 737, 738 }, new ArmorSetBonus(DefenseBonus: 1)),   // 珍珠木套 Pearlwood（Wood）
        (new[] { 924, 925, 926 }, new ArmorSetBonus(DefenseBonus: 1)),   // 棕榈木套 Palm Wood（Wood）
        (new[] { 2509, 2510, 2511 }, new ArmorSetBonus(DefenseBonus: 1)),// 针叶木套 Boreal Wood（Wood）
        (new[] { 2512, 2513, 2514 }, new ArmorSetBonus(DefenseBonus: 1)),// 灰木套 Ash Wood 变体（Wood）
        (new[] { 89, 80, 76 },    new ArmorSetBonus(DefenseBonus: 2)),   // 铜套（MetalTier1）
        (new[] { 687, 688, 689 }, new ArmorSetBonus(DefenseBonus: 2)),   // 锡套（MetalTier1）
        (new[] { 90, 81, 77 },    new ArmorSetBonus(DefenseBonus: 2)),   // 铁套（MetalTier1）
        (new[] { 954, 81, 77 },   new ArmorSetBonus(DefenseBonus: 2)),   // 铁套远古头盔变体（MetalTier1）
        (new[] { 91, 82, 78 },    new ArmorSetBonus(DefenseBonus: 3)),   // 银套（MetalTier2）
        (new[] { 92, 83, 79 },    new ArmorSetBonus(DefenseBonus: 3)),   // 金套（MetalTier2）
        (new[] { 955, 83, 79 },   new ArmorSetBonus(DefenseBonus: 3)),   // 金套远古头盔变体（MetalTier2）
        (new[] { 690, 691, 692 }, new ArmorSetBonus(DefenseBonus: 3)),   // 铅套 Lead（MetalTier2）
        (new[] { 693, 694, 695 }, new ArmorSetBonus(DefenseBonus: 3)),   // 钨套 Tungsten（MetalTier2）
        (new[] { 696, 697, 698 }, new ArmorSetBonus(DefenseBonus: 4)),   // 铂金套 Platinum
        (new[] { 231, 232, 233 }, new ArmorSetBonus(MeleePct: 10)),       // 熔岩套 Molten：近战 +10%
        (new[] { 684, 685, 686 }, new ArmorSetBonus(MeleePct: 10, RangedPct: 10)), // 霜冻套 Frost
        (new[] { 1731, 1732, 1733 }, new ArmorSetBonus(AllDamage: 10)),  // 南瓜套 Pumpkin：全伤害 +10%
        (new[] { 4982, 4983, 4984 }, new ArmorSetBonus(AllDamage: 10)),  // 水晶刺客套 Crystal Assassin：全伤害 +10%
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
