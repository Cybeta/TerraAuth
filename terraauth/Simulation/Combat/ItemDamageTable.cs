// TerraAuth — 服务端权威伤害结算：武器基础伤害表
// 阶段 E：近战（无弹幕）命中按**手持武器权威伤害**校验包 28 上报值。
// 未收录的物品（含空手 / 未知武器）→ 失败放行（退回既有上限校验），绝不误拒。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>武器条目：基础伤害 + 职业（决定 buff 修饰列）。</summary>
public readonly record struct WeaponStats(int Damage, WeaponClass Class);

/// <summary>
/// 物品 ID → 武器基础伤害（原版 <c>Item.damage</c>，不含任何修饰）。
/// 覆盖服务端实测 / 测试用到的基础武器；数值来源 Terraria 1.4.5.8 原版 Item.SetDefaults。
/// 待扩展：随实测覆盖面逐步补充（见 plan 阶段 E「数据覆盖」）。
/// </summary>
public static class ItemDamageTable
{
    public static readonly IReadOnlyDictionary<int, WeaponStats> Of = new Dictionary<int, WeaponStats>
    {
        [1]  = new(5, WeaponClass.Melee),    // 铜短剑 Copper Shortsword
        [25] = new(7, WeaponClass.Melee),    // 木剑 Wooden Sword
        [26] = new(9, WeaponClass.Ranged),   // 木弓 Wooden Bow
    };
}
