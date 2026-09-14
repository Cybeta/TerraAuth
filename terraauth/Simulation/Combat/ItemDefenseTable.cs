// TerraAuth — 服务端权威伤害结算：装备防御表（阶段 D 第二部分）
// 物品 ID → 防御值；数据源：原版 Item.cs SetDefaults（此处先覆盖常用基础套）
// 供 PlayerRuntime.RecalculateDefense 在物品栏变更时回填 statDefense，
// 让包 117 区间上界与接触兜底按真实装备减防（原版 Main.CalculateDamagePlayersTake）。

namespace TerraAuth.Simulation;

/// <summary>物品防御表：未知物品防御为 0（先覆盖基础套，后续按需扩充）。</summary>
public static class ItemDefenseTable
{
    /// <summary>物品 ID → 防御值。仅收「直接提供防御」的装备。</summary>
    public static readonly IReadOnlyDictionary<int, int> Of = new Dictionary<int, int>
    {
        // 铜套（79-81）：1 / 3 / 2，合计 6
        [79] = 1,   // Copper Helmet
        [80] = 3,   // Copper Chainmail
        [81] = 2,   // Copper Greaves
        // 铁套（82-84）：2 / 5 / 3，合计 10
        [82] = 2,   // Iron Helmet
        [83] = 5,   // Iron Chainmail
        [84] = 3,   // Iron Greaves
        // 木套（95-97）：1 / 1 / 1，合计 3
        [95] = 1,   // Wood Helmet
        [96] = 1,   // Wood Breastplate
        [97] = 1,   // Wood Greaves
    };

    /// <summary>查询物品防御；未知物品 / 空槽返回 0。</summary>
    public static int DefenseOf(int itemId)
        => itemId > 0 && Of.TryGetValue(itemId, out var d) ? d : 0;
}
