// TerraAuth — 服务端权威伤害结算：护甲单件伤害修饰表（阶段 E-5，数据表补齐）
// 数据源：Terraria 1.4.5.8 原版 Player.GrantArmorBenefits 的 armorPiece.type 判定
// （switch + if 链，ID 以 ItemID.cs 为准）。原版护甲单件加成与套装加成**各自独立**、
// 加算累进职业伤害字段（如熔岩胸甲近战 +7% 与熔岩套近战 +10% 并存 → 合计 +17%）。
// 只扫护甲槽（0-2：头/胸/腿）；未收录 → 无加成，绝不误拒。
// 纯召唤伤害单件（甲虫/蜜蜂/星尘/死灵等 minion 系）不参与近战武器校验，未收录。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>单个护甲单件的职业伤害修饰（%）。</summary>
public readonly record struct ArmorPieceEffects(
    double Melee,
    double Ranged,
    double Magic);

/// <summary>护甲单件 ID → 职业伤害修饰（百分比 = 原版数值 × 100）。</summary>
public static class ArmorPieceBonusTable
{
    /// <summary>护甲单件 ID → 修饰；未收录 → 无加成。</summary>
    public static readonly IReadOnlyDictionary<int, ArmorPieceEffects> Of = new Dictionary<int, ArmorPieceEffects>
    {
        [123]  = new(0, 0, 9),     // 陨星盔 Meteor Helmet
        [124]  = new(0, 0, 9),     // 陨星服 Meteor Suit
        [125]  = new(0, 0, 9),     // 陨星裤 Meteor Leggings
        [151]  = new(0, 5, 0),     // 死灵头盔 Necro Helmet
        [152]  = new(0, 5, 0),     // 死灵胸甲 Necro Breastplate
        [153]  = new(0, 5, 0),     // 死灵护胫 Necro Greaves
        [229]  = new(0, 0, 6),     // 丛林上衣 Jungle Shirt
        [232]  = new(7, 0, 0),     // 熔岩胸甲 Molten Breastplate
        [238]  = new(0, 0, 5),     // 巫师帽 Wizard Hat
        [371]  = new(0, 0, 10),    // 钴蓝帽 Cobalt Hat
        [372]  = new(15, 0, 0),    // 钴蓝头盔 Cobalt Helmet
        [373]  = new(0, 10, 0),    // 钴蓝面具 Cobalt Mask
        [375]  = new(3, 3, 3),     // 钴蓝护腿 Cobalt Leggings
        [376]  = new(0, 0, 15),    // 秘银兜帽 Mythril Hood
        [377]  = new(10, 0, 0),    // 秘银头盔 Mythril Helmet
        [378]  = new(0, 12, 0),    // 秘银帽 Mythril Hat
        [379]  = new(7, 7, 7),     // 秘银锁甲 Mythril Chainmail
        [400]  = new(0, 0, 12),    // 精金头饰 Adamantite Headgear
        [401]  = new(14, 0, 0),    // 精金头盔 Adamantite Helmet
        [402]  = new(0, 14, 0),    // 精金面具 Adamantite Mask
        [403]  = new(8, 8, 8),     // 精金胸甲 Adamantite Breastplate
        [552]  = new(7, 7, 7),     // 神圣护胫 Hallowed Greaves
        [553]  = new(0, 15, 0),    // 神圣头盔 Hallowed Helmet
        [558]  = new(0, 0, 12),    // 神圣头饰 Hallowed Headgear
        [559]  = new(10, 0, 0),    // 神圣面具 Hallowed Mask
        [684]  = new(16, 16, 0),   // 霜冻头盔 Frost Helmet
        [792]  = new(3, 3, 3),     // 猩红头盔 Crimson Helmet
        [793]  = new(3, 3, 3),     // 猩红鳞甲 Crimson Scalemail
        [794]  = new(3, 3, 3),     // 猩红护胫 Crimson Greaves
        [959]  = new(0, 5, 0),     // 远古死灵头盔 Ancient Necro Helmet
        [961]  = new(0, 0, 6),     // 远古钴蓝胸甲 Ancient Cobalt Breastplate
        [1001] = new(16, 0, 0),    // 叶绿面具 Chlorophyte Mask
        [1002] = new(0, 16, 0),    // 叶绿头盔 Chlorophyte Helmet
        [1003] = new(0, 0, 16),    // 叶绿头饰 Chlorophyte Headgear
        [1004] = new(5, 5, 5),     // 叶绿板甲 Chlorophyte Plate Mail
        [1205] = new(12, 0, 0),    // 钯金面具 Palladium Mask
        [1206] = new(0, 9, 0),     // 钯金头盔 Palladium Helmet
        [1207] = new(0, 0, 9),     // 钯金头饰 Palladium Headgear
        [1208] = new(3, 3, 3),     // 钯金胸甲 Palladium Breastplate
        [1209] = new(2, 2, 2),     // 钯金护腿 Palladium Leggings
        [1210] = new(11, 0, 0),    // 山铜面具 Orichalcum Mask
        [1214] = new(8, 8, 8),     // 山铜护腿 Orichalcum Leggings
        [1215] = new(9, 0, 0),     // 钛金面具 Titanium Mask
        [1216] = new(0, 16, 0),    // 钛金头盔 Titanium Helmet
        [1217] = new(0, 0, 16),    // 钛金头饰 Titanium Headgear
        [1218] = new(4, 4, 4),     // 钛金胸甲 Titanium Breastplate
        [1219] = new(3, 3, 3),     // 钛金护腿 Titanium Leggings
        [1316] = new(6, 0, 0),     // 龟甲头盔 Turtle Helmet
        [1317] = new(8, 0, 0),     // 龟甲鳞甲 Turtle Scale Mail
        [1504] = new(0, 0, 7),     // 幽灵长袍 Spectre Robe
        [1505] = new(0, 0, 8),     // 幽灵裤 Spectre Pants
        [1549] = new(0, 13, 0),    // 蘑菇矿胸甲 Shroomite Breastplate
        [2189] = new(0, 0, 10),    // 幽灵面具 Spectre Mask
        [2199] = new(6, 0, 0),     // 甲虫头盔 Beetle Helmet
        [2200] = new(8, 0, 0),     // 甲虫鳞甲 Beetle Scale Mail
        [2201] = new(5, 0, 0),     // 甲虫壳 Beetle Shell
        [2275] = new(0, 0, 6),     // 魔法帽 Magic Hat
        [2277] = new(5, 5, 5),     // 武道服 Gi
        [2279] = new(0, 0, 6),     // 吉普赛长袍 Gypsy Robe
        [2757] = new(0, 16, 0),    // 星旋头盔 Vortex Helmet
        [2758] = new(0, 12, 0),    // 星旋胸甲 Vortex Breastplate
        [2759] = new(0, 8, 0),     // 星旋护腿 Vortex Leggings
        [2760] = new(0, 0, 7),     // 星云头盔 Nebula Helmet
        [2761] = new(0, 0, 9),     // 星云胸甲 Nebula Breastplate
        [2762] = new(0, 0, 10),    // 星云护腿 Nebula Leggings
        [2764] = new(29, 0, 0),    // 耀斑胸甲 Solar Flare Breastplate
        [3375] = new(0, 5, 0),     // 化石衫 Fossil Shirt
        [3776] = new(0, 0, 15),    // 远古征战盔帽 Ancient Battle Armor Hat
        [3778] = new(0, 0, 10),    // 远古征战盔裤 Ancient Battle Armor Pants
        [3797] = new(0, 0, 10),    // 学徒帽 Apprentice Hat
        [3798] = new(0, 0, 10),    // 学徒长袍 Apprentice Robe
        [3801] = new(15, 0, 0),    // 侍从板甲 Squire Plating
        [3804] = new(0, 20, 0),    // 女猎人紧身上衣 Huntress Jerkin
        [3807] = new(20, 0, 0),    // 武僧衬衫 Monk Shirt
        [3871] = new(10, 0, 0),    // 侍从头盔（变体）Squire Alt Head
        [3874] = new(0, 0, 15),    // 学徒帽（变体）Apprentice Alt Head
        [3875] = new(0, 0, 10),    // 学徒长袍（变体）Apprentice Alt Shirt
        [3878] = new(0, 25, 0),    // 女猎人紧身上衣（变体）Huntress Alt Shirt
        [3880] = new(20, 0, 0),    // 武僧头盔（变体）Monk Alt Head
        [4896] = new(10, 0, 0),    // 远古神圣面具 Ancient Hallowed Mask
        [4897] = new(0, 15, 0),    // 远古神圣头盔 Ancient Hallowed Helmet
        [4898] = new(0, 0, 12),    // 远古神圣头饰 Ancient Hallowed Headgear
        [4901] = new(7, 7, 7),     // 远古神圣护胫 Ancient Hallowed Greaves
        [4983] = new(5, 5, 5),     // 水晶忍者胸甲 Crystal Ninja Chestplate
    };

    /// <summary>护甲槽（0-2）合计指定武器职业的伤害加成（百分比）。</summary>
    public static double ClassDamagePercent(IReadOnlyList<int> items, WeaponClass weaponClass)
    {
        double sum = 0;
        for (int i = PlayerRuntime.EquipmentSlotStart; i <= PlayerRuntime.ArmorSlotEnd && i < items.Count; i++)
        {
            if (!Of.TryGetValue(items[i], out var fx))
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
