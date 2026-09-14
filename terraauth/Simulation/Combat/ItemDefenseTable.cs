// TerraAuth — 服务端权威伤害结算：装备防御表（阶段 D 第二部分 / E 修正）
// 物品 ID → 防御值；数据源：Terraria 1.4.5.8 原版 Item.SetDefaults 的 defense 字段（ID 以 ItemID.cs 为准）。
// 供 PlayerRuntime.RecalculateDefense 在物品栏变更时回填 statDefense，
// 让包 117 区间上界与接触兜底按真实装备减防（原版 Main.CalculateDamagePlayersTake）。
// 注意：1.4.5.8 铜/铁/银/金套为 89/80/76、90/81/77、91/82/78、92/83/79（早期 1.3 的 79-84 已重排）。

namespace TerraAuth.Simulation;

/// <summary>物品防御表：未知物品防御为 0（先覆盖常用基础套，后续按需扩充）。</summary>
public static class ItemDefenseTable
{
    /// <summary>物品 ID → 防御值。仅收「直接提供防御」的装备。</summary>
    public static readonly IReadOnlyDictionary<int, int> Of = new Dictionary<int, int>
    {
        // 木套（727-729）：1 / 1 / 0，合计 2
        [727] = 1,   // Wood Helmet
        [728] = 1,   // Wood Breastplate
        [729] = 0,   // Wood Greaves
        // 铜套（89/80/76）：1 / 2 / 1，合计 4
        [89] = 1,    // Copper Helmet
        [80] = 2,    // Copper Chainmail
        [76] = 1,    // Copper Greaves
        // 锡套（687-689）：2 / 2 / 1，合计 5
        [687] = 2,   // Tin Helmet
        [688] = 2,   // Tin Chainmail
        [689] = 1,   // Tin Greaves
        // 铁套（90/81/77）：2 / 3 / 2，合计 7
        [90] = 2,    // Iron Helmet
        [81] = 3,    // Iron Chainmail
        [77] = 2,    // Iron Greaves
        // 银套（91/82/78）：3 / 4 / 3，合计 10
        [91] = 3,    // Silver Helmet
        [82] = 4,    // Silver Chainmail
        [78] = 3,    // Silver Greaves
        // 金套（92/83/79）：4 / 5 / 4，合计 13
        [92] = 4,    // Gold Helmet
        [83] = 5,    // Gold Chainmail
        [79] = 4,    // Gold Greaves
        // 铂金套（696-698）：5 / 6 / 5，合计 16
        [696] = 5,   // Platinum Helmet
        [697] = 6,   // Platinum Chainmail
        [698] = 5,   // Platinum Greaves
        // 熔岩套（231-233）：8 / 9 / 8，合计 25
        [231] = 8,   // Molten Helmet
        [232] = 9,   // Molten Breastplate
        [233] = 8,   // Molten Greaves
        // 陨石套（123-125）：5 / 6 / 5，合计 16
        [123] = 5,   // Meteor Helmet
        [124] = 6,   // Meteor Suit
        [125] = 5,   // Meteor Leggings
    };

    /// <summary>查询物品防御；未知物品 / 空槽返回 0。</summary>
    public static int DefenseOf(int itemId)
        => itemId > 0 && Of.TryGetValue(itemId, out var d) ? d : 0;
}
