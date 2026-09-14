// TerraAuth — 服务端权威伤害结算：武器基础伤害表（阶段 E / 修正）
// 阶段 E：近战（无弹幕）命中按**手持武器权威伤害**校验包 28 上报值。
// 数据源：Terraria 1.4.5.8 原版 Item.SetDefaults 的 damage 字段 + ItemID.cs 权威 ID。
// 未收录的物品（含空手 / 未知武器 / 弹药 / 工具）→ 失败放行（退回既有上限校验），绝不误拒。
// 注意：1.4.5.8 木剑=24、木弓=39、木锤=196（早期 1.3 的 25/26/7 已重排）；铜/锡/银/金系列武器在 3500+。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>武器条目：基础伤害 + 职业（决定 buff/套装修饰列）。</summary>
public readonly record struct WeaponStats(int Damage, WeaponClass Class);

/// <summary>
/// 物品 ID → 武器基础伤害（原版 <c>Item.damage</c>，不含任何修饰）。
/// 覆盖肉山前常见武器；待扩展：随实测覆盖面逐步补充。
/// </summary>
public static class ItemDamageTable
{
    public static readonly IReadOnlyDictionary<int, WeaponStats> Of = new Dictionary<int, WeaponStats>
    {
        // ---- 近战 ----
        [24]  = new(7,  WeaponClass.Melee),   // 木剑 Wooden Sword
        [196] = new(2,  WeaponClass.Melee),   // 木锤 Wooden Hammer
        [284] = new(10, WeaponClass.Melee),   // 木回旋镖 Wooden Boomerang
        [55]  = new(17, WeaponClass.Melee),   // 附魔回旋镖 Enchanted Boomerang
        [4]   = new(12, WeaponClass.Melee),   // 铁阔剑 Iron Broadsword
        [6]   = new(8,  WeaponClass.Melee),   // 铁短剑 Iron Shortsword
        [7]   = new(7,  WeaponClass.Melee),   // 铁锤 Iron Hammer
        [3501] = new(7,  WeaponClass.Melee),  // 锡短剑 Tin Shortsword
        [3502] = new(10, WeaponClass.Melee),  // 锡阔剑 Tin Broadsword
        [3507] = new(5,  WeaponClass.Melee),  // 铜短剑 Copper Shortsword
        [3508] = new(9,  WeaponClass.Melee),  // 铜阔剑 Copper Broadsword
        [3513] = new(9,  WeaponClass.Melee),  // 银短剑 Silver Shortsword
        [3514] = new(14, WeaponClass.Melee),  // 银阔剑 Silver Broadsword
        [3519] = new(12, WeaponClass.Melee),  // 金短剑 Gold Shortsword
        [3520] = new(15, WeaponClass.Melee),  // 金阔剑 Gold Broadsword
        // ---- 远程 ----
        [39]  = new(4,  WeaponClass.Ranged),  // 木弓 Wooden Bow
        [99]  = new(8,  WeaponClass.Ranged),  // 铁弓 Iron Bow
        [3504] = new(6,  WeaponClass.Ranged), // 铜弓 Copper Bow
        [3510] = new(9,  WeaponClass.Ranged), // 银弓 Silver Bow
        [3516] = new(11, WeaponClass.Ranged), // 金弓 Gold Bow
    };
}
