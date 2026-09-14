// TerraAuth — 服务端权威伤害结算：装备防御表（阶段 D 第二部分 / E 全量补齐）
// 物品 ID → 防御值；数据源：Terraria 1.4.5.8 原版 Item.SetDefaults1-5 的 defense 字段（ID 以 ItemID.cs 为准）。
// 供 PlayerRuntime.RecalculateDefense 在物品栏变更时回填 statDefense，
// 让包 117 区间上界与接触兜底按真实装备减防（原版 Main.CalculateDamagePlayersTake）。
// 注意：1.4.5.8 铜/铁/银/金套为 89/80/76、90/81/77、91/82/78、92/83/79（早期 1.3 的 79-84 已重排）；
//       叶绿套 = 1001-1005（Mask/Helmet/Headgear/PlateMail/Greaves），钛金 = 1215-1219，幽灵 = 1503-1505 + 2189 Mask。

namespace TerraAuth.Simulation;

/// <summary>物品防御表：未知物品防御为 0（覆盖原版全部直接提供防御的装备与常见饰品）。</summary>
public static class ItemDefenseTable
{
    /// <summary>物品 ID → 防御值。仅收「直接提供防御」的装备。</summary>
    public static readonly IReadOnlyDictionary<int, int> Of = new Dictionary<int, int>
    {
        // ---- 木质基础套 ----
        [727] = 1,    // Wood Helmet
        [728] = 1,    // Wood Breastplate
        // ---- 矿石套（铜 / 锡 / 铁 / 铅 / 银 / 钨 / 金 / 铂金）----
        [89] = 1,     // Copper Helmet
        [80] = 2,     // Copper Chainmail
        [76] = 1,     // Copper Greaves
        [687] = 2,    // Tin Helmet
        [688] = 2,    // Tin Chainmail
        [689] = 1,    // Tin Greaves
        [90] = 2,     // Iron Helmet
        [81] = 3,     // Iron Chainmail
        [77] = 2,     // Iron Greaves
        [690] = 3,    // Lead Helmet
        [691] = 3,    // Lead Chainmail
        [692] = 2,    // Lead Greaves
        [91] = 3,     // Silver Helmet
        [82] = 4,     // Silver Chainmail
        [78] = 3,     // Silver Greaves
        [693] = 4,    // Tungsten Helmet
        [694] = 5,    // Tungsten Chainmail
        [695] = 3,    // Tungsten Greaves
        [92] = 4,     // Gold Helmet
        [83] = 5,     // Gold Chainmail
        [79] = 4,     // Gold Greaves
        [696] = 5,    // Platinum Helmet
        [697] = 6,    // Platinum Chainmail
        [698] = 5,    // Platinum Greaves
        // ---- 木材 / 特殊基础套 ----
        [730] = 1,    // Ebonwood Helmet
        [731] = 2,    // Ebonwood Breastplate
        [732] = 1,    // Ebonwood Greaves
        [733] = 1,    // Rich Mahogany Helmet
        [734] = 1,    // Rich Mahogany Breastplate
        [735] = 1,    // Rich Mahogany Greaves
        [736] = 2,    // Pearlwood Helmet
        [737] = 3,    // Pearlwood Breastplate
        [738] = 2,    // Pearlwood Greaves
        [924] = 1,    // Shadewood Helmet
        [925] = 2,    // Shadewood Breastplate
        [926] = 1,    // Shadewood Greaves
        [5279] = 2,   // Ash Wood Helmet
        [5280] = 3,   // Ash Wood Breastplate
        [5281] = 2,   // Ash Wood Greaves
        [894] = 1,    // Cactus Helmet
        [895] = 1,    // Cactus Breastplate
        [896] = 1,    // Cactus Leggings
        [803] = 3,    // Eskimo Hood
        [804] = 3,    // Eskimo Coat
        [805] = 3,    // Eskimo Pants
        [978] = 3,    // Pink Eskimo Hood
        [979] = 3,    // Pink Eskimo Coat
        [980] = 3,    // Pink Eskimo Pants
        // ---- 肉山前战斗套 ----
        [228] = 5,    // Jungle Hat
        [229] = 6,    // Jungle Shirt
        [230] = 6,    // Jungle Pants
        [100] = 6,    // Shadow Helmet
        [101] = 7,    // Shadow Scalemail
        [102] = 6,    // Shadow Greaves
        [792] = 6,    // Crimson Helmet
        [793] = 7,    // Crimson Scalemail
        [794] = 6,    // Crimson Greaves
        [151] = 6,    // Necro Helmet
        [152] = 7,    // Necro Breastplate
        [153] = 6,    // Necro Greaves
        [256] = 2,    // Ninja Hood
        [257] = 4,    // Ninja Shirt
        [258] = 3,    // Ninja Pants
        [123] = 5,    // Meteor Helmet
        [124] = 6,    // Meteor Suit
        [125] = 5,    // Meteor Leggings
        [231] = 8,    // Molten Helmet
        [232] = 9,    // Molten Breastplate
        [233] = 8,    // Molten Greaves
        [3187] = 5,   // Gladiator Helmet
        [3188] = 6,   // Gladiator Breastplate
        [3189] = 5,   // Gladiator Leggings
        [3266] = 4,   // Obsidian Helm
        [3267] = 6,   // Obsidian Shirt
        [3268] = 5,   // Obsidian Pants
        [3374] = 4,   // Fossil Helm
        [3375] = 5,   // Fossil Shirt
        [3376] = 4,   // Fossil Pants
        [1731] = 2,   // Pumpkin Helmet
        [1732] = 3,   // Pumpkin Breastplate
        [1733] = 2,   // Pumpkin Leggings
        // ---- 肉山后矿石套（钴蓝 / 秘银 / 钯金 / 山铜 / 精金 / 钛金）----
        [371] = 3,    // Cobalt Hat
        [372] = 14,   // Cobalt Helmet
        [373] = 5,    // Cobalt Mask
        [374] = 10,   // Cobalt Breastplate
        [375] = 8,    // Cobalt Leggings
        [376] = 3,    // Mythril Hood
        [377] = 16,   // Mythril Helmet
        [378] = 6,    // Mythril Hat
        [379] = 12,   // Mythril Chainmail
        [380] = 9,    // Mythril Greaves
        [1205] = 14,  // Palladium Mask
        [1206] = 5,   // Palladium Helmet
        [1207] = 3,   // Palladium Headgear
        [1208] = 10,  // Palladium Breastplate
        [1209] = 8,   // Palladium Leggings
        [1210] = 19,  // Orichalcum Mask
        [1211] = 7,   // Orichalcum Helmet
        [1212] = 4,   // Orichalcum Headgear
        [1213] = 13,  // Orichalcum Breastplate
        [1214] = 10,  // Orichalcum Leggings
        [400] = 4,    // Adamantite Headgear
        [401] = 22,   // Adamantite Helmet
        [402] = 8,    // Adamantite Mask
        [403] = 16,   // Adamantite Breastplate
        [404] = 12,   // Adamantite Leggings
        [1215] = 23,  // Titanium Mask
        [1216] = 8,   // Titanium Helmet
        [1217] = 4,   // Titanium Headgear
        [1218] = 15,  // Titanium Breastplate
        [1219] = 11,  // Titanium Leggings
        // ---- 肉山后高级套 ----
        [551] = 15,   // Hallowed Plate Mail
        [552] = 11,   // Hallowed Greaves
        [553] = 9,    // Hallowed Helmet
        [558] = 5,    // Hallowed Headgear
        [559] = 24,   // Hallowed Mask
        [4873] = 1,   // Hallowed Hood
        [4896] = 24,  // Ancient Hallowed Mask
        [4897] = 9,   // Ancient Hallowed Helmet
        [4898] = 5,   // Ancient Hallowed Headgear
        [4899] = 1,   // Ancient Hallowed Hood
        [4900] = 15,  // Ancient Hallowed Plate Mail
        [4901] = 11,  // Ancient Hallowed Greaves
        [1001] = 20,  // Chlorophyte Mask
        [1002] = 13,  // Chlorophyte Helmet
        [1003] = 7,   // Chlorophyte Headgear
        [1004] = 18,  // Chlorophyte Plate Mail
        [1005] = 13,  // Chlorophyte Greaves
        [5524] = 2,   // Chlorophyte Visor
        [684] = 10,   // Frost Helmet
        [685] = 20,   // Frost Breastplate
        [686] = 13,   // Frost Leggings
        [1316] = 21,  // Turtle Helmet
        [1317] = 27,  // Turtle Scale Mail
        [1318] = 17,  // Turtle Leggings
        [1503] = 6,   // Spectre Hood
        [1504] = 14,  // Spectre Robe
        [1505] = 10,  // Spectre Pants
        [2189] = 18,  // Spectre Mask
        [1546] = 11,  // Shroomite Headgear
        [1547] = 11,  // Shroomite Mask
        [1548] = 11,  // Shroomite Helmet
        [1549] = 24,  // Shroomite Breastplate
        [1550] = 16,  // Shroomite Leggings
        [1159] = 6,   // Tiki Mask
        [1160] = 17,  // Tiki Shirt
        [1161] = 12,  // Tiki Pants
        [1832] = 9,   // Spooky Helmet
        [1833] = 11,  // Spooky Breastplate
        [1834] = 10,  // Spooky Leggings
        [3381] = 10,  // Stardust Helmet
        [3382] = 16,  // Stardust Breastplate
        [3383] = 12,  // Stardust Leggings
        [4982] = 12,  // Crystal Ninja Helmet
        [4983] = 14,  // Crystal Ninja Chestplate
        [4984] = 10,  // Crystal Ninja Leggings
        [3776] = 6,   // Ancient Battle Armor Hat
        [3777] = 12,  // Ancient Battle Armor Shirt
        [3778] = 8,   // Ancient Battle Armor Pants
        [3797] = 7,   // Apprentice Hat
        [3798] = 15,  // Apprentice Robe
        [3799] = 10,  // Apprentice Trousers
        [3800] = 13,  // Squire Great Helm
        [3801] = 27,  // Squire Plating
        [3802] = 18,  // Squire Greaves
        [3803] = 7,   // Huntress Wig
        [3804] = 17,  // Huntress Jerkin
        [3805] = 12,  // Huntress Pants
        [3806] = 8,   // Monk Brows
        [3807] = 22,  // Monk Shirt
        [3808] = 16,  // Monk Pants
        [3871] = 20,  // Squire Alt Head
        [3872] = 24,  // Squire Alt Shirt
        [3873] = 24,  // Squire Alt Pants
        [3874] = 7,   // Apprentice Alt Head
        [3875] = 21,  // Apprentice Alt Shirt
        [3876] = 14,  // Apprentice Alt Pants
        [3877] = 8,   // Huntress Alt Head
        [3878] = 24,  // Huntress Alt Shirt
        [3879] = 16,  // Huntress Alt Pants
        [3880] = 10,  // Monk Alt Head
        [3881] = 26,  // Monk Alt Shirt
        [3882] = 18,  // Monk Alt Pants
        // ---- 单件头饰 / 服装（原版直接给防御）----
        [37] = 1,     // Goggles
        [88] = 2,     // Mining Helmet
        [238] = 4,    // Wizard Hat
        [268] = 2,    // Diving Helmet
        [879] = 4,    // Viking Helmet
        [867] = 2,    // Green Cap
        [410] = 1,    // Mining Shirt
        [411] = 1,    // Mining Pants
        [954] = 2,    // Ancient Iron Helmet
        [955] = 4,    // Ancient Gold Helmet
        [956] = 6,    // Ancient Shadow Helmet
        [957] = 7,    // Ancient Shadow Scalemail
        [958] = 6,    // Ancient Shadow Greaves
        [959] = 6,    // Ancient Necro Helmet
        [960] = 5,    // Ancient Cobalt Helmet
        [961] = 6,    // Ancient Cobalt Breastplate
        [962] = 6,    // Ancient Cobalt Leggings
        [1135] = 1,   // Rain Hat
        [1136] = 2,   // Rain Coat
        [1283] = 1,   // Topaz Robe
        [1284] = 1,   // Sapphire Robe
        [1285] = 2,   // Emerald Robe
        [1286] = 2,   // Ruby Robe
        [1287] = 3,   // Diamond Robe
        [4256] = 3,   // Amber Robe
        [3109] = 4,   // Night Vision Helmet
        [4008] = 4,   // Ultrabright Helmet
        [5001] = 3,   // Moon Lord Legs
        [5007] = 4,   // Dead Man's Sweater
        [5068] = 1,   // Flinx Fur Coat
        [205] = 1,    // Empty Bucket（可戴头部）
        [6177] = 1,   // Royal Guard Harness
        [5588] = 10,  // Upgraded Mining Head
        [5589] = 12,  // Upgraded Mining Body
        [5590] = 11,  // Upgraded Mining Legs
        [5591] = 10,  // Upgraded Fishing Head
        [5592] = 12,  // Upgraded Fishing Body
        [5593] = 11,  // Upgraded Fishing Legs
        // ---- 饰品（装备区 3-8 槽，原版给防御）----
        [216] = 1,    // Shackle
        [156] = 1,    // Cobalt Shield
        [193] = 1,    // Obsidian Skull
        [397] = 2,    // Obsidian Shield
        [938] = 6,    // Paladin's Shield
        [1613] = 5,   // Ankh Shield
        [1595] = 2,   // Magic Cuffs
        [3016] = 8,   // Flesh Knuckles
        [3097] = 2,   // Shield of Cthulhu
        [3992] = 8,   // Berserker's Glove
        [3997] = 6,   // Frozen Shield
        [3998] = 10,  // Hero Shield
        [6183] = 2,   // Silver Shield
        [6188] = 1,   // Restoration Shield
    };

    /// <summary>查询物品防御；未知物品 / 空槽返回 0。</summary>
    public static int DefenseOf(int itemId)
        => itemId > 0 && Of.TryGetValue(itemId, out var d) ? d : 0;
}
