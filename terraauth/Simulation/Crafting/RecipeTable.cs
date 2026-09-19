// TerraAuth — 合成配方表（自动生成，勿手改）
// 数据源：Terraria 1.4.5.8 原版 Recipe.SetupRecipes 及其家具 / 雕像辅助方法（decompiled/src/Terraria/Recipe.cs）。
// 生成脚本：decompiled-tmp/gen-recipes.ps1。
//
// 用途：SSC 背包 / 箱子守恒事务在「总量必须严格不变」之外，额外放行**原版配方可解释**的
// 「材料 → 产物」净增量（合成），并按 RequiredTile / Environment 校验**合成站与环境**。
// 「只有减少、没有增加」的纯消耗由守恒层单独放行，不需要配方。
//
// 每条配方携带：<产物, 产物堆叠, RequiredTile（原版 requiredTile / SetCraftingStation；-1 = 手工合成）,
// CraftEnvironment（原版 needWater / needHoney / needLava）, 材料需求[]（含配方组需求）>。
//
// 含原版 CreateReverseWallRecipes / CreateReversePlatformRecipes 派生的反向配方（108 条）：
//   墙 → 块、平台 → 材料；派生规则只复制站位，不复制液体环境（与原版一致）。
//
// 未做环境校验（服务端不可确定性复现，故不收录；仅记录出现次数以便如实说明覆盖范围）：
//   needSnowBiome=1 / needGraveyardBiome=131 / needMechdusa=1 / needTorchGodsFavor=2
//   （雪原 / 墓地的判定依赖客户端分辨率的场景度量，机械三王 / 火把神恩依赖特殊种子与玩家解锁状态）
//
// 未收录（生成器静态不可解，按「不猜数值」原则整体跳过，绝不落半条配方）：
//   - 下列产物的配方依赖反编译提升的局部量（num / stack），无法静态求值，整条作废：3918, 3965, 3972, 3970, 3962, 3969, 3961, 3959, 3960, 3966, 3973, 3971, 3964
//   - 少量辅助方法内联数组 / 无法静态求值的表达式分支（合成站 / 环境均可静态解，已收录）

namespace TerraAuth.Simulation;

public static partial class RecipeTable
{
    /// <summary>原版配方组（RecipeGroups.Register 顺序）→ 可替代成员物品 ID 列表。</summary>
    public static readonly int[][] Groups =
    {
        new[] { 2015, 2016, 2017 },   // Birds
        new[] { 2157, 2156 },   // Scorpions
        new[] { 2018, 3563 },   // Squirrels
        new[] { 3194, 3192, 3193 },   // Bugs
        new[] { 2123, 2122 },   // Ducks
        new[] { 1998, 2001, 1994, 1995, 1996, 1999, 1997, 2000 },   // Butterflies
        new[] { 1992, 2004 },   // Fireflies
        new[] { 2006, 2007 },   // Snails
        new[] { 4334, 4335, 4336, 4338, 4339, 4337 },   // Dragonflies
        new[] { 4464, 4465 },   // Turtles
        new[] { 5212, 5300 },   // Macaws
        new[] { 5312, 5313 },   // Cockatiels
        new[] { 399, 1250 },   // CloudBalloons
        new[] { 1163, 1251 },   // BlizzardBalloons
        new[] { 983, 1252 },   // SandstormBalloons
        new[] { 4767, 5453 },   // CritterGuides
        new[] { 5309, 5454 },   // NatureGuides
        new[] { 2625, 2626 },   // Seashells
        new[] { 4009, 4282, 4283, 4284, 4285, 4286, 4287, 4288, 4289, 4290, 4291, 4292, 4293, 4294, 4295, 4296, 4297, 5277, 5278 },   // Fruit
        new[] { 3738, 3736, 3737 },   // Balloons
        new[] { 381, 1184 },   // CobaltBar
        new[] { 382, 1191 },   // MythrilBar
        new[] { 391, 1198 },   // AdamantiteBar
        new[] { 4838, 4844, 4843, 4841, 4842, 4840, 4839, 4831, 4837, 4836, 4834, 4835, 4833, 4832 },   // GemCritter
        new[] { 50, 3199 },   // MagicMirror
        new[] { 9, 619, 620, 621, 911, 1729, 2504, 2503, 5215 },   // Wood
        new[] { 3, 61, 836, 409 },   // Stone
        new[] { 169, 408, 1246, 370, 3272, 3338, 3274, 3275 },   // Sand
        new[] { 22, 704 },   // IronBar
        new[] { 21, 705 },   // SilverBar
        new[] { 19, 706 },   // GoldBar
        new[] { 3458, 3456, 3457, 3459 },   // Fragment
        new[] { 542, 852, 543, 541, 1151, 529, 853, 4261 },   // PressurePlate
        new[] { 2436, 2437, 2438 },   // Jellyfish
    };

    /// <summary>合成站等价关系（原版 Recipe.TileCountsAs / SetupTileInheritance）：左边图格算作右边图格。</summary>
    public static readonly (int Tile, int Equivalent)[] TileCountsAs =
    {
        (96, 215),
        (17, 215),
        (302, 17),
        (77, 17),
        (133, 77),
        (134, 16),
        (355, 13),
        (699, 13),
        (304, 86),
    };

    /// <summary>算作水源的图格（原版 TileID.Sets.CountsAsWaterForCrafting，如水槽 / 喷泉）：紧邻即可满足 needWater。</summary>
    public static readonly int[] WaterForCraftingTiles = { 172, 207 };

    /// <summary>逐条提取的原版配方（产物 ID / 产物堆叠 / 合成站 / 液体环境 / 材料需求，含配方组需求）。</summary>
    public static readonly CraftRecipe[] All =
    {
        new CraftRecipe(8, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(23, -1, 1),new CraftRequirement(9, 25, 1) }),   // Torch
        new CraftRecipe(974, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(664, -1, 1) }),   // IceTorch
        new CraftRecipe(974, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(833, -1, 1) }),   // IceTorch
        new CraftRecipe(974, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(834, -1, 1) }),   // IceTorch
        new CraftRecipe(974, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(835, -1, 1) }),   // IceTorch
        new CraftRecipe(4383, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(3272, -1, 1) }),   // DesertTorch
        new CraftRecipe(4383, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(3274, -1, 1) }),   // DesertTorch
        new CraftRecipe(4383, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(3275, -1, 1) }),   // DesertTorch
        new CraftRecipe(4383, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(3338, -1, 1) }),   // DesertTorch
        new CraftRecipe(4384, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(275, -1, 1) }),   // CoralTorch
        new CraftRecipe(4385, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(61, -1, 1) }),   // CorruptTorch
        new CraftRecipe(4385, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(833, -1, 1) }),   // CorruptTorch
        new CraftRecipe(4385, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(3274, -1, 1) }),   // CorruptTorch
        new CraftRecipe(4386, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(836, -1, 1) }),   // CrimsonTorch
        new CraftRecipe(4386, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(835, -1, 1) }),   // CrimsonTorch
        new CraftRecipe(4386, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(3275, -1, 1) }),   // CrimsonTorch
        new CraftRecipe(4387, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(409, -1, 1) }),   // HallowedTorch
        new CraftRecipe(4387, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(834, -1, 1) }),   // HallowedTorch
        new CraftRecipe(4387, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(3338, -1, 1) }),   // HallowedTorch
        new CraftRecipe(4388, 25, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 25),new CraftRequirement(331, -1, 1) }),   // JungleTorch
        new CraftRecipe(5293, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(183, -1, 1) }),   // MushroomTorch
        new CraftRecipe(5293, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(23, -1, 1),new CraftRequirement(183, -1, 1) }),   // MushroomTorch
        new CraftRecipe(3114, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(3111, -1, 1),new CraftRequirement(9, 25, 1) }),   // PinkTorch
        new CraftRecipe(433, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 3),new CraftRequirement(173, -1, 1) }),   // DemonTorch
        new CraftRecipe(523, 33, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 33),new CraftRequirement(522, -1, 1) }),   // CursedTorch
        new CraftRecipe(1333, 33, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 33),new CraftRequirement(1332, -1, 1) }),   // IchorTorch
        new CraftRecipe(3045, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 10),new CraftRequirement(662, -1, 1) }),   // RainbowTorch
        new CraftRecipe(427, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 10),new CraftRequirement(177, -1, 1) }),   // BlueTorch
        new CraftRecipe(428, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 10),new CraftRequirement(178, -1, 1) }),   // RedTorch
        new CraftRecipe(429, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 10),new CraftRequirement(179, -1, 1) }),   // GreenTorch
        new CraftRecipe(432, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 10),new CraftRequirement(180, -1, 1) }),   // YellowTorch
        new CraftRecipe(430, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 10),new CraftRequirement(181, -1, 1) }),   // PurpleTorch
        new CraftRecipe(431, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 10),new CraftRequirement(182, -1, 1) }),   // WhiteTorch
        new CraftRecipe(1245, 20, -1, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 20),new CraftRequirement(999, -1, 1) }),   // OrangeTorch
        new CraftRecipe(5378, 33, -1, CraftEnvironment.None, new[] { new CraftRequirement(931, -1, 33),new CraftRequirement(522, -1, 1) }),   // CursedFlare
        new CraftRecipe(5379, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(931, -1, 10),new CraftRequirement(662, -1, 1) }),   // RainbowFlare
        new CraftRecipe(5377, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(931, -1, 1),new CraftRequirement(3002, -1, 1) }),   // SpelunkerFlare
        new CraftRecipe(966, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(8, -1, 5) }),   // Campfire
        new CraftRecipe(3048, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(974, -1, 5) }),   // FrozenCampfire
        new CraftRecipe(3047, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(433, -1, 5) }),   // DemonCampfire
        new CraftRecipe(3046, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(523, -1, 5) }),   // CursedCampfire
        new CraftRecipe(3049, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(1333, -1, 5) }),   // IchorCampfire
        new CraftRecipe(3050, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(3045, -1, 5) }),   // RainbowCampfire
        new CraftRecipe(3723, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(2274, -1, 5) }),   // UltraBrightCampfire
        new CraftRecipe(3724, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(3004, -1, 5) }),   // BoneCampfire
        new CraftRecipe(4689, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(4383, -1, 5) }),   // DesertCampfire
        new CraftRecipe(4690, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(4384, -1, 5) }),   // CoralCampfire
        new CraftRecipe(4691, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(4385, -1, 5) }),   // CorruptCampfire
        new CraftRecipe(4692, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(4386, -1, 5) }),   // CrimsonCampfire
        new CraftRecipe(4693, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(4387, -1, 5) }),   // HallowedCampfire
        new CraftRecipe(4694, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(4388, -1, 5) }),   // JungleCampfire
        new CraftRecipe(5299, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 10),new CraftRequirement(5293, -1, 5) }),   // MushroomCampfire
        new CraftRecipe(5357, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(5353, -1, 5) }),   // ShimmerCampfire
        new CraftRecipe(2751, 20, 125, CraftEnvironment.None, new[] { new CraftRequirement(2701, -1, 20),new CraftRequirement(522, -1, 1) }),   // LivingCursedFireBlock
        new CraftRecipe(2752, 20, 125, CraftEnvironment.None, new[] { new CraftRequirement(2701, -1, 20),new CraftRequirement(433, -1, 1) }),   // LivingDemonFireBlock
        new CraftRecipe(2753, 20, 125, CraftEnvironment.None, new[] { new CraftRequirement(2701, -1, 20),new CraftRequirement(664, -1, 10) }),   // LivingFrostFireBlock
        new CraftRecipe(2754, 20, 125, CraftEnvironment.None, new[] { new CraftRequirement(2701, -1, 20),new CraftRequirement(1332, -1, 1) }),   // LivingIchorBlock
        new CraftRecipe(2755, 20, 125, CraftEnvironment.None, new[] { new CraftRequirement(2701, -1, 20),new CraftRequirement(2274, -1, 2) }),   // LivingUltrabrightFireBlock
        new CraftRecipe(985, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(965, -1, 10) }),   // RopeCoil
        new CraftRecipe(3005, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2996, -1, 10) }),   // VineRopeCoil
        new CraftRecipe(3078, 3, -1, CraftEnvironment.None, new[] { new CraftRequirement(150, -1, 1) }),   // WebRope
        new CraftRecipe(3080, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3078, -1, 10) }),   // WebRopeCoil
        new CraftRecipe(3077, 30, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 1) }),   // SilkRope
        new CraftRecipe(3079, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3077, -1, 10) }),   // SilkRopeCoil
        new CraftRecipe(2590, 5, -1, CraftEnvironment.None, new[] { new CraftRequirement(353, -1, 5),new CraftRequirement(23, -1, 1),new CraftRequirement(225, -1, 1),new CraftRequirement(8, -1, 1) }),   // MolotovCocktail
        new CraftRecipe(1130, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(168, -1, 1),new CraftRequirement(2431, -1, 1) }),   // Beenade
        new CraftRecipe(2586, 5, -1, CraftEnvironment.None, new[] { new CraftRequirement(168, -1, 5),new CraftRequirement(23, -1, 1) }),   // StickyGrenade
        new CraftRecipe(235, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(166, -1, 1),new CraftRequirement(23, -1, 1) }),   // StickyBomb
        new CraftRecipe(2896, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(167, -1, 1),new CraftRequirement(23, -1, 1) }),   // StickyDynamite
        new CraftRecipe(3116, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(168, -1, 2),new CraftRequirement(3111, -1, 1) }),   // BouncyGrenade
        new CraftRecipe(3115, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(166, -1, 1),new CraftRequirement(3111, -1, 1) }),   // BouncyBomb
        new CraftRecipe(3547, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(167, -1, 1),new CraftRequirement(3111, -1, 1) }),   // BouncyDynamite
        new CraftRecipe(4423, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(166, -1, 1),new CraftRequirement(3380, -1, 1) }),   // ScarabBomb
        new CraftRecipe(4908, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(166, -1, 1),new CraftRequirement(2, -1, 25) }),   // DirtBomb
        new CraftRecipe(4909, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4908, -1, 1),new CraftRequirement(23, -1, 1) }),   // DirtStickyBomb
        new CraftRecipe(4909, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(235, -1, 1),new CraftRequirement(2, -1, 25) }),   // DirtStickyBomb
        new CraftRecipe(5594, 5, 16, CraftEnvironment.None, new[] { new CraftRequirement(166, -1, 5),new CraftRequirement(381, 20, 1) }),   // SuperBomb
        new CraftRecipe(5595, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5594, -1, 1),new CraftRequirement(23, -1, 1) }),   // SuperStickyBomb
        new CraftRecipe(5595, 5, 16, CraftEnvironment.None, new[] { new CraftRequirement(235, -1, 5),new CraftRequirement(381, 20, 1) }),   // SuperStickyBomb
        new CraftRecipe(1338, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2019, -1, 1),new CraftRequirement(167, -1, 1) }),   // ExplosiveBunny
        new CraftRecipe(286, 5, -1, CraftEnvironment.None, new[] { new CraftRequirement(282, -1, 5),new CraftRequirement(23, -1, 1) }),   // StickyGlowstick
        new CraftRecipe(3112, 5, -1, CraftEnvironment.None, new[] { new CraftRequirement(282, -1, 5),new CraftRequirement(3111, -1, 1) }),   // BouncyGlowstick
        new CraftRecipe(3191, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2002, -1, 1),new CraftRequirement(75, -1, 1) }),   // EnchantedNightcrawler
        new CraftRecipe(2243, 2, 17, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 1) }),   // GlassBowl
        new CraftRecipe(2244, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 1) }),   // WineGlass
        new CraftRecipe(351, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 1) }),   // Mug
        new CraftRecipe(353, 1, 94, CraftEnvironment.None, new[] { new CraftRequirement(351, -1, 1) }),   // Ale
        new CraftRecipe(357, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(5, -1, 1),new CraftRequirement(261, -1, 1) }),   // BowlofSoup
        new CraftRecipe(3195, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(3192, -1, 1),new CraftRequirement(3193, -1, 1),new CraftRequirement(3194, -1, 1) }),   // GrubSoup
        new CraftRecipe(1787, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 15) }),   // PumpkinPie
        new CraftRecipe(5092, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(68, -1, 8) }),   // MonsterLasagna
        new CraftRecipe(5092, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(1330, -1, 8) }),   // MonsterLasagna
        new CraftRecipe(5093, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2121, -1, 2) }),   // FroggleBunwich
        new CraftRecipe(4033, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2121, -1, 1) }),   // SauteedFrogLegs
        new CraftRecipe(4032, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2123, -1, 1) }),   // RoastedDuck
        new CraftRecipe(4032, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2122, -1, 1) }),   // RoastedDuck
        new CraftRecipe(4032, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4374, -1, 1) }),   // RoastedDuck
        new CraftRecipe(4031, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2015, -1, 1) }),   // RoastedBird
        new CraftRecipe(4031, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2017, -1, 1) }),   // RoastedBird
        new CraftRecipe(4031, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2016, -1, 1) }),   // RoastedBird
        new CraftRecipe(4031, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2205, -1, 1) }),   // RoastedBird
        new CraftRecipe(4031, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4359, -1, 1) }),   // RoastedBird
        new CraftRecipe(4031, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4395, -1, 1) }),   // RoastedBird
        new CraftRecipe(4024, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2018, -1, 1) }),   // GrilledSquirrel
        new CraftRecipe(4024, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(3563, -1, 1) }),   // GrilledSquirrel
        new CraftRecipe(4014, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2019, -1, 1) }),   // BunnyStew
        new CraftRecipe(5645, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4838, 23, 1) }),   // RockCandy
        new CraftRecipe(4019, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2006, -1, 1) }),   // Escargot
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2308, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2889, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2890, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2892, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2894, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(3564, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2893, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2895, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2891, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4274, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4340, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4362, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4482, -1, 1) }),   // GoldenDelight
        new CraftRecipe(4022, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4419, -1, 1) }),   // GoldenDelight
        new CraftRecipe(2425, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2290, -1, 1) }),   // CookedFish
        new CraftRecipe(2425, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2297, -1, 1) }),   // CookedFish
        new CraftRecipe(2425, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2299, -1, 1) }),   // CookedFish
        new CraftRecipe(2427, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2300, -1, 1) }),   // Sashimi
        new CraftRecipe(2427, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2298, -1, 1) }),   // Sashimi
        new CraftRecipe(2427, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2301, -1, 1) }),   // Sashimi
        new CraftRecipe(2427, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4401, -1, 1) }),   // Sashimi
        new CraftRecipe(2426, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2316, -1, 1) }),   // CookedShrimp
        new CraftRecipe(4403, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4402, -1, 1) }),   // LobsterTail
        new CraftRecipe(5009, 1, 622, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1) }),   // Teacup
        new CraftRecipe(4614, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4009, -1, 1),new CraftRequirement(31, -1, 1) }),   // AppleJuice
        new CraftRecipe(4617, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4283, -1, 1),new CraftRequirement(31, -1, 1),new CraftRequirement(593, -1, 1) }),   // BananaDaiquiri
        new CraftRecipe(4615, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4023, -1, 2),new CraftRequirement(31, -1, 1) }),   // GrapeJuice
        new CraftRecipe(4616, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4291, -1, 1),new CraftRequirement(31, -1, 1) }),   // Lemonade
        new CraftRecipe(4618, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4293, -1, 1),new CraftRequirement(31, -1, 1) }),   // PeachSangria
        new CraftRecipe(4619, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4294, -1, 1),new CraftRequirement(4287, -1, 1),new CraftRequirement(31, -1, 1) }),   // PinaColada
        new CraftRecipe(4620, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4292, -1, 1),new CraftRequirement(4294, -1, 1),new CraftRequirement(31, -1, 1) }),   // TropicalSmoothie
        new CraftRecipe(4621, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4285, -1, 1),new CraftRequirement(4296, -1, 1),new CraftRequirement(31, -1, 1) }),   // BloodyMoscato
        new CraftRecipe(4622, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4284, -1, 1),new CraftRequirement(4289, -1, 1),new CraftRequirement(31, -1, 1) }),   // SmoothieofDarkness
        new CraftRecipe(4624, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4009, 18, 2),new CraftRequirement(31, -1, 1) }),   // FruitJuice
        new CraftRecipe(4625, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4009, 18, 3),new CraftRequirement(356, -1, 1) }),   // FruitSalad
        new CraftRecipe(4623, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(4297, -1, 1),new CraftRequirement(4288, -1, 1),new CraftRequirement(31, -1, 1) }),   // PrismaticPunch
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2303, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2317, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2305, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2304, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2313, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2318, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2312, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2306, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2319, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2314, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2302, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2315, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2307, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2310, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2309, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2321, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(4034, 1, 96, CraftEnvironment.None, new[] { new CraftRequirement(2311, -1, 2) }),   // SeafoodDinner
        new CraftRecipe(5537, 1, 215, CraftEnvironment.None, new[] { new CraftRequirement(2290, -1, 1) }),   // BlackenedFish
        new CraftRecipe(5537, 1, 215, CraftEnvironment.None, new[] { new CraftRequirement(2297, -1, 1) }),   // BlackenedFish
        new CraftRecipe(5537, 1, 215, CraftEnvironment.None, new[] { new CraftRequirement(2299, -1, 1) }),   // BlackenedFish
        new CraftRecipe(5537, 1, 215, CraftEnvironment.None, new[] { new CraftRequirement(4401, -1, 1) }),   // BlackenedFish
        new CraftRecipe(5537, 1, 215, CraftEnvironment.None, new[] { new CraftRequirement(2302, -1, 1) }),   // BlackenedFish
        new CraftRecipe(5537, 1, 215, CraftEnvironment.None, new[] { new CraftRequirement(2298, -1, 1) }),   // BlackenedFish
        new CraftRecipe(5537, 1, 215, CraftEnvironment.None, new[] { new CraftRequirement(2300, -1, 1) }),   // BlackenedFish
        new CraftRecipe(5537, 1, 215, CraftEnvironment.None, new[] { new CraftRequirement(2301, -1, 1) }),   // BlackenedFish
        new CraftRecipe(968, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(967, -1, 1),new CraftRequirement(9, 25, 1) }),   // MarshmallowonaStick
        new CraftRecipe(31, 2, 17, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 1) }),   // Bottle
        new CraftRecipe(126, 1, -1, CraftEnvironment.Water, new[] { new CraftRequirement(31, -1, 1) }),   // BottledWater
        new CraftRecipe(1134, 1, -1, CraftEnvironment.Honey, new[] { new CraftRequirement(31, -1, 1) }),   // BottledHoney
        new CraftRecipe(422, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 10),new CraftRequirement(501, -1, 2),new CraftRequirement(369, -1, 1) }),   // HolyWater
        new CraftRecipe(423, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 10),new CraftRequirement(370, -1, 1),new CraftRequirement(59, -1, 1) }),   // UnholyWater
        new CraftRecipe(3477, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 10),new CraftRequirement(1246, -1, 1),new CraftRequirement(2171, -1, 1) }),   // BloodWater
        new CraftRecipe(28, 2, 13, CraftEnvironment.None, new[] { new CraftRequirement(5, -1, 1),new CraftRequirement(23, -1, 2),new CraftRequirement(31, -1, 2) }),   // LesserHealingPotion
        new CraftRecipe(227, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(5, -1, 1),new CraftRequirement(183, -1, 1),new CraftRequirement(3111, -1, 1),new CraftRequirement(31, -1, 1) }),   // RestorationPotion
        new CraftRecipe(188, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(28, -1, 2),new CraftRequirement(183, -1, 1) }),   // HealingPotion
        new CraftRecipe(499, 3, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 3),new CraftRequirement(501, -1, 3),new CraftRequirement(502, -1, 1) }),   // GreaterHealingPotion
        new CraftRecipe(5496, 3, 13, CraftEnvironment.None, new[] { new CraftRequirement(499, -1, 3),new CraftRequirement(1291, -1, 1) }),   // LifeFruitHealingPotion
        new CraftRecipe(3544, 4, 13, CraftEnvironment.None, new[] { new CraftRequirement(499, -1, 4),new CraftRequirement(3457, -1, 1),new CraftRequirement(3458, -1, 1),new CraftRequirement(3459, -1, 1),new CraftRequirement(3456, -1, 1) }),   // SuperHealingPotion
        new CraftRecipe(189, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(110, -1, 2),new CraftRequirement(183, -1, 1) }),   // ManaPotion
        new CraftRecipe(2209, 8, 13, CraftEnvironment.None, new[] { new CraftRequirement(500, -1, 8),new CraftRequirement(75, -1, 2),new CraftRequirement(1508, -1, 1) }),   // SuperManaPotion
        new CraftRecipe(2756, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(314, -1, 1),new CraftRequirement(315, -1, 1),new CraftRequirement(317, -1, 1),new CraftRequirement(316, -1, 1),new CraftRequirement(2358, -1, 1),new CraftRequirement(318, -1, 1) }),   // GenderChangePotion
        new CraftRecipe(5099, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(313, -1, 1),new CraftRequirement(314, -1, 1),new CraftRequirement(315, -1, 1),new CraftRequirement(317, -1, 1),new CraftRequirement(316, -1, 1),new CraftRequirement(2358, -1, 1),new CraftRequirement(318, -1, 1) }),   // GarlandHat
        new CraftRecipe(288, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(318, -1, 1),new CraftRequirement(317, -1, 1),new CraftRequirement(173, -1, 1) }),   // ObsidianSkinPotion
        new CraftRecipe(289, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(5, -1, 1) }),   // RegenerationPotion
        new CraftRecipe(290, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(315, -1, 1),new CraftRequirement(276, -1, 1) }),   // SwiftnessPotion
        new CraftRecipe(291, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(317, -1, 1),new CraftRequirement(275, -1, 1) }),   // GillsPotion
        new CraftRecipe(5573, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(8, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(314, -1, 1),new CraftRequirement(318, -1, 1) }),   // TorchGodPotion
        new CraftRecipe(292, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(11, -1, 1) }),   // IronskinPotion
        new CraftRecipe(292, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(700, -1, 1) }),   // IronskinPotion
        new CraftRecipe(293, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(314, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(75, -1, 1) }),   // ManaRegenerationPotion
        new CraftRecipe(294, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(314, -1, 1),new CraftRequirement(316, -1, 1),new CraftRequirement(75, -1, 1) }),   // MagicPowerPotion
        new CraftRecipe(295, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(315, -1, 1),new CraftRequirement(320, -1, 1) }),   // FeatherfallPotion
        new CraftRecipe(296, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(315, -1, 1),new CraftRequirement(314, -1, 1),new CraftRequirement(13, -1, 1) }),   // SpelunkerPotion
        new CraftRecipe(296, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(315, -1, 1),new CraftRequirement(314, -1, 1),new CraftRequirement(702, -1, 1) }),   // SpelunkerPotion
        new CraftRecipe(297, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(315, -1, 1),new CraftRequirement(314, -1, 1) }),   // InvisibilityPotion
        new CraftRecipe(298, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(183, -1, 1) }),   // ShinePotion
        new CraftRecipe(299, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(315, -1, 1) }),   // NightOwlPotion
        new CraftRecipe(300, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(316, -1, 1),new CraftRequirement(68, -1, 1) }),   // BattlePotion
        new CraftRecipe(300, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(316, -1, 1),new CraftRequirement(1330, -1, 1) }),   // BattlePotion
        new CraftRecipe(301, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(316, -1, 1),new CraftRequirement(276, -1, 1) }),   // ThornsPotion
        new CraftRecipe(302, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(317, -1, 1),new CraftRequirement(319, -1, 1) }),   // WaterWalkingPotion
        new CraftRecipe(303, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(38, -1, 1) }),   // ArcheryPotion
        new CraftRecipe(304, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(315, -1, 1),new CraftRequirement(319, -1, 1) }),   // HunterPotion
        new CraftRecipe(305, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(318, -1, 1),new CraftRequirement(316, -1, 1),new CraftRequirement(315, -1, 1),new CraftRequirement(320, -1, 1) }),   // GravitationPotion
        new CraftRecipe(2354, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(1127, -1, 1),new CraftRequirement(317, -1, 1) }),   // FishingPotion
        new CraftRecipe(2356, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(999, -1, 1),new CraftRequirement(314, -1, 1),new CraftRequirement(2358, -1, 1),new CraftRequirement(317, -1, 1) }),   // CratePotion
        new CraftRecipe(2325, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(315, -1, 1),new CraftRequirement(2358, -1, 1),new CraftRequirement(314, -1, 1) }),   // BuilderPotion
        new CraftRecipe(2326, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(154, -1, 1),new CraftRequirement(316, -1, 1),new CraftRequirement(2358, -1, 1) }),   // TitanPotion
        new CraftRecipe(2329, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2358, -1, 1),new CraftRequirement(150, -1, 10) }),   // TrapsightPotion
        new CraftRecipe(2355, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(317, -1, 1),new CraftRequirement(275, -1, 1) }),   // SonarPotion
        new CraftRecipe(2322, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(323, -1, 1),new CraftRequirement(315, -1, 1) }),   // MiningPotion
        new CraftRecipe(2327, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2358, -1, 1),new CraftRequirement(317, -1, 1) }),   // FlipperPotion
        new CraftRecipe(2323, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2305, -1, 1),new CraftRequirement(313, -1, 1) }),   // HeartreachPotion
        new CraftRecipe(2324, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2304, -1, 1),new CraftRequirement(313, -1, 1) }),   // CalmingPotion
        new CraftRecipe(2328, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2311, -1, 1),new CraftRequirement(314, -1, 1) }),   // SummoningPotion
        new CraftRecipe(2344, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2313, -1, 1),new CraftRequirement(314, -1, 1) }),   // AmmoReservationPotion
        new CraftRecipe(2345, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2310, -1, 1),new CraftRequirement(314, -1, 1),new CraftRequirement(2358, -1, 1),new CraftRequirement(317, -1, 1) }),   // LifeforcePotion
        new CraftRecipe(2346, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2303, -1, 1),new CraftRequirement(315, -1, 1) }),   // EndurancePotion
        new CraftRecipe(2348, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2312, -1, 1),new CraftRequirement(2315, -1, 2),new CraftRequirement(318, -1, 1) }),   // InfernoPotion
        new CraftRecipe(2347, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2319, -1, 1),new CraftRequirement(316, -1, 1) }),   // RagePotion
        new CraftRecipe(2349, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2318, -1, 1),new CraftRequirement(316, -1, 1) }),   // WrathPotion
        new CraftRecipe(2350, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2309, -1, 1),new CraftRequirement(313, -1, 1) }),   // RecallPotion
        new CraftRecipe(2997, 3, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 3),new CraftRequirement(2309, -1, 1),new CraftRequirement(315, -1, 1) }),   // WormholePotion
        new CraftRecipe(2351, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2317, -1, 1),new CraftRequirement(318, -1, 1) }),   // TeleportationPotion
        new CraftRecipe(2352, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2307, -1, 1),new CraftRequirement(2358, -1, 1) }),   // LovePotion
        new CraftRecipe(2353, 2, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 2),new CraftRequirement(2321, -1, 1),new CraftRequirement(316, -1, 1) }),   // StinkPotion
        new CraftRecipe(2359, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(2306, -1, 1),new CraftRequirement(2358, -1, 1) }),   // WarmthPotion
        new CraftRecipe(4477, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(317, -1, 1),new CraftRequirement(4361, -1, 1),new CraftRequirement(4412, -1, 1) }),   // LuckPotionLesser
        new CraftRecipe(4478, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(317, -1, 1),new CraftRequirement(4361, -1, 1),new CraftRequirement(4413, -1, 1) }),   // LuckPotion
        new CraftRecipe(4479, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(317, -1, 1),new CraftRequirement(4361, -1, 1),new CraftRequirement(4414, -1, 1) }),   // LuckPotionGreater
        new CraftRecipe(4870, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(2350, -1, 1),new CraftRequirement(2315, -1, 1) }),   // PotionOfReturn
        new CraftRecipe(5211, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(318, -1, 1),new CraftRequirement(315, -1, 1),new CraftRequirement(314, -1, 1),new CraftRequirement(62, -1, 5) }),   // BiomeSightPotion
        new CraftRecipe(1359, 1, 243, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(209, -1, 1) }),   // FlaskofPoison
        new CraftRecipe(1354, 1, 243, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(174, -1, 3) }),   // FlaskofFire
        new CraftRecipe(1340, 1, 243, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(1339, -1, 5) }),   // FlaskofVenom
        new CraftRecipe(1355, 1, 243, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(1348, -1, 5) }),   // FlaskofGold
        new CraftRecipe(1356, 1, 243, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(1332, -1, 2) }),   // FlaskofIchor
        new CraftRecipe(1353, 1, 243, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(522, -1, 2) }),   // FlaskofCursedFlames
        new CraftRecipe(1357, 1, 243, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(1346, -1, 5) }),   // FlaskofNanites
        new CraftRecipe(1358, 1, 243, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(1345, -1, 5) }),   // FlaskofParty
        new CraftRecipe(3092, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(520, -1, 15) }),   // LightKey
        new CraftRecipe(3091, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(521, -1, 15) }),   // NightKey
        new CraftRecipe(949, 15, -1, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 1) }),   // Snowball
        new CraftRecipe(3103, 1, 125, CraftEnvironment.None, new[] { new CraftRequirement(40, -1, 9999) }),   // EndlessQuiver
        new CraftRecipe(3104, 1, 125, CraftEnvironment.None, new[] { new CraftRequirement(97, -1, 9999) }),   // EndlessMusketPouch
        new CraftRecipe(40, 25, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 1),new CraftRequirement(3, -1, 1) }),   // WoodenArrow
        new CraftRecipe(41, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(40, -1, 10),new CraftRequirement(8, -1, 1) }),   // FlamingArrow
        new CraftRecipe(988, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(40, -1, 10),new CraftRequirement(974, -1, 1) }),   // FrostburnArrow
        new CraftRecipe(51, 10, -1, CraftEnvironment.None, new[] { new CraftRequirement(40, -1, 10),new CraftRequirement(75, -1, 1) }),   // JestersArrow
        new CraftRecipe(47, 20, 16, CraftEnvironment.None, new[] { new CraftRequirement(40, -1, 20),new CraftRequirement(69, -1, 1) }),   // UnholyArrow
        new CraftRecipe(47, 10, 16, CraftEnvironment.None, new[] { new CraftRequirement(40, -1, 10),new CraftRequirement(1330, -1, 1) }),   // UnholyArrow
        new CraftRecipe(265, 100, 16, CraftEnvironment.None, new[] { new CraftRequirement(40, -1, 100),new CraftRequirement(175, -1, 1) }),   // HellfireArrow
        new CraftRecipe(545, 150, 134, CraftEnvironment.None, new[] { new CraftRequirement(40, -1, 150),new CraftRequirement(522, -1, 1) }),   // CursedArrow
        new CraftRecipe(1334, 150, 134, CraftEnvironment.None, new[] { new CraftRequirement(40, -1, 150),new CraftRequirement(1332, -1, 1) }),   // IchorArrow
        new CraftRecipe(516, 200, 134, CraftEnvironment.None, new[] { new CraftRequirement(40, -1, 200),new CraftRequirement(501, -1, 3),new CraftRequirement(526, -1, 1) }),   // HolyArrow
        new CraftRecipe(1341, 35, 134, CraftEnvironment.None, new[] { new CraftRequirement(40, -1, 35),new CraftRequirement(1339, -1, 1) }),   // VenomArrow
        new CraftRecipe(1235, 150, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 1) }),   // ChlorophyteArrow
        new CraftRecipe(1310, 100, 16, CraftEnvironment.None, new[] { new CraftRequirement(209, -1, 1) }),   // PoisonDart
        new CraftRecipe(3009, 100, 134, CraftEnvironment.None, new[] { new CraftRequirement(502, -1, 1) }),   // CrystalDart
        new CraftRecipe(3010, 100, 134, CraftEnvironment.None, new[] { new CraftRequirement(522, -1, 1) }),   // CursedDart
        new CraftRecipe(3011, 100, 134, CraftEnvironment.None, new[] { new CraftRequirement(1332, -1, 1) }),   // IchorDart
        new CraftRecipe(234, 70, 16, CraftEnvironment.None, new[] { new CraftRequirement(97, -1, 70),new CraftRequirement(117, -1, 1) }),   // MeteorShot
        new CraftRecipe(278, 70, 16, CraftEnvironment.None, new[] { new CraftRequirement(97, -1, 70),new CraftRequirement(21, -1, 1) }),   // SilverBullet
        new CraftRecipe(4915, 70, 16, CraftEnvironment.None, new[] { new CraftRequirement(97, -1, 70),new CraftRequirement(705, -1, 1) }),   // TungstenBullet
        new CraftRecipe(515, 100, 134, CraftEnvironment.None, new[] { new CraftRequirement(97, -1, 100),new CraftRequirement(502, -1, 1) }),   // CrystalBullet
        new CraftRecipe(546, 150, 134, CraftEnvironment.None, new[] { new CraftRequirement(97, -1, 150),new CraftRequirement(522, -1, 1) }),   // CursedBullet
        new CraftRecipe(1335, 150, 134, CraftEnvironment.None, new[] { new CraftRequirement(97, -1, 150),new CraftRequirement(1332, -1, 1) }),   // IchorBullet
        new CraftRecipe(1179, 60, 134, CraftEnvironment.None, new[] { new CraftRequirement(97, -1, 60),new CraftRequirement(1006, -1, 1) }),   // ChlorophyteBullet
        new CraftRecipe(1302, 50, 18, CraftEnvironment.None, new[] { new CraftRequirement(1432, -1, 50),new CraftRequirement(1344, -1, 1) }),   // HighVelocityBullet
        new CraftRecipe(1349, 50, 18, CraftEnvironment.None, new[] { new CraftRequirement(1432, -1, 50),new CraftRequirement(1345, -1, 1) }),   // PartyBullet
        new CraftRecipe(1350, 50, 18, CraftEnvironment.None, new[] { new CraftRequirement(1432, -1, 50),new CraftRequirement(1346, -1, 1) }),   // NanoBullet
        new CraftRecipe(1351, 50, 18, CraftEnvironment.None, new[] { new CraftRequirement(1432, -1, 50),new CraftRequirement(1347, -1, 1) }),   // ExplodingBullet
        new CraftRecipe(1352, 50, 18, CraftEnvironment.None, new[] { new CraftRequirement(1432, -1, 50),new CraftRequirement(1348, -1, 1) }),   // GoldenBullet
        new CraftRecipe(1342, 50, 18, CraftEnvironment.None, new[] { new CraftRequirement(1432, -1, 50),new CraftRequirement(1339, -1, 1) }),   // VenomBullet
        new CraftRecipe(4447, 1, -1, CraftEnvironment.Water, new[] { new CraftRequirement(4459, -1, 1) }),   // WetRocket
        new CraftRecipe(4448, 1, -1, CraftEnvironment.Lava, new[] { new CraftRequirement(4459, -1, 1) }),   // LavaRocket
        new CraftRecipe(4449, 1, -1, CraftEnvironment.Honey, new[] { new CraftRequirement(4459, -1, 1) }),   // HoneyRocket
        new CraftRecipe(4459, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4447, -1, 1) }),   // DryRocket
        new CraftRecipe(4459, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4448, -1, 1) }),   // DryRocket
        new CraftRecipe(4459, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4449, -1, 1) }),   // DryRocket
        new CraftRecipe(4824, 1, -1, CraftEnvironment.Water, new[] { new CraftRequirement(4827, -1, 1) }),   // WetBomb
        new CraftRecipe(4825, 1, -1, CraftEnvironment.Lava, new[] { new CraftRequirement(4827, -1, 1) }),   // LavaBomb
        new CraftRecipe(4826, 1, -1, CraftEnvironment.Honey, new[] { new CraftRequirement(4827, -1, 1) }),   // HoneyBomb
        new CraftRecipe(4827, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4824, -1, 1) }),   // DryBomb
        new CraftRecipe(4827, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4825, -1, 1) }),   // DryBomb
        new CraftRecipe(4827, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4826, -1, 1) }),   // DryBomb
        new CraftRecipe(4457, 100, 247, CraftEnvironment.None, new[] { new CraftRequirement(773, -1, 100),new CraftRequirement(1552, -1, 1) }),   // MiniNukeI
        new CraftRecipe(4458, 100, 247, CraftEnvironment.None, new[] { new CraftRequirement(774, -1, 100),new CraftRequirement(1552, -1, 1) }),   // MiniNukeII
        new CraftRecipe(67, 5, 13, CraftEnvironment.None, new[] { new CraftRequirement(60, -1, 1) }),   // VilePowder
        new CraftRecipe(2886, 5, 13, CraftEnvironment.None, new[] { new CraftRequirement(2887, -1, 1) }),   // ViciousPowder
        new CraftRecipe(5438, 1, 13, CraftEnvironment.None, new[] { new CraftRequirement(5395, -1, 3),new CraftRequirement(154, -1, 3),new CraftRequirement(172, -1, 3) }),   // Fertilizer
        new CraftRecipe(287, 50, -1, CraftEnvironment.None, new[] { new CraftRequirement(279, -1, 50),new CraftRequirement(67, -1, 1) }),   // PoisonedKnife
        new CraftRecipe(287, 50, -1, CraftEnvironment.None, new[] { new CraftRequirement(279, -1, 50),new CraftRequirement(2886, -1, 1) }),   // PoisonedKnife
        new CraftRecipe(3610, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3609, -1, 1) }),   // ConveyorBeltRight
        new CraftRecipe(3609, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3610, -1, 1) }),   // ConveyorBeltLeft
        new CraftRecipe(94, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 1) }),   // WoodPlatform
        new CraftRecipe(4416, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 1) }),   // StonePlatform
        new CraftRecipe(2518, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 1) }),   // PalmWoodPlatform
        new CraftRecipe(2566, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 1) }),   // BorealWoodPlatform
        new CraftRecipe(632, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 1) }),   // RichMahoganyPlatform
        new CraftRecipe(631, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 1) }),   // EbonwoodPlatform
        new CraftRecipe(913, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 1) }),   // ShadewoodPlatform
        new CraftRecipe(633, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 1) }),   // PearlwoodPlatform
        new CraftRecipe(2744, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 1) }),   // CactusPlatform
        new CraftRecipe(1702, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 1) }),   // GlassPlatform
        new CraftRecipe(1796, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 1) }),   // PumpkinPlatform
        new CraftRecipe(1818, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 1) }),   // SpookyPlatform
        new CraftRecipe(634, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 1) }),   // BonePlatform
        new CraftRecipe(2549, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 1) }),   // MushroomPlatform
        new CraftRecipe(2581, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 1) }),   // SlimePlatform
        new CraftRecipe(2627, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 1) }),   // SteampunkPlatform
        new CraftRecipe(2628, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 1) }),   // SkywarePlatform
        new CraftRecipe(2629, 2, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 1) }),   // LivingWoodPlatform
        new CraftRecipe(2630, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 1) }),   // HoneyPlatform
        new CraftRecipe(1457, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(192, -1, 1) }),   // ObsidianPlatform
        new CraftRecipe(3144, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 1) }),   // MeteoritePlatform
        new CraftRecipe(3146, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 1) }),   // GranitePlatform
        new CraftRecipe(3145, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 1) }),   // MarblePlatform
        new CraftRecipe(2822, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 1) }),   // MartianPlatform
        new CraftRecipe(3903, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 1) }),   // CrystalPlatform
        new CraftRecipe(3945, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 1) }),   // SpiderPlatform
        new CraftRecipe(3905, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 1) }),   // DynastyPlatform
        new CraftRecipe(3906, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 1) }),   // LihzahrdPlatform
        new CraftRecipe(3907, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 1) }),   // FleshPlatform
        new CraftRecipe(3957, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(3955, -1, 1) }),   // LesionPlatform
        new CraftRecipe(3908, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 1) }),   // FrozenPlatform
        new CraftRecipe(4159, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 1) }),   // SolarPlatform
        new CraftRecipe(4180, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 1) }),   // VortexPlatform
        new CraftRecipe(4201, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 1) }),   // NebulaPlatform
        new CraftRecipe(4222, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 1) }),   // StardustPlatform
        new CraftRecipe(4311, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 1) }),   // SandstonePlatform
        new CraftRecipe(4580, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 1) }),   // BambooPlatform
        new CraftRecipe(5162, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 1) }),   // CoralPlatform
        new CraftRecipe(5183, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 1) }),   // BalloonPlatform
        new CraftRecipe(5204, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 1) }),   // AshWoodPlatform
        new CraftRecipe(5292, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(4392, -1, 1) }),   // EchoPlatform
        new CraftRecipe(5544, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 1) }),   // CloudPlatform
        new CraftRecipe(1384, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(134, -1, 1) }),   // BlueBrickPlatform
        new CraftRecipe(1386, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(137, -1, 1) }),   // GreenBrickPlatform
        new CraftRecipe(1385, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(139, -1, 1) }),   // PinkBrickPlatform
        new CraftRecipe(5562, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 1) }),   // AetheriumPlatform
        new CraftRecipe(5615, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 1) }),   // FallenStarPlatform
        new CraftRecipe(5703, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 1) }),   // FeywoodPlatform
        new CraftRecipe(5726, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 1) }),   // HallowedPlatform
        new CraftRecipe(5751, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 1) }),   // GothicPlatform
        new CraftRecipe(5770, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 1) }),   // DemonitePlatform
        new CraftRecipe(5791, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 1) }),   // CrimtanePlatform
        new CraftRecipe(5812, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 1) }),   // SnowPlatform
        new CraftRecipe(5833, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 1) }),   // FlinxFurPlatform
        new CraftRecipe(5852, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 1) }),   // PinePlatform
        new CraftRecipe(5852, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 1) }),   // PinePlatform
        new CraftRecipe(5872, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 1) }),   // EasterPlatform
        new CraftRecipe(5912, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 1) }),   // JellyfishPlatform
        new CraftRecipe(5946, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 1) }),   // HarpyPlatform
        new CraftRecipe(5989, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 1) }),   // MoonplatePlatform
        new CraftRecipe(6012, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 1) }),   // LibrarianPlatform
        new CraftRecipe(6035, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 1) }),   // SpikePlatform
        new CraftRecipe(6058, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 1) }),   // OfficePlatform
        new CraftRecipe(6081, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 1) }),   // ForbiddenPlatform
        new CraftRecipe(6103, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 1) }),   // WaterPlatform
        new CraftRecipe(6125, 2, -1, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 1) }),   // BoulderPlatform
        new CraftRecipe(1389, 2, 300, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 1) }),   // WoodShelf
        new CraftRecipe(1388, 2, 300, CraftEnvironment.None, new[] { new CraftRequirement(145, -1, 1) }),   // BrassShelf
        new CraftRecipe(1418, 2, 300, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 1) }),   // DungeonShelf
        new CraftRecipe(1387, 2, 300, CraftEnvironment.None, new[] { new CraftRequirement(717, -1, 1) }),   // MetalShelf
        new CraftRecipe(1431, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(75, -1, 1) }),   // StarinaBottle
        new CraftRecipe(1993, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(1992, -1, 1) }),   // FireflyinaBottle
        new CraftRecipe(2005, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(2004, -1, 1) }),   // LightningBuginaBottle
        new CraftRecipe(4848, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(4847, -1, 1) }),   // LavaflyinaBottle
        new CraftRecipe(5351, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(5350, -1, 1) }),   // ShimmerflyinaBottle
        new CraftRecipe(4695, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(520, -1, 1) }),   // SoulBottleLight
        new CraftRecipe(4696, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(521, -1, 1) }),   // SoulBottleNight
        new CraftRecipe(4697, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(575, -1, 1) }),   // SoulBottleFlight
        new CraftRecipe(4698, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(549, -1, 1) }),   // SoulBottleSight
        new CraftRecipe(4699, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(548, -1, 1) }),   // SoulBottleMight
        new CraftRecipe(4700, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(547, -1, 1) }),   // SoulBottleFright
        new CraftRecipe(1859, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(29, -1, 1),new CraftRequirement(85, -1, 4) }),   // HeartLantern
        new CraftRecipe(344, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 5),new CraftRequirement(8, -1, 1) }),   // ChineseLantern
        new CraftRecipe(342, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(9, 25, 3) }),   // TikiTorch
        new CraftRecipe(341, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 2),new CraftRequirement(8, -1, 1) }),   // LampPost
        new CraftRecipe(347, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 10),new CraftRequirement(8, -1, 1) }),   // SkullLantern
        new CraftRecipe(5472, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 6),new CraftRequirement(85, -1, 1) }),   // DeadCellsDisplayJar
        new CraftRecipe(3782, 3, 125, CraftEnvironment.None, new[] { new CraftRequirement(3186, -1, 3),new CraftRequirement(169, 27, 1) }),   // MagicSandDropper
        new CraftRecipe(3182, 1, 125, CraftEnvironment.Water, new[] { new CraftRequirement(3186, -1, 1) }),   // MagicWaterDropper
        new CraftRecipe(3184, 1, 125, CraftEnvironment.Lava, new[] { new CraftRequirement(3186, -1, 1) }),   // MagicLavaDropper
        new CraftRecipe(3185, 1, 125, CraftEnvironment.Honey, new[] { new CraftRequirement(3186, -1, 1) }),   // MagicHoneyDropper
        new CraftRecipe(2693, 1, 125, CraftEnvironment.Water, new[] { new CraftRequirement(170, -1, 1) }),   // WaterfallBlock
        new CraftRecipe(2169, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2693, -1, 1) }),   // WaterfallWall
        new CraftRecipe(2694, 1, 125, CraftEnvironment.Lava, new[] { new CraftRequirement(170, -1, 1) }),   // LavafallBlock
        new CraftRecipe(2170, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2694, -1, 1) }),   // LavafallWall
        new CraftRecipe(2787, 1, 125, CraftEnvironment.Honey, new[] { new CraftRequirement(170, -1, 1) }),   // HoneyfallBlock
        new CraftRecipe(2788, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2787, -1, 1) }),   // HoneyfallWall
        new CraftRecipe(3754, 1, 125, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 1),new CraftRequirement(169, -1, 1) }),   // SandFallBlock
        new CraftRecipe(3752, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3754, -1, 1) }),   // SandFallWall
        new CraftRecipe(3755, 1, 125, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 1),new CraftRequirement(593, -1, 1) }),   // SnowFallBlock
        new CraftRecipe(3753, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3755, -1, 1) }),   // SnowFallWall
        new CraftRecipe(5494, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5493, -1, 1) }),   // ShimmerFallWall
        new CraftRecipe(4277, 10, 125, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 10),new CraftRequirement(75, -1, 1) }),   // GoldStarryGlassBlock
        new CraftRecipe(4279, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4277, -1, 1) }),   // GoldStarryGlassWall
        new CraftRecipe(4278, 10, 125, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 10),new CraftRequirement(75, -1, 1) }),   // BlueStarryGlassBlock
        new CraftRecipe(4280, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4278, -1, 1) }),   // BlueStarryGlassWall
        new CraftRecipe(5291, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4392, -1, 1) }),   // EchoWall
        new CraftRecipe(4490, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4640, -1, 1) }),   // AmethystEcho
        new CraftRecipe(4491, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4641, -1, 1) }),   // TopazEcho
        new CraftRecipe(4492, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4642, -1, 1) }),   // SapphireEcho
        new CraftRecipe(4493, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4643, -1, 1) }),   // EmeraldEcho
        new CraftRecipe(4494, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4644, -1, 1) }),   // RubyEcho
        new CraftRecipe(4495, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4645, -1, 1) }),   // DiamondEcho
        new CraftRecipe(4647, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4646, -1, 1) }),   // AmberStoneWallEcho
        new CraftRecipe(4496, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4349, -1, 1) }),   // Cave1Echo
        new CraftRecipe(4497, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4350, -1, 1) }),   // Cave2Echo
        new CraftRecipe(4498, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4351, -1, 1) }),   // Cave3Echo
        new CraftRecipe(4499, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4352, -1, 1) }),   // Cave4Echo
        new CraftRecipe(4500, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4353, -1, 1) }),   // Cave5Echo
        new CraftRecipe(4503, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(150, -1, 1) }),   // SpiderEcho
        new CraftRecipe(4529, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(62, -1, 1) }),   // Jungle1Echo
        new CraftRecipe(4531, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(62, -1, 1) }),   // Jungle3Echo
        new CraftRecipe(4530, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(195, -1, 1) }),   // Jungle2Echo
        new CraftRecipe(4532, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(195, -1, 1) }),   // Jungle4Echo
        new CraftRecipe(3340, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3272, -1, 1) }),   // HardenedSandWall
        new CraftRecipe(3341, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3274, -1, 1) }),   // CorruptHardenedSandWall
        new CraftRecipe(3342, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3275, -1, 1) }),   // CrimsonHardenedSandWall
        new CraftRecipe(3343, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3338, -1, 1) }),   // HallowHardenedSandWall
        new CraftRecipe(3344, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3276, -1, 1) }),   // CorruptSandstoneWall
        new CraftRecipe(3345, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3277, -1, 1) }),   // CrimsonSandstoneWall
        new CraftRecipe(3346, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3339, -1, 1) }),   // HallowSandstoneWall
        new CraftRecipe(3348, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3347, -1, 1) }),   // DesertFossilWall
        new CraftRecipe(663, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(662, -1, 1) }),   // RainbowBrickWall
        new CraftRecipe(2695, 1, 125, CraftEnvironment.None, new[] { new CraftRequirement(1345, -1, 1),new CraftRequirement(170, -1, 1) }),   // ConfettiBlock
        new CraftRecipe(2696, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2695, -1, 1) }),   // ConfettiWall
        new CraftRecipe(2697, 1, 125, CraftEnvironment.None, new[] { new CraftRequirement(1345, -1, 1),new CraftRequirement(170, -1, 1) }),   // ConfettiBlockBlack
        new CraftRecipe(2698, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2697, -1, 1) }),   // ConfettiWallBlack
        new CraftRecipe(3748, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3743, -1, 1),new CraftRequirement(3744, -1, 1),new CraftRequirement(3745, -1, 1) }),   // PartyBundleOfBalloonTile
        new CraftRecipe(4090, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(2625, -1, 1) }),   // ShellPileBlock
        new CraftRecipe(4090, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(4071, -1, 1) }),   // ShellPileBlock
        new CraftRecipe(4090, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(4072, -1, 1) }),   // ShellPileBlock
        new CraftRecipe(4090, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(4073, -1, 1) }),   // ShellPileBlock
        new CraftRecipe(4090, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(2626, -1, 1) }),   // ShellPileBlock
        new CraftRecipe(4090, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(275, -1, 1) }),   // ShellPileBlock
        new CraftRecipe(5323, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4767, 15, 1),new CraftRequirement(5309, 16, 1) }),   // DontHurtComboBook
        new CraftRecipe(752, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 1) }),   // CloudWall
        new CraftRecipe(222, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(133, -1, 5) }),   // ClayPot
        new CraftRecipe(350, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(133, -1, 4) }),   // PinkVase
        new CraftRecipe(356, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(133, -1, 2) }),   // Bowl
        new CraftRecipe(4326, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(133, -1, 2) }),   // FoodPlatter
        new CraftRecipe(170, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(169, 27, 2) }),   // Glass
        new CraftRecipe(392, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 1) }),   // GlassWall
        new CraftRecipe(1267, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(392, -1, 20),new CraftRequirement(181, -1, 1) }),   // PurpleStainedGlass
        new CraftRecipe(1268, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(392, -1, 20),new CraftRequirement(180, -1, 1) }),   // YellowStainedGlass
        new CraftRecipe(1269, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(392, -1, 20),new CraftRequirement(177, -1, 1) }),   // BlueStainedGlass
        new CraftRecipe(1270, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(392, -1, 20),new CraftRequirement(179, -1, 1) }),   // GreenStainedGlass
        new CraftRecipe(1271, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(392, -1, 20),new CraftRequirement(178, -1, 1) }),   // RedStainedGlass
        new CraftRecipe(4260, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(392, -1, 20),new CraftRequirement(999, -1, 1) }),   // OrangeStainedGlass
        new CraftRecipe(1272, 50, 18, CraftEnvironment.None, new[] { new CraftRequirement(392, -1, 50),new CraftRequirement(181, -1, 1),new CraftRequirement(180, -1, 1),new CraftRequirement(177, -1, 1),new CraftRequirement(179, -1, 1),new CraftRequirement(178, -1, 1) }),   // MulticoloredStainedGlass
        new CraftRecipe(1970, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 20),new CraftRequirement(181, -1, 1) }),   // AmethystGemsparkBlock
        new CraftRecipe(2679, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1970, -1, 1) }),   // AmethystGemsparkWall
        new CraftRecipe(2680, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1970, -1, 1) }),   // AmethystGemsparkWallOff
        new CraftRecipe(1971, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 20),new CraftRequirement(180, -1, 1) }),   // TopazGemsparkBlock
        new CraftRecipe(2689, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1971, -1, 1) }),   // TopazGemsparkWall
        new CraftRecipe(2690, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1971, -1, 1) }),   // TopazGemsparkWallOff
        new CraftRecipe(1972, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 20),new CraftRequirement(177, -1, 1) }),   // SapphireGemsparkBlock
        new CraftRecipe(2687, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1972, -1, 1) }),   // SapphireGemsparkWall
        new CraftRecipe(2688, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1972, -1, 1) }),   // SapphireGemsparkWallOff
        new CraftRecipe(1973, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 20),new CraftRequirement(179, -1, 1) }),   // EmeraldGemsparkBlock
        new CraftRecipe(2683, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1973, -1, 1) }),   // EmeraldGemsparkWall
        new CraftRecipe(2684, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1973, -1, 1) }),   // EmeraldGemsparkWallOff
        new CraftRecipe(1974, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 20),new CraftRequirement(178, -1, 1) }),   // RubyGemsparkBlock
        new CraftRecipe(2685, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1974, -1, 1) }),   // RubyGemsparkWall
        new CraftRecipe(2686, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1974, -1, 1) }),   // RubyGemsparkWallOff
        new CraftRecipe(1975, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 20),new CraftRequirement(182, -1, 1) }),   // DiamondGemsparkBlock
        new CraftRecipe(2681, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1975, -1, 1) }),   // DiamondGemsparkWall
        new CraftRecipe(2682, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1975, -1, 1) }),   // DiamondGemsparkWallOff
        new CraftRecipe(1976, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 20),new CraftRequirement(999, -1, 1) }),   // AmberGemsparkBlock
        new CraftRecipe(2677, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1976, -1, 1) }),   // AmberGemsparkWall
        new CraftRecipe(2678, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1976, -1, 1) }),   // AmberGemsparkWallOff
        new CraftRecipe(4238, 2, 283, CraftEnvironment.None, new[] { new CraftRequirement(134, -1, 1) }),   // CrackedBlueBrick
        new CraftRecipe(4239, 2, 283, CraftEnvironment.None, new[] { new CraftRequirement(137, -1, 1) }),   // CrackedGreenBrick
        new CraftRecipe(4240, 2, 283, CraftEnvironment.None, new[] { new CraftRequirement(139, -1, 1) }),   // CrackedPinkBrick
        new CraftRecipe(135, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(134, -1, 1) }),   // BlueBrickWall
        new CraftRecipe(1379, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(134, -1, 1) }),   // BlueTiledWall
        new CraftRecipe(1378, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(134, -1, 1) }),   // BlueSlabWall
        new CraftRecipe(138, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(137, -1, 1) }),   // GreenBrickWall
        new CraftRecipe(1383, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(137, -1, 1) }),   // GreenTiledWall
        new CraftRecipe(1382, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(137, -1, 1) }),   // GreenSlabWall
        new CraftRecipe(140, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(139, -1, 1) }),   // PinkBrickWall
        new CraftRecipe(1381, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(139, -1, 1) }),   // PinkTiledWall
        new CraftRecipe(1380, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(139, -1, 1) }),   // PinkSlabWall
        new CraftRecipe(2119, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1) }),   // StoneSlab
        new CraftRecipe(2433, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2119, -1, 1) }),   // StoneSlabWall
        new CraftRecipe(4962, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1) }),   // AccentSlab
        new CraftRecipe(2120, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(169, -1, 1) }),   // SandstoneSlab
        new CraftRecipe(3272, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(169, -1, 1),new CraftRequirement(2, -1, 1) }),   // HardenedSand
        new CraftRecipe(3271, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(169, -1, 1),new CraftRequirement(3, -1, 1) }),   // Sandstone
        new CraftRecipe(2173, 5, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(12, -1, 1) }),   // CopperPlating
        new CraftRecipe(2432, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2173, -1, 1) }),   // CopperPlatingWall
        new CraftRecipe(2692, 5, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(699, -1, 1) }),   // TinPlating
        new CraftRecipe(2691, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2692, -1, 1) }),   // TinPlatingWall
        new CraftRecipe(775, 1, 217, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 2),new CraftRequirement(23, -1, 1) }),   // AsphaltBlock
        new CraftRecipe(1102, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 1) }),   // LihzahrdBrickWall
        new CraftRecipe(172, 1, 77, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 5) }),   // AshBlock
        new CraftRecipe(129, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 2) }),   // GrayBrick
        new CraftRecipe(130, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 1) }),   // GrayBrickWall
        new CraftRecipe(131, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(133, -1, 2) }),   // RedBrick
        new CraftRecipe(132, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(131, -1, 1) }),   // RedBrickWall
        new CraftRecipe(145, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(12, -1, 1) }),   // CopperBrick
        new CraftRecipe(146, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(145, -1, 1) }),   // CopperBrickWall
        new CraftRecipe(3951, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(11, -1, 1) }),   // IronBrick
        new CraftRecipe(3952, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3951, -1, 1) }),   // IronBrickWall
        new CraftRecipe(3953, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(700, -1, 1) }),   // LeadBrick
        new CraftRecipe(3954, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3953, -1, 1) }),   // LeadBrickWall
        new CraftRecipe(144, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(143, -1, 1) }),   // SilverBrickWall
        new CraftRecipe(143, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(14, -1, 1) }),   // SilverBrick
        new CraftRecipe(142, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(141, -1, 1) }),   // GoldBrickWall
        new CraftRecipe(141, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(13, -1, 1) }),   // GoldBrick
        new CraftRecipe(717, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(699, -1, 1) }),   // TinBrick
        new CraftRecipe(720, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(717, -1, 1) }),   // TinBrickWall
        new CraftRecipe(721, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(718, -1, 1) }),   // TungstenBrickWall
        new CraftRecipe(718, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(701, -1, 1) }),   // TungstenBrick
        new CraftRecipe(722, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(719, -1, 1) }),   // PlatinumBrickWall
        new CraftRecipe(719, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(702, -1, 1) }),   // PlatinumBrick
        new CraftRecipe(214, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(174, -1, 1) }),   // HellstoneBrick
        new CraftRecipe(3067, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(214, -1, 1) }),   // HellstoneBrickWall
        new CraftRecipe(4533, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(174, -1, 1) }),   // Lava1Echo
        new CraftRecipe(4534, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(174, -1, 1) }),   // Lava2Echo
        new CraftRecipe(4535, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(174, -1, 1) }),   // Lava3Echo
        new CraftRecipe(4536, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(174, -1, 1) }),   // Lava4Echo
        new CraftRecipe(192, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(173, -1, 1) }),   // ObsidianBrick
        new CraftRecipe(330, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(192, -1, 1) }),   // ObsidianBrickWall
        new CraftRecipe(4507, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(173, -1, 1) }),   // ObsidianBackEcho
        new CraftRecipe(606, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 1) }),   // DemoniteBrickWall
        new CraftRecipe(594, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 2) }),   // SnowBrick
        new CraftRecipe(595, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(594, -1, 1) }),   // SnowBrickWall
        new CraftRecipe(4489, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 1) }),   // SnowWallEcho
        new CraftRecipe(883, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 2) }),   // IceBrick
        new CraftRecipe(4506, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 1) }),   // IceEcho
        new CraftRecipe(884, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(883, -1, 1) }),   // IceBrickWall
        new CraftRecipe(587, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(586, -1, 1) }),   // CandyCaneWall
        new CraftRecipe(592, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(591, -1, 1) }),   // GreenCandyCaneWall
        new CraftRecipe(607, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(169, -1, 1) }),   // SandstoneBrick
        new CraftRecipe(608, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(607, -1, 1) }),   // SandstoneBrickWall
        new CraftRecipe(4051, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(3271, -1, 1) }),   // SmoothSandstone
        new CraftRecipe(4053, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 1) }),   // SmoothSandstoneWall
        new CraftRecipe(3273, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3271, -1, 1) }),   // SandstoneWall
        new CraftRecipe(4565, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 1) }),   // BambooBlockWall
        new CraftRecipe(4547, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 1) }),   // LargeBambooBlock
        new CraftRecipe(4564, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4547, -1, 1) }),   // BambooBlock
        new CraftRecipe(4548, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4547, -1, 1) }),   // LargeBambooBlockWall
        new CraftRecipe(412, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(409, -1, 2) }),   // PearlstoneBrick
        new CraftRecipe(417, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(412, -1, 1) }),   // PearlstoneBrickWall
        new CraftRecipe(4488, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(409, -1, 1) }),   // PearlstoneEcho
        new CraftRecipe(4525, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(409, -1, 1) }),   // Hallow1Echo
        new CraftRecipe(4526, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(409, -1, 1) }),   // Hallow2Echo
        new CraftRecipe(4527, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(409, -1, 1) }),   // Hallow3Echo
        new CraftRecipe(4528, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(409, -1, 1) }),   // Hallow4Echo
        new CraftRecipe(609, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(61, -1, 2) }),   // EbonstoneBrick
        new CraftRecipe(610, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(609, -1, 1) }),   // EbonstoneBrickWall
        new CraftRecipe(4486, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(61, -1, 1) }),   // EbonstoneEcho
        new CraftRecipe(4513, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(61, -1, 1) }),   // Corruption1Echo
        new CraftRecipe(4514, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(61, -1, 1) }),   // Corruption2Echo
        new CraftRecipe(4515, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(61, -1, 1) }),   // Corruption3Echo
        new CraftRecipe(4516, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(61, -1, 1) }),   // Corruption4Echo
        new CraftRecipe(4050, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(836, -1, 2) }),   // CrimstoneBrick
        new CraftRecipe(4052, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4050, -1, 1) }),   // CrimstoneBrickWall
        new CraftRecipe(4509, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(836, -1, 1) }),   // CrimstoneEcho
        new CraftRecipe(4517, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(836, -1, 1) }),   // Crimson1Echo
        new CraftRecipe(4518, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(836, -1, 1) }),   // Crimson2Echo
        new CraftRecipe(4519, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(836, -1, 1) }),   // Crimson3Echo
        new CraftRecipe(4520, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(836, -1, 1) }),   // Crimson4Echo
        new CraftRecipe(413, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1),new CraftRequirement(172, -1, 1) }),   // IridescentBrick
        new CraftRecipe(418, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(413, -1, 1) }),   // IridescentBrickWall
        new CraftRecipe(414, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1),new CraftRequirement(176, -1, 1) }),   // MudstoneBlock
        new CraftRecipe(419, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(414, -1, 1) }),   // MudstoneBrickWall
        new CraftRecipe(611, 10, 17, CraftEnvironment.None, new[] { new CraftRequirement(424, -1, 1),new CraftRequirement(133, -1, 10) }),   // RedStucco
        new CraftRecipe(615, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(611, -1, 1) }),   // RedStuccoWall
        new CraftRecipe(612, 10, 17, CraftEnvironment.None, new[] { new CraftRequirement(424, -1, 1),new CraftRequirement(169, -1, 10) }),   // YellowStucco
        new CraftRecipe(616, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(612, -1, 1) }),   // YellowStuccoWall
        new CraftRecipe(613, 10, 17, CraftEnvironment.None, new[] { new CraftRequirement(424, -1, 1),new CraftRequirement(3, -1, 10),new CraftRequirement(255, -1, 1) }),   // GreenStucco
        new CraftRecipe(617, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(613, -1, 1) }),   // GreenStuccoWall
        new CraftRecipe(614, 10, 17, CraftEnvironment.None, new[] { new CraftRequirement(424, -1, 1),new CraftRequirement(3, -1, 10) }),   // GrayStucco
        new CraftRecipe(618, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(614, -1, 1) }),   // GrayStuccoWall
        new CraftRecipe(3100, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(116, -1, 1) }),   // MeteoriteBrick
        new CraftRecipe(3101, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 1) }),   // MeteoriteBrickWall
        new CraftRecipe(2793, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(880, -1, 1),new CraftRequirement(836, -1, 5) }),   // CrimtaneBrick
        new CraftRecipe(2790, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 1) }),   // CrimtaneBrickWall
        new CraftRecipe(134, 5, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(364, -1, 1) }),   // BlueBrick
        new CraftRecipe(137, 5, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(365, -1, 1) }),   // GreenBrick
        new CraftRecipe(137, 5, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(1106, -1, 1) }),   // GreenBrick
        new CraftRecipe(139, 5, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(1104, -1, 1) }),   // PinkBrick
        new CraftRecipe(139, 5, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(1105, -1, 1) }),   // PinkBrick
        new CraftRecipe(139, 5, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(366, -1, 1) }),   // PinkBrick
        new CraftRecipe(415, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(364, -1, 1) }),   // CobaltBrick
        new CraftRecipe(420, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(415, -1, 1) }),   // CobaltBrickWall
        new CraftRecipe(416, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(365, -1, 1) }),   // MythrilBrick
        new CraftRecipe(421, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(416, -1, 1) }),   // MythrilBrickWall
        new CraftRecipe(604, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(366, -1, 1) }),   // AdamantiteBeam
        new CraftRecipe(605, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(604, -1, 1) }),   // AdamantiteBeamWall
        new CraftRecipe(1589, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(1104, -1, 1) }),   // PalladiumColumn
        new CraftRecipe(1590, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1589, -1, 1) }),   // PalladiumColumnWall
        new CraftRecipe(1591, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(1105, -1, 1) }),   // BubblegumBlock
        new CraftRecipe(1592, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1591, -1, 1) }),   // BubblegumBlockWall
        new CraftRecipe(1593, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(1106, -1, 1) }),   // TitanstoneBlock
        new CraftRecipe(1594, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1593, -1, 1) }),   // TitanstoneBlockWall
        new CraftRecipe(2792, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(947, -1, 1) }),   // ChlorophyteBrick
        new CraftRecipe(2789, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2792, -1, 1) }),   // ChlorophyteBrickWall
        new CraftRecipe(2794, 25, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 25),new CraftRequirement(1552, -1, 1) }),   // ShroomitePlating
        new CraftRecipe(2791, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2794, -1, 1) }),   // ShroomitePlatingWall
        new CraftRecipe(3461, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(3460, -1, 1) }),   // LunarBrick
        new CraftRecipe(3472, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3461, -1, 1) }),   // LunarBrickWall
        new CraftRecipe(5409, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5401, -1, 1) }),   // LunarRustBrickWall
        new CraftRecipe(5410, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5402, -1, 1) }),   // DarkCelestialBrickWall
        new CraftRecipe(5411, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5403, -1, 1) }),   // AstraBrickWall
        new CraftRecipe(5412, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5404, -1, 1) }),   // CosmicEmberBrickWall
        new CraftRecipe(5413, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5405, -1, 1) }),   // CryocoreBrickWall
        new CraftRecipe(5414, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5406, -1, 1) }),   // MercuryBrickWall
        new CraftRecipe(5415, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5407, -1, 1) }),   // StarRoyaleBrickWall
        new CraftRecipe(5416, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5408, -1, 1) }),   // HeavenforgeBrickWall
        new CraftRecipe(5418, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5417, -1, 1) }),   // AncientBlueDungeonBrickWall
        new CraftRecipe(5420, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5419, -1, 1) }),   // AncientGreenDungeonBrickWall
        new CraftRecipe(5422, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5421, -1, 1) }),   // AncientPinkDungeonBrickWall
        new CraftRecipe(5424, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5423, -1, 1) }),   // AncientGoldBrickWall
        new CraftRecipe(5426, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5425, -1, 1) }),   // AncientSilverBrickWall
        new CraftRecipe(5428, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5427, -1, 1) }),   // AncientCopperBrickWall
        new CraftRecipe(5436, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5435, -1, 1) }),   // AncientHellstoneBrickWall
        new CraftRecipe(5434, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5433, -1, 1) }),   // AncientObsidianBrickWall
        new CraftRecipe(5432, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5431, -1, 1) }),   // AncientMythrilBrickWall
        new CraftRecipe(5430, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5429, -1, 1) }),   // AncientCobaltBrickWall
        new CraftRecipe(5439, 10, 17, CraftEnvironment.None, new[] { new CraftRequirement(4354, -1, 1),new CraftRequirement(133, -1, 10) }),   // LavaMossBlock
        new CraftRecipe(5440, 10, 17, CraftEnvironment.None, new[] { new CraftRequirement(4389, -1, 1),new CraftRequirement(133, -1, 10) }),   // ArgonMossBlock
        new CraftRecipe(5441, 10, 17, CraftEnvironment.None, new[] { new CraftRequirement(4377, -1, 1),new CraftRequirement(133, -1, 10) }),   // KryptonMossBlock
        new CraftRecipe(5442, 10, 17, CraftEnvironment.None, new[] { new CraftRequirement(4378, -1, 1),new CraftRequirement(133, -1, 10) }),   // XenonMossBlock
        new CraftRecipe(5443, 10, 17, CraftEnvironment.None, new[] { new CraftRequirement(5127, -1, 1),new CraftRequirement(133, -1, 10) }),   // VioletMossBlock
        new CraftRecipe(5444, 10, 17, CraftEnvironment.None, new[] { new CraftRequirement(5128, -1, 1),new CraftRequirement(133, -1, 10) }),   // RainbowMossBlock
        new CraftRecipe(5445, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5439, -1, 1) }),   // LavaMossBlockWall
        new CraftRecipe(5446, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5440, -1, 1) }),   // ArgonMossBlockWall
        new CraftRecipe(5447, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5441, -1, 1) }),   // KryptonMossBlockWall
        new CraftRecipe(5448, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5442, -1, 1) }),   // XenonMossBlockWall
        new CraftRecipe(5449, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5443, -1, 1) }),   // VioletMossBlockWall
        new CraftRecipe(5450, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5444, -1, 1) }),   // RainbowMossBlockWall
        new CraftRecipe(5397, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5349, -1, 1) }),   // ShimmerWall
        new CraftRecipe(5398, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(5349, -1, 1),new CraftRequirement(3, -1, 1) }),   // ShimmerBrick
        new CraftRecipe(5399, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 1) }),   // ShimmerBrickWall
        new CraftRecipe(5622, 25, 17, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 25),new CraftRequirement(75, -1, 1) }),   // FallenStarBlock
        new CraftRecipe(5623, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 1) }),   // FallenStarWall
        new CraftRecipe(5710, 20, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 20),new CraftRequirement(5, -1, 1) }),   // Feywood
        new CraftRecipe(5711, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 1) }),   // FeywoodWall
        new CraftRecipe(5733, 25, 17, CraftEnvironment.None, new[] { new CraftRequirement(409, -1, 25),new CraftRequirement(1225, -1, 1) }),   // HallowedBrick
        new CraftRecipe(5734, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 1) }),   // HallowedBrickWall
        new CraftRecipe(5919, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 1) }),   // PineTreeBlockWall
        new CraftRecipe(5921, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 1) }),   // EasterBlockWall
        new CraftRecipe(5922, 20, 300, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 20),new CraftRequirement(150, -1, 5),new CraftRequirement(154, -1, 5) }),   // GothicBrick
        new CraftRecipe(5923, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 1) }),   // GothicBrickWall
        new CraftRecipe(5924, 20, 18, CraftEnvironment.None, new[] { new CraftRequirement(5070, -1, 1) }),   // FlinxFurBlock
        new CraftRecipe(5925, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 1) }),   // FlinxFurBlockWall
        new CraftRecipe(5927, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 1) }),   // JellyfishBlockWall
        new CraftRecipe(5953, 10, 305, CraftEnvironment.None, new[] { new CraftRequirement(320, -1, 1) }),   // HarpyBlock
        new CraftRecipe(5954, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 1) }),   // HarpyBlockWall
        new CraftRecipe(5997, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 1) }),   // MoonplateBlockWall
        new CraftRecipe(6019, 20, 101, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 20),new CraftRequirement(149, -1, 1) }),   // LibrarianBlock
        new CraftRecipe(6020, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 1) }),   // LibrarianBlockWall
        new CraftRecipe(6042, 5, 18, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(147, -1, 1) }),   // SpikeBlock
        new CraftRecipe(6043, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 1) }),   // SpikeBlockWall
        new CraftRecipe(6134, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 1) }),   // DamagingSpikeBlock
        new CraftRecipe(6042, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6134, -1, 1) }),   // SpikeBlock
        new CraftRecipe(6065, 10, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(23, -1, 5) }),   // OfficeBlock
        new CraftRecipe(6066, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 1) }),   // OfficeBlockWall
        new CraftRecipe(6088, 150, 18, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 150),new CraftRequirement(3783, -1, 1) }),   // ForbiddenBlock
        new CraftRecipe(6089, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 1) }),   // ForbiddenBlockWall
        new CraftRecipe(6109, 1, 18, CraftEnvironment.Water, new[] { new CraftRequirement(170, -1, 1) }),   // WaterBlock
        new CraftRecipe(6110, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 1) }),   // WaterBlockWall
        new CraftRecipe(6132, 5, 283, CraftEnvironment.None, new[] { new CraftRequirement(540, -1, 1) }),   // BoulderBlock
        new CraftRecipe(6133, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 1) }),   // BoulderBlockWall
        new CraftRecipe(1872, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 1) }),   // PineTreeBlock
        new CraftRecipe(5930, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 1) }),   // PineWoodBlock
        new CraftRecipe(5931, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 1) }),   // PineWoodBlockWall
        new CraftRecipe(3573, 5, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 1),new CraftRequirement(3, -1, 5) }),   // LunarBlockSolar
        new CraftRecipe(3574, 5, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 1),new CraftRequirement(3, -1, 5) }),   // LunarBlockVortex
        new CraftRecipe(3575, 5, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 1),new CraftRequirement(3, -1, 5) }),   // LunarBlockNebula
        new CraftRecipe(3576, 5, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 1),new CraftRequirement(3, -1, 5) }),   // LunarBlockStardust
        new CraftRecipe(3234, 5, 133, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(502, -1, 1) }),   // CrystalBlock
        new CraftRecipe(3238, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 1) }),   // CrystalBlockWall
        new CraftRecipe(2435, 5, -1, CraftEnvironment.Water, new[] { new CraftRequirement(3, -1, 5),new CraftRequirement(275, -1, 1) }),   // CoralstoneBlock
        new CraftRecipe(5306, 15, -1, CraftEnvironment.Water, new[] { new CraftRequirement(3, -1, 15),new CraftRequirement(275, -1, 1),new CraftRequirement(2625, 17, 1) }),   // ReefBlock
        new CraftRecipe(5307, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 1) }),   // ReefWall
        new CraftRecipe(5396, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5395, -1, 1) }),   // PoopWall
        new CraftRecipe(577, 5, 17, CraftEnvironment.None, new[] { new CraftRequirement(56, -1, 1),new CraftRequirement(61, -1, 5) }),   // DemoniteBrick
        new CraftRecipe(176, 1, -1, CraftEnvironment.Water, new[] { new CraftRequirement(2, -1, 1) }),   // MudBlock
        new CraftRecipe(5572, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(176, -1, 1) }),   // MudBallPlayer
        new CraftRecipe(4487, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(176, -1, 1) }),   // MudWallEcho
        new CraftRecipe(30, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2, -1, 1) }),   // DirtWall
        new CraftRecipe(4501, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2, -1, 1) }),   // Cave6Echo
        new CraftRecipe(4510, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2, -1, 1) }),   // CaveWall1Echo
        new CraftRecipe(4511, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2, -1, 1) }),   // CaveWall2Echo
        new CraftRecipe(4521, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2, -1, 1) }),   // Dirt1Echo
        new CraftRecipe(4522, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2, -1, 1) }),   // Dirt2Echo
        new CraftRecipe(4523, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2, -1, 1) }),   // Dirt3Echo
        new CraftRecipe(4524, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2, -1, 1) }),   // Dirt4Echo
        new CraftRecipe(26, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1) }),   // StoneWall
        new CraftRecipe(4502, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1) }),   // Cave7Echo
        new CraftRecipe(4512, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1) }),   // Cave8Echo
        new CraftRecipe(4537, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1) }),   // Rocks1Echo
        new CraftRecipe(4538, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1) }),   // Rocks2Echo
        new CraftRecipe(4539, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1) }),   // Rocks3Echo
        new CraftRecipe(4540, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1) }),   // Rocks4Echo
        new CraftRecipe(1723, 4, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 1) }),   // LivingWoodWall
        new CraftRecipe(3584, 4, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 1) }),   // LivingLeafWall
        new CraftRecipe(93, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 1) }),   // WoodWall
        new CraftRecipe(623, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 1) }),   // RichMahoganyWall
        new CraftRecipe(622, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 1) }),   // EbonwoodWall
        new CraftRecipe(927, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 1) }),   // ShadewoodWall
        new CraftRecipe(624, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 1) }),   // PearlwoodWall
        new CraftRecipe(2505, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 1) }),   // BorealWoodWall
        new CraftRecipe(2506, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 1) }),   // PalmWoodWall
        new CraftRecipe(5216, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 1) }),   // AshWoodWall
        new CraftRecipe(764, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 1) }),   // MushroomWall
        new CraftRecipe(1726, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 1) }),   // PumpkinWall
        new CraftRecipe(1728, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1727, -1, 1) }),   // HayWall
        new CraftRecipe(1730, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 1) }),   // SpookyWoodWall
        new CraftRecipe(3751, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 1) }),   // CogWall
        new CraftRecipe(2861, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 1) }),   // MartianConduitWall
        new CraftRecipe(3760, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3736, -1, 1) }),   // SillyBalloonPinkWall
        new CraftRecipe(3761, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3737, -1, 1) }),   // SillyBalloonPurpleWall
        new CraftRecipe(3762, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3738, -1, 1) }),   // SillyBalloonGreenWall
        new CraftRecipe(3239, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 8),new CraftRequirement(22, 28, 4) }),   // Trapdoor
        new CraftRecipe(3240, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 12),new CraftRequirement(22, 28, 4) }),   // TallGate
        new CraftRecipe(25, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 6) }),   // WoodenDoor
        new CraftRecipe(34, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 4) }),   // WoodenChair
        new CraftRecipe(48, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 8),new CraftRequirement(22, 28, 2) }),   // Chest
        new CraftRecipe(2827, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 6),new CraftRequirement(206, -1, 1) }),   // WoodenSink
        new CraftRecipe(32, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 8) }),   // WoodenTable
        new CraftRecipe(36, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 10) }),   // WorkBench
        new CraftRecipe(333, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(9, -1, 15),new CraftRequirement(149, -1, 1) }),   // Piano
        new CraftRecipe(224, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 15),new CraftRequirement(225, -1, 5) }),   // Bed
        new CraftRecipe(334, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 16) }),   // Dresser
        new CraftRecipe(354, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 20),new CraftRequirement(149, -1, 10) }),   // Bookcase
        new CraftRecipe(2519, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 14) }),   // PalmWoodBathtub
        new CraftRecipe(2520, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 15),new CraftRequirement(225, -1, 5) }),   // PalmWoodBed
        new CraftRecipe(2521, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 8) }),   // PalmWoodBench
        new CraftRecipe(2536, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 20),new CraftRequirement(149, -1, 10) }),   // PalmWoodBookcase
        new CraftRecipe(2522, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 5),new CraftRequirement(8, -1, 3) }),   // PalmWoodCandelabra
        new CraftRecipe(2523, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 4),new CraftRequirement(8, -1, 1) }),   // PalmWoodCandle
        new CraftRecipe(2524, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 4) }),   // PalmWoodChair
        new CraftRecipe(2525, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // PalmWoodChandelier
        new CraftRecipe(2526, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 8),new CraftRequirement(22, 28, 2) }),   // PalmWoodChest
        new CraftRecipe(2601, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(2504, -1, 10) }),   // PalmWoodClock
        new CraftRecipe(2528, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 6) }),   // PalmWoodDoor
        new CraftRecipe(2529, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 16) }),   // PalmWoodDresser
        new CraftRecipe(2533, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(2504, -1, 3) }),   // PalmWoodLamp
        new CraftRecipe(2530, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 6),new CraftRequirement(8, -1, 1) }),   // PalmWoodLantern
        new CraftRecipe(2531, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(2504, -1, 15),new CraftRequirement(149, -1, 1) }),   // PalmWoodPiano
        new CraftRecipe(2527, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 5),new CraftRequirement(225, -1, 2) }),   // PalmWoodSofa
        new CraftRecipe(2850, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 6),new CraftRequirement(206, -1, 1) }),   // PalmWoodSink
        new CraftRecipe(4118, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 6) }),   // ToiletPalm
        new CraftRecipe(2532, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 8) }),   // PalmWoodTable
        new CraftRecipe(2534, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 10) }),   // PalmWoodWorkBench
        new CraftRecipe(4717, 2, 106, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 1) }),   // BorealBeam
        new CraftRecipe(2552, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 14) }),   // BorealWoodBathtub
        new CraftRecipe(2553, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 15),new CraftRequirement(225, -1, 5) }),   // BorealWoodBed
        new CraftRecipe(2554, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 20),new CraftRequirement(149, -1, 10) }),   // BorealWoodBookcase
        new CraftRecipe(2555, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 5),new CraftRequirement(974, -1, 3) }),   // BorealWoodCandelabra
        new CraftRecipe(2556, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 4),new CraftRequirement(974, -1, 1) }),   // BorealWoodCandle
        new CraftRecipe(2557, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 4) }),   // BorealWoodChair
        new CraftRecipe(2558, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 4),new CraftRequirement(974, -1, 4),new CraftRequirement(85, -1, 1) }),   // BorealWoodChandelier
        new CraftRecipe(2559, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 8),new CraftRequirement(22, 28, 2) }),   // BorealWoodChest
        new CraftRecipe(2560, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(2503, -1, 10) }),   // BorealWoodClock
        new CraftRecipe(2561, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 6) }),   // BorealWoodDoor
        new CraftRecipe(2562, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 16) }),   // BorealWoodDresser
        new CraftRecipe(2563, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(974, -1, 1),new CraftRequirement(2503, -1, 3) }),   // BorealWoodLamp
        new CraftRecipe(2564, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 6),new CraftRequirement(974, -1, 1) }),   // BorealWoodLantern
        new CraftRecipe(2565, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(2503, -1, 15),new CraftRequirement(149, -1, 1) }),   // BorealWoodPiano
        new CraftRecipe(858, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 5),new CraftRequirement(225, -1, 2) }),   // BorealWoodSofa
        new CraftRecipe(2852, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 6),new CraftRequirement(206, -1, 1) }),   // BorealWoodSink
        new CraftRecipe(4119, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 6) }),   // ToiletBoreal
        new CraftRecipe(677, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 8) }),   // BorealWoodTable
        new CraftRecipe(673, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 10) }),   // BorealWoodWorkBench
        new CraftRecipe(4718, 2, 106, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 1) }),   // RichMahoganyBeam
        new CraftRecipe(2597, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(620, -1, 10) }),   // RichMahoganyClock
        new CraftRecipe(651, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 6) }),   // RichMahoganyDoor
        new CraftRecipe(629, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 4) }),   // RichMahoganyChair
        new CraftRecipe(626, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 8),new CraftRequirement(22, 28, 2) }),   // RichMahoganyChest
        new CraftRecipe(2829, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 6),new CraftRequirement(206, -1, 1) }),   // RichMahoganySink
        new CraftRecipe(639, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 8) }),   // RichMahoganyTable
        new CraftRecipe(636, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 10) }),   // RichMahoganyWorkBench
        new CraftRecipe(642, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(620, -1, 15),new CraftRequirement(149, -1, 1) }),   // RichMahoganyPiano
        new CraftRecipe(645, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 15),new CraftRequirement(225, -1, 5) }),   // RichMahoganyBed
        new CraftRecipe(648, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 16) }),   // RichMahoganyDresser
        new CraftRecipe(2026, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 20),new CraftRequirement(149, -1, 10) }),   // RichMahoganyBookcase
        new CraftRecipe(2077, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 14) }),   // RichMahoganyBathtub
        new CraftRecipe(2050, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 4),new CraftRequirement(8, -1, 1) }),   // RichMahoganyCandle
        new CraftRecipe(2038, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 6),new CraftRequirement(8, -1, 1) }),   // RichMahoganyLantern
        new CraftRecipe(2060, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // RichMahoganyChandelier
        new CraftRecipe(2087, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(620, -1, 3) }),   // RichMahoganyLamp
        new CraftRecipe(2098, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 5),new CraftRequirement(8, -1, 3) }),   // RichMahoganyCandelabra
        new CraftRecipe(2399, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 5),new CraftRequirement(225, -1, 2) }),   // RichMahoganySofa
        new CraftRecipe(4097, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 6) }),   // ToiletRichMahogany
        new CraftRecipe(650, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 6) }),   // EbonwoodDoor
        new CraftRecipe(628, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 4) }),   // EbonwoodChair
        new CraftRecipe(2593, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(619, -1, 10) }),   // EbonwoodClock
        new CraftRecipe(625, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 8),new CraftRequirement(22, 28, 2) }),   // EbonwoodChest
        new CraftRecipe(2828, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 6),new CraftRequirement(206, -1, 1) }),   // EbonwoodSink
        new CraftRecipe(638, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 8) }),   // EbonwoodTable
        new CraftRecipe(635, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 10) }),   // EbonwoodWorkBench
        new CraftRecipe(641, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(619, -1, 15),new CraftRequirement(149, -1, 1) }),   // EbonwoodPiano
        new CraftRecipe(644, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 15),new CraftRequirement(225, -1, 5) }),   // EbonwoodBed
        new CraftRecipe(647, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 16) }),   // EbonwoodDresser
        new CraftRecipe(2021, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 20),new CraftRequirement(149, -1, 10) }),   // EbonwoodBookcase
        new CraftRecipe(2073, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 14) }),   // EbonwoodBathtub
        new CraftRecipe(2033, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 6),new CraftRequirement(8, -1, 1) }),   // EbonwoodLantern
        new CraftRecipe(2046, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 4),new CraftRequirement(8, -1, 1) }),   // EbonwoodCandle
        new CraftRecipe(2056, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // EbonwoodChandelier
        new CraftRecipe(2083, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(619, -1, 3) }),   // EbonwoodLamp
        new CraftRecipe(2093, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 5),new CraftRequirement(8, -1, 3) }),   // EbonwoodCandelabra
        new CraftRecipe(2398, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 5),new CraftRequirement(225, -1, 2) }),   // EbonwoodSofa
        new CraftRecipe(4096, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 6) }),   // ToiletEbonyWood
        new CraftRecipe(2604, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(911, -1, 10) }),   // ShadewoodClock
        new CraftRecipe(912, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 6) }),   // ShadewoodDoor
        new CraftRecipe(915, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 4) }),   // ShadewoodChair
        new CraftRecipe(914, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 8),new CraftRequirement(22, 28, 2) }),   // ShadewoodChest
        new CraftRecipe(2835, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 6),new CraftRequirement(206, -1, 1) }),   // ShadewoodSink
        new CraftRecipe(917, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 8) }),   // ShadewoodTable
        new CraftRecipe(916, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 10) }),   // ShadewoodWorkBench
        new CraftRecipe(919, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(911, -1, 15),new CraftRequirement(149, -1, 1) }),   // ShadewoodPiano
        new CraftRecipe(920, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 15),new CraftRequirement(225, -1, 5) }),   // ShadewoodBed
        new CraftRecipe(2127, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 14) }),   // ShadewoodBathtub
        new CraftRecipe(2136, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 20),new CraftRequirement(149, -1, 10) }),   // ShadewoodBookcase
        new CraftRecipe(918, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 16) }),   // ShadewoodDresser
        new CraftRecipe(2142, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // ShadewoodChandelier
        new CraftRecipe(2150, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 5),new CraftRequirement(8, -1, 3) }),   // ShadewoodCandelabra
        new CraftRecipe(2146, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 6),new CraftRequirement(8, -1, 1) }),   // ShadewoodLantern
        new CraftRecipe(2132, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(911, -1, 3) }),   // ShadewoodLamp
        new CraftRecipe(2154, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 4),new CraftRequirement(8, -1, 1) }),   // ShadewoodCandle
        new CraftRecipe(2401, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 5),new CraftRequirement(225, -1, 2) }),   // ShadewoodSofa
        new CraftRecipe(4105, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 6) }),   // ToiletShadewood
        new CraftRecipe(2602, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(621, -1, 10) }),   // PearlwoodClock
        new CraftRecipe(652, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 6) }),   // PearlwoodDoor
        new CraftRecipe(630, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 4) }),   // PearlwoodChair
        new CraftRecipe(627, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 8),new CraftRequirement(22, 28, 2) }),   // PearlwoodChest
        new CraftRecipe(2830, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 6),new CraftRequirement(206, -1, 1) }),   // PearlwoodSink
        new CraftRecipe(640, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 8) }),   // PearlwoodTable
        new CraftRecipe(637, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 10) }),   // PearlwoodWorkBench
        new CraftRecipe(643, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(621, -1, 15),new CraftRequirement(149, -1, 1) }),   // PearlwoodPiano
        new CraftRecipe(646, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 15),new CraftRequirement(225, -1, 5) }),   // PearlwoodBed
        new CraftRecipe(649, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 16) }),   // PearlwoodDresser
        new CraftRecipe(2027, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 20),new CraftRequirement(149, -1, 10) }),   // PearlwoodBookcase
        new CraftRecipe(2078, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 14) }),   // PearlwoodBathtub
        new CraftRecipe(2039, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 6),new CraftRequirement(8, -1, 1) }),   // PearlwoodLantern
        new CraftRecipe(2051, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 4),new CraftRequirement(8, -1, 1) }),   // PearlwoodCandle
        new CraftRecipe(2061, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // PearlwoodChandelier
        new CraftRecipe(2088, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(621, -1, 3) }),   // PearlwoodLamp
        new CraftRecipe(2099, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 5),new CraftRequirement(8, -1, 3) }),   // PearlwoodCandelabra
        new CraftRecipe(2400, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 5),new CraftRequirement(225, -1, 2) }),   // PearlwoodSofa
        new CraftRecipe(4098, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 6) }),   // ToiletPearlwood
        new CraftRecipe(4721, 2, 106, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 1) }),   // MushroomBeam
        new CraftRecipe(2537, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 14) }),   // MushroomBathtub
        new CraftRecipe(2538, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 15),new CraftRequirement(225, -1, 5) }),   // MushroomBed
        new CraftRecipe(2539, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 8) }),   // MushroomBench
        new CraftRecipe(2540, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 20),new CraftRequirement(149, -1, 10) }),   // MushroomBookcase
        new CraftRecipe(2541, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 5),new CraftRequirement(8, -1, 3) }),   // MushroomCandelabra
        new CraftRecipe(2542, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 4),new CraftRequirement(8, -1, 1) }),   // MushroomCandle
        new CraftRecipe(810, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 4) }),   // MushroomChair
        new CraftRecipe(2543, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // MushroomChandelier
        new CraftRecipe(2544, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 8),new CraftRequirement(22, 28, 2) }),   // MushroomChest
        new CraftRecipe(2599, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(183, -1, 10) }),   // MushroomClock
        new CraftRecipe(818, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 6) }),   // MushroomDoor
        new CraftRecipe(2545, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 16) }),   // MushroomDresser
        new CraftRecipe(2547, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(183, -1, 3) }),   // MushroomLamp
        new CraftRecipe(2546, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 6),new CraftRequirement(8, -1, 1) }),   // MushroomLantern
        new CraftRecipe(2548, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(183, -1, 15),new CraftRequirement(149, -1, 1) }),   // MushroomPiano
        new CraftRecipe(2413, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 5),new CraftRequirement(225, -1, 2) }),   // MushroomSofa
        new CraftRecipe(2851, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 6),new CraftRequirement(206, -1, 1) }),   // MushroomSink
        new CraftRecipe(4103, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 6) }),   // ToiletMushroom
        new CraftRecipe(2550, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 8) }),   // MushroomTable
        new CraftRecipe(814, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 10) }),   // MushroomWorkBench
        new CraftRecipe(2567, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 14) }),   // SlimeBathtub
        new CraftRecipe(2568, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 15),new CraftRequirement(225, -1, 5) }),   // SlimeBed
        new CraftRecipe(2569, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 20),new CraftRequirement(149, -1, 10) }),   // SlimeBookcase
        new CraftRecipe(2570, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 5),new CraftRequirement(8, -1, 3) }),   // SlimeCandelabra
        new CraftRecipe(2571, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 4),new CraftRequirement(8, -1, 1) }),   // SlimeCandle
        new CraftRecipe(2572, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 4) }),   // SlimeChair
        new CraftRecipe(2573, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // SlimeChandelier
        new CraftRecipe(2574, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 8),new CraftRequirement(22, 28, 2) }),   // SlimeChest
        new CraftRecipe(2575, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(762, -1, 10) }),   // SlimeClock
        new CraftRecipe(2576, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 6) }),   // SlimeDoor
        new CraftRecipe(2577, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 16) }),   // SlimeDresser
        new CraftRecipe(2578, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(762, -1, 3) }),   // SlimeLamp
        new CraftRecipe(2579, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 6),new CraftRequirement(8, -1, 1) }),   // SlimeLantern
        new CraftRecipe(2580, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(762, -1, 15),new CraftRequirement(149, -1, 1) }),   // SlimePiano
        new CraftRecipe(2582, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 5),new CraftRequirement(225, -1, 2) }),   // SlimeSofa
        new CraftRecipe(2853, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 6),new CraftRequirement(206, -1, 1) }),   // SlimeSink
        new CraftRecipe(4120, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 6) }),   // ToiletSlime
        new CraftRecipe(2583, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 8) }),   // SlimeTable
        new CraftRecipe(815, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 10) }),   // SlimeWorkBench
        new CraftRecipe(3159, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 14) }),   // MeteoriteBathtub
        new CraftRecipe(3162, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 15),new CraftRequirement(225, -1, 5) }),   // MeteoriteBed
        new CraftRecipe(3165, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 20),new CraftRequirement(149, -1, 10) }),   // MeteoriteBookcase
        new CraftRecipe(3168, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 5),new CraftRequirement(8, -1, 3) }),   // MeteoriteCandelabra
        new CraftRecipe(3171, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 4),new CraftRequirement(8, -1, 1) }),   // MeteoriteCandle
        new CraftRecipe(3174, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 4) }),   // MeteoriteChair
        new CraftRecipe(3177, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // MeteoriteChandelier
        new CraftRecipe(3180, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 8),new CraftRequirement(22, 28, 2) }),   // MeteoriteChest
        new CraftRecipe(3126, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(3100, -1, 10) }),   // MeteoriteClock
        new CraftRecipe(3129, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 6) }),   // MeteoriteDoor
        new CraftRecipe(3132, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 16) }),   // MeteoriteDresser
        new CraftRecipe(3135, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(3100, -1, 3) }),   // MeteoriteLamp
        new CraftRecipe(3138, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 6),new CraftRequirement(8, -1, 1) }),   // MeteoriteLantern
        new CraftRecipe(3141, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(3100, -1, 15),new CraftRequirement(149, -1, 1) }),   // MeteoritePiano
        new CraftRecipe(3150, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 5),new CraftRequirement(225, -1, 2) }),   // MeteoriteSofa
        new CraftRecipe(3147, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 6),new CraftRequirement(206, -1, 1) }),   // MeteoriteSink
        new CraftRecipe(4141, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 6) }),   // ToiletMeteor
        new CraftRecipe(3153, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 8) }),   // MeteoriteTable
        new CraftRecipe(3156, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3100, -1, 10) }),   // MeteoriteWorkBench
        new CraftRecipe(3066, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3081, -1, 1) }),   // MarbleBlock
        new CraftRecipe(3083, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 1) }),   // MarbleBlockWall
        new CraftRecipe(3082, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3081, -1, 1) }),   // MarbleWall
        new CraftRecipe(4554, 2, 106, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 1) }),   // MarbleColumn
        new CraftRecipe(3160, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 14) }),   // MarbleBathtub
        new CraftRecipe(3163, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 15),new CraftRequirement(225, -1, 5) }),   // MarbleBed
        new CraftRecipe(3166, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 20),new CraftRequirement(149, -1, 10) }),   // MarbleBookcase
        new CraftRecipe(3169, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 5),new CraftRequirement(8, -1, 3) }),   // MarbleCandelabra
        new CraftRecipe(3172, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 4),new CraftRequirement(8, -1, 1) }),   // MarbleCandle
        new CraftRecipe(3175, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 4) }),   // MarbleChair
        new CraftRecipe(3178, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // MarbleChandelier
        new CraftRecipe(3181, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 8),new CraftRequirement(22, 28, 2) }),   // MarbleChest
        new CraftRecipe(3127, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(3066, -1, 10) }),   // MarbleClock
        new CraftRecipe(3130, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 6) }),   // MarbleDoor
        new CraftRecipe(3133, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 16) }),   // MarbleDresser
        new CraftRecipe(3136, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(3066, -1, 3) }),   // MarbleLamp
        new CraftRecipe(3139, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 6),new CraftRequirement(8, -1, 1) }),   // MarbleLantern
        new CraftRecipe(3142, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(3066, -1, 15),new CraftRequirement(149, -1, 1) }),   // MarblePiano
        new CraftRecipe(3151, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 5),new CraftRequirement(225, -1, 2) }),   // MarbleSofa
        new CraftRecipe(3148, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 6),new CraftRequirement(206, -1, 1) }),   // MarbleSink
        new CraftRecipe(4123, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 6) }),   // ToiletMarble
        new CraftRecipe(3154, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 8) }),   // MarbleTable
        new CraftRecipe(3157, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3066, -1, 10) }),   // MarbleWorkBench
        new CraftRecipe(3087, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3086, -1, 1) }),   // GraniteBlock
        new CraftRecipe(3089, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 1) }),   // GraniteBlockWall
        new CraftRecipe(3088, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3086, -1, 1) }),   // GraniteWall
        new CraftRecipe(4719, 2, 106, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 1) }),   // GraniteColumn
        new CraftRecipe(3161, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 14) }),   // GraniteBathtub
        new CraftRecipe(3164, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 15),new CraftRequirement(225, -1, 5) }),   // GraniteBed
        new CraftRecipe(3167, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 20),new CraftRequirement(149, -1, 10) }),   // GraniteBookcase
        new CraftRecipe(3170, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 5),new CraftRequirement(8, -1, 3) }),   // GraniteCandelabra
        new CraftRecipe(3173, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 4),new CraftRequirement(8, -1, 1) }),   // GraniteCandle
        new CraftRecipe(3176, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 4) }),   // GraniteChair
        new CraftRecipe(3179, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // GraniteChandelier
        new CraftRecipe(3125, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 8),new CraftRequirement(22, 28, 2) }),   // GraniteChest
        new CraftRecipe(3128, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(3087, -1, 10) }),   // GraniteClock
        new CraftRecipe(3131, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 6) }),   // GraniteDoor
        new CraftRecipe(3134, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 16) }),   // GraniteDresser
        new CraftRecipe(3137, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(3087, -1, 3) }),   // GraniteLamp
        new CraftRecipe(3140, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 6),new CraftRequirement(8, -1, 1) }),   // GraniteLantern
        new CraftRecipe(3143, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(3087, -1, 15),new CraftRequirement(149, -1, 1) }),   // GranitePiano
        new CraftRecipe(3152, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 5),new CraftRequirement(225, -1, 2) }),   // GraniteSofa
        new CraftRecipe(3149, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 6),new CraftRequirement(206, -1, 1) }),   // GraniteSink
        new CraftRecipe(4122, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 6) }),   // ToiletGranite
        new CraftRecipe(3155, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 8) }),   // GraniteTable
        new CraftRecipe(3158, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3087, -1, 10) }),   // GraniteWorkBench
        new CraftRecipe(2810, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 14) }),   // MartianBathtub
        new CraftRecipe(2811, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 15),new CraftRequirement(225, -1, 5) }),   // MartianBed
        new CraftRecipe(2817, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 20),new CraftRequirement(149, -1, 10) }),   // MartianHolobookcase
        new CraftRecipe(2825, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 5),new CraftRequirement(8, -1, 3) }),   // MartianTableLamp
        new CraftRecipe(2818, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 4),new CraftRequirement(8, -1, 1) }),   // MartianHoverCandle
        new CraftRecipe(2812, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 4) }),   // MartianHoverChair
        new CraftRecipe(2813, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // MartianChandelier
        new CraftRecipe(2814, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 8),new CraftRequirement(22, 28, 2) }),   // MartianChest
        new CraftRecipe(2809, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(2860, -1, 10) }),   // MartianAstroClock
        new CraftRecipe(2815, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 6) }),   // MartianDoor
        new CraftRecipe(2816, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 16) }),   // MartianDresser
        new CraftRecipe(2819, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(2860, -1, 3) }),   // MartianLamppost
        new CraftRecipe(2820, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 6),new CraftRequirement(8, -1, 1) }),   // MartianLantern
        new CraftRecipe(2821, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(2860, -1, 15),new CraftRequirement(149, -1, 1) }),   // MartianPiano
        new CraftRecipe(2823, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 5),new CraftRequirement(225, -1, 2) }),   // MartianSofa
        new CraftRecipe(2855, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 6),new CraftRequirement(206, -1, 1) }),   // MartianSink
        new CraftRecipe(4121, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 6) }),   // ToiletMartian
        new CraftRecipe(2824, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 8) }),   // MartianTable
        new CraftRecipe(2826, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2860, -1, 10) }),   // MartianWorkBench
        new CraftRecipe(3895, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 35) }),   // CrystalBathtub
        new CraftRecipe(3897, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 40),new CraftRequirement(225, -1, 5) }),   // CrystalBed
        new CraftRecipe(3917, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 50),new CraftRequirement(149, -1, 10) }),   // CrystalBookCase
        new CraftRecipe(3893, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 10),new CraftRequirement(8, -1, 3) }),   // CrystalCandelabra
        new CraftRecipe(3890, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 8),new CraftRequirement(8, -1, 1) }),   // CrystalCandle
        new CraftRecipe(3889, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 10) }),   // CrystalChair
        new CraftRecipe(3894, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 10),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // CrystalChandelier
        new CraftRecipe(3884, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 20),new CraftRequirement(22, 28, 2) }),   // CrystalChest
        new CraftRecipe(3898, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(3234, -1, 25) }),   // CrystalClock
        new CraftRecipe(3888, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 15) }),   // CrystalDoor
        new CraftRecipe(3911, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 40) }),   // CrystalDresser
        new CraftRecipe(3892, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(3234, -1, 6) }),   // CrystalLamp
        new CraftRecipe(3891, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 15),new CraftRequirement(8, -1, 1) }),   // CrystalLantern
        new CraftRecipe(3915, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(3234, -1, 40),new CraftRequirement(149, -1, 1) }),   // CrystalPiano
        new CraftRecipe(4124, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 15) }),   // ToiletCrystal
        new CraftRecipe(3896, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 15),new CraftRequirement(206, -1, 1) }),   // CrystalSink
        new CraftRecipe(3920, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 20) }),   // CrystalTable
        new CraftRecipe(3909, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3234, -1, 25) }),   // CrystalWorkbench
        new CraftRecipe(5687, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(23, -1, 50) }),   // SlimeSpear
        new CraftRecipe(5688, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(23, -1, 50) }),   // SlimeWhip
        new CraftRecipe(171, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 6) }),   // Sign
        new CraftRecipe(4710, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 6) }),   // TatteredWoodSign
        new CraftRecipe(1447, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 1) }),   // WoodenFence
        new CraftRecipe(2210, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 1) }),   // EbonwoodFence
        new CraftRecipe(2211, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 1) }),   // RichMahoganyFence
        new CraftRecipe(2212, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 1) }),   // PearlwoodFence
        new CraftRecipe(2213, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 1) }),   // ShadewoodFence
        new CraftRecipe(2507, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 1) }),   // BorealWoodFence
        new CraftRecipe(2508, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 1) }),   // PalmWoodFence
        new CraftRecipe(5217, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 1) }),   // AshWoodFence
        new CraftRecipe(1448, 4, 16, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 1) }),   // LeadFence
        new CraftRecipe(2333, 4, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 1) }),   // IronFence
        new CraftRecipe(4424, 4, 283, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 1) }),   // WroughtIronFence
        new CraftRecipe(4667, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 1) }),   // BambooFence
        new CraftRecipe(3665, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(48, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_Chest
        new CraftRecipe(3666, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(306, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_GoldChest
        new CraftRecipe(3667, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(328, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_ShadowChest
        new CraftRecipe(3668, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(625, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_EbonwoodChest
        new CraftRecipe(3669, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(626, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_RichMahoganyChest
        new CraftRecipe(3670, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(627, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_PearlwoodChest
        new CraftRecipe(3671, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(680, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_IvyChest
        new CraftRecipe(3672, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(681, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_IceChest
        new CraftRecipe(3673, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(831, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_LivingWoodChest
        new CraftRecipe(3674, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(838, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_SkywareChest
        new CraftRecipe(3675, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(914, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_ShadewoodChest
        new CraftRecipe(3676, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(952, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_WebCoveredChest
        new CraftRecipe(3677, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1142, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_LihzahrdChest
        new CraftRecipe(3678, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1298, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_WaterChest
        new CraftRecipe(3679, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1528, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_JungleChest
        new CraftRecipe(3680, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1529, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_CorruptionChest
        new CraftRecipe(3681, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1530, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_CrimsonChest
        new CraftRecipe(3682, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1531, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_HallowedChest
        new CraftRecipe(3683, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1532, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_FrozenChest
        new CraftRecipe(3684, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2230, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_DynastyChest
        new CraftRecipe(3685, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2249, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_HoneyChest
        new CraftRecipe(3686, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2250, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_SteampunkChest
        new CraftRecipe(3687, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2526, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_PalmWoodChest
        new CraftRecipe(3688, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2544, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_MushroomChest
        new CraftRecipe(3689, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2559, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_BorealWoodChest
        new CraftRecipe(3690, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2574, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_SlimeChest
        new CraftRecipe(3691, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2612, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_GreenDungeonChest
        new CraftRecipe(3692, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2613, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_PinkDungeonChest
        new CraftRecipe(3693, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2614, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_BlueDungeonChest
        new CraftRecipe(3694, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2615, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_BoneChest
        new CraftRecipe(3695, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2616, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_CactusChest
        new CraftRecipe(3696, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2617, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_FleshChest
        new CraftRecipe(3697, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2618, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_ObsidianChest
        new CraftRecipe(3698, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2619, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_PumpkinChest
        new CraftRecipe(3699, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2620, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_SpookyChest
        new CraftRecipe(3700, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2748, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_GlassChest
        new CraftRecipe(3701, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(2814, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_MartianChest
        new CraftRecipe(3702, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3180, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_MeteoriteChest
        new CraftRecipe(3703, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3125, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_GraniteChest
        new CraftRecipe(3704, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3181, -1, 1),new CraftRequirement(530, -1, 10) }),   // Fake_MarbleChest
        new CraftRecipe(2340, 50, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 1),new CraftRequirement(9, 25, 1) }),   // MinecartTrack
        new CraftRecipe(2492, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2340, -1, 1),new CraftRequirement(542, 32, 1) }),   // PressureTrack
        new CraftRecipe(479, 4, 106, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1),new CraftRequirement(9, 25, 1) }),   // PlankedWall
        new CraftRecipe(480, 2, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 1) }),   // WoodenBeam
        new CraftRecipe(3202, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 20),new CraftRequirement(1727, -1, 50) }),   // TargetDummy
        new CraftRecipe(498, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 20) }),   // Mannequin
        new CraftRecipe(1989, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 20) }),   // Womannquin
        new CraftRecipe(3977, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 16) }),   // HatRack
        new CraftRecipe(2699, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10) }),   // WeaponRack
        new CraftRecipe(3270, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 6) }),   // ItemFrame
        new CraftRecipe(5137, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5132, -1, 1) }),   // StinkbugHousingBlocker
        new CraftRecipe(5138, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5132, -1, 1) }),   // StinkbugHousingBlockerEcho
        new CraftRecipe(343, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 9),new CraftRequirement(22, 28, 1) }),   // Barrel
        new CraftRecipe(359, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(9, -1, 10) }),   // GrandfatherClock
        new CraftRecipe(352, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 14) }),   // Keg
        new CraftRecipe(5008, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(133, -1, 12),new CraftRequirement(154, -1, 12) }),   // TeaKettle
        new CraftRecipe(332, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 12) }),   // Loom
        new CraftRecipe(2114, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 12),new CraftRequirement(22, 28, 3) }),   // BlacksmithRack
        new CraftRecipe(2115, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 12),new CraftRequirement(22, 28, 3) }),   // CarpentryRack
        new CraftRecipe(2116, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 12),new CraftRequirement(22, 28, 3) }),   // HelmetRack
        new CraftRecipe(2117, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 12),new CraftRequirement(22, 28, 3) }),   // SpearRack
        new CraftRecipe(2118, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 12),new CraftRequirement(22, 28, 3) }),   // SwordRack
        new CraftRecipe(1706, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 4) }),   // BarStool
        new CraftRecipe(1714, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 8) }),   // BanquetTable
        new CraftRecipe(1715, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 8) }),   // Bar
        new CraftRecipe(335, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 8) }),   // Bench
        new CraftRecipe(2397, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 5),new CraftRequirement(225, -1, 2) }),   // Sofa
        new CraftRecipe(363, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(22, 28, 2),new CraftRequirement(85, -1, 1) }),   // Sawmill
        new CraftRecipe(5012, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5011, -1, 1),new CraftRequirement(8, -1, 99) }),   // FlamingMace
        new CraftRecipe(6152, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(313, -1, 3),new CraftRequirement(23, -1, 5),new CraftRequirement(9, -1, 12) }),   // DaybloomStaff
        new CraftRecipe(6154, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(223, -1, 1),new CraftRequirement(314, -1, 2),new CraftRequirement(210, -1, 3),new CraftRequirement(620, -1, 7) }),   // Petalstorm
        new CraftRecipe(5147, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3069, -1, 1),new CraftRequirement(974, -1, 99) }),   // WandofFrosting
        new CraftRecipe(55, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(284, -1, 1),new CraftRequirement(75, -1, 1) }),   // EnchantedBoomerang
        new CraftRecipe(5298, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(55, -1, 1),new CraftRequirement(4764, -1, 1),new CraftRequirement(670, -1, 1) }),   // Trimarang
        new CraftRecipe(2289, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 8) }),   // WoodFishingPole
        new CraftRecipe(727, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 20) }),   // WoodHelmet
        new CraftRecipe(728, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 30) }),   // WoodBreastplate
        new CraftRecipe(729, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 25) }),   // WoodGreaves
        new CraftRecipe(24, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 7) }),   // WoodenSword
        new CraftRecipe(196, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 8) }),   // WoodenHammer
        new CraftRecipe(39, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 10) }),   // WoodenBow
        new CraftRecipe(3278, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 10),new CraftRequirement(150, -1, 20) }),   // WoodYoyo
        new CraftRecipe(3283, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3278, -1, 1),new CraftRequirement(502, -1, 15),new CraftRequirement(520, -1, 10) }),   // Chik
        new CraftRecipe(3315, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, 22, 6),new CraftRequirement(19, 30, 12),new CraftRequirement(521, -1, 8),new CraftRequirement(547, -1, 15) }),   // FormatC
        new CraftRecipe(733, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 20) }),   // RichMahoganyHelmet
        new CraftRecipe(734, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 30) }),   // RichMahoganyBreastplate
        new CraftRecipe(735, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 25) }),   // RichMahoganyGreaves
        new CraftRecipe(2509, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 20) }),   // BorealWoodHelmet
        new CraftRecipe(2510, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 30) }),   // BorealWoodBreastplate
        new CraftRecipe(2511, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 25) }),   // BorealWoodGreaves
        new CraftRecipe(2745, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 7) }),   // BorealWoodSword
        new CraftRecipe(2746, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 8) }),   // BorealWoodHammer
        new CraftRecipe(2747, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2503, -1, 10) }),   // BorealWoodBow
        new CraftRecipe(2512, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 20) }),   // PalmWoodHelmet
        new CraftRecipe(2513, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 30) }),   // PalmWoodBreastplate
        new CraftRecipe(2514, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 25) }),   // PalmWoodGreaves
        new CraftRecipe(2517, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 7) }),   // PalmWoodSword
        new CraftRecipe(2516, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 8) }),   // PalmWoodHammer
        new CraftRecipe(2515, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2504, -1, 10) }),   // PalmWoodBow
        new CraftRecipe(656, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 7) }),   // RichMahoganySword
        new CraftRecipe(657, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 8) }),   // RichMahoganyHammer
        new CraftRecipe(658, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 10) }),   // RichMahoganyBow
        new CraftRecipe(730, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 20) }),   // EbonwoodHelmet
        new CraftRecipe(731, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 30) }),   // EbonwoodBreastplate
        new CraftRecipe(732, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 25) }),   // EbonwoodGreaves
        new CraftRecipe(653, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 7) }),   // EbonwoodSword
        new CraftRecipe(654, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 8) }),   // EbonwoodHammer
        new CraftRecipe(655, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(619, -1, 10) }),   // EbonwoodBow
        new CraftRecipe(924, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 20) }),   // ShadewoodHelmet
        new CraftRecipe(925, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 30) }),   // ShadewoodBreastplate
        new CraftRecipe(926, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 25) }),   // ShadewoodGreaves
        new CraftRecipe(921, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 7) }),   // ShadewoodSword
        new CraftRecipe(922, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 8) }),   // ShadewoodHammer
        new CraftRecipe(923, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(911, -1, 10) }),   // ShadewoodBow
        new CraftRecipe(736, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 20) }),   // PearlwoodHelmet
        new CraftRecipe(737, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 30) }),   // PearlwoodBreastplate
        new CraftRecipe(738, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 25) }),   // PearlwoodGreaves
        new CraftRecipe(659, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 7) }),   // PearlwoodSword
        new CraftRecipe(660, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 8) }),   // PearlwoodHammer
        new CraftRecipe(661, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(621, -1, 10) }),   // PearlwoodBow
        new CraftRecipe(1832, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 200) }),   // SpookyHelmet
        new CraftRecipe(1833, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 300) }),   // SpookyBreastplate
        new CraftRecipe(1834, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 250) }),   // SpookyLeggings
        new CraftRecipe(2263, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 1) }),   // WhiteDynastyWall
        new CraftRecipe(2264, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 1) }),   // BlueDynastyWall
        new CraftRecipe(2265, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 6) }),   // DynastyDoor
        new CraftRecipe(2228, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 4) }),   // DynastyChair
        new CraftRecipe(2230, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 8),new CraftRequirement(22, 28, 2) }),   // DynastyChest
        new CraftRecipe(3916, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(2260, -1, 15),new CraftRequirement(149, -1, 1) }),   // DynastyPiano
        new CraftRecipe(3912, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 16) }),   // DynastyDresser
        new CraftRecipe(3919, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 5),new CraftRequirement(225, -1, 2) }),   // DynastySofa
        new CraftRecipe(2849, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 6),new CraftRequirement(206, -1, 1) }),   // DynastySink
        new CraftRecipe(4117, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 6) }),   // ToiletDynasty
        new CraftRecipe(2259, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 8) }),   // DynastyTable
        new CraftRecipe(2229, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 10) }),   // DynastyWorkBench
        new CraftRecipe(2231, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 15),new CraftRequirement(225, -1, 5) }),   // DynastyBed
        new CraftRecipe(2237, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(2260, -1, 10) }),   // DynastyClock
        new CraftRecipe(2233, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 20),new CraftRequirement(149, -1, 10) }),   // DynastyBookcase
        new CraftRecipe(2232, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 14),new CraftRequirement(22, 28, 2) }),   // DynastyBathtub
        new CraftRecipe(2226, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 6),new CraftRequirement(8, -1, 1) }),   // DynastyLantern
        new CraftRecipe(2236, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 4),new CraftRequirement(8, -1, 1) }),   // DynastyCandle
        new CraftRecipe(2224, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 4),new CraftRequirement(8, -1, 4) }),   // DynastyChandelier
        new CraftRecipe(2225, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(2260, -1, 3) }),   // DynastyLamp
        new CraftRecipe(2227, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 5),new CraftRequirement(8, -1, 3) }),   // DynastyCandelabra
        new CraftRecipe(2235, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 2) }),   // DynastyBowl
        new CraftRecipe(2234, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2260, -1, 2) }),   // DynastyCup
        new CraftRecipe(2596, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(9, -1, 10) }),   // LivingWoodClock
        new CraftRecipe(806, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 4) }),   // LivingWoodChair
        new CraftRecipe(831, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 8),new CraftRequirement(22, 28, 2) }),   // LivingWoodChest
        new CraftRecipe(819, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 6) }),   // LivingWoodDoor
        new CraftRecipe(3914, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 16) }),   // LivingWoodDresser
        new CraftRecipe(2833, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 6),new CraftRequirement(206, -1, 1) }),   // LivingWoodSink
        new CraftRecipe(829, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 8) }),   // LivingWoodTable
        new CraftRecipe(2139, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 15),new CraftRequirement(225, -1, 5) }),   // LivingWoodBed
        new CraftRecipe(2245, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(9, -1, 15),new CraftRequirement(149, -1, 1) }),   // LivingWoodPiano
        new CraftRecipe(2135, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 20),new CraftRequirement(149, -1, 10) }),   // LivingWoodBookcase
        new CraftRecipe(2126, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 14) }),   // LivingWoodBathtub
        new CraftRecipe(2145, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 6),new CraftRequirement(8, -1, 1) }),   // LivingWoodLantern
        new CraftRecipe(2153, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 4),new CraftRequirement(8, -1, 1) }),   // LivingWoodCandle
        new CraftRecipe(2141, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // LivingWoodChandelier
        new CraftRecipe(2131, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(9, -1, 3) }),   // LivingWoodLamp
        new CraftRecipe(2149, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 5),new CraftRequirement(8, -1, 3) }),   // LivingWoodCandelabra
        new CraftRecipe(2636, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 5),new CraftRequirement(225, -1, 2) }),   // LivingWoodSofa
        new CraftRecipe(2633, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 10) }),   // LivingWoodWorkBench
        new CraftRecipe(4099, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(9, -1, 6) }),   // ToiletLivingWood
        new CraftRecipe(2239, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 16) }),   // GlassClock
        new CraftRecipe(1703, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 4) }),   // GlassChair
        new CraftRecipe(2748, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 8),new CraftRequirement(22, 28, 2) }),   // GlassChest
        new CraftRecipe(1709, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 6) }),   // GlassDoor
        new CraftRecipe(2639, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 16) }),   // GlassDresser
        new CraftRecipe(2842, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 6),new CraftRequirement(206, -1, 1) }),   // GlassSink
        new CraftRecipe(1713, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 8) }),   // GlassTable
        new CraftRecipe(1719, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 15),new CraftRequirement(225, -1, 5) }),   // GlassBed
        new CraftRecipe(2254, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(170, -1, 15),new CraftRequirement(149, -1, 1) }),   // GlassPiano
        new CraftRecipe(2025, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 20),new CraftRequirement(149, -1, 10) }),   // GlassBookcase
        new CraftRecipe(2075, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 14) }),   // GlassBathtub
        new CraftRecipe(2037, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 6),new CraftRequirement(8, -1, 1) }),   // GlassLantern
        new CraftRecipe(2048, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 4),new CraftRequirement(8, -1, 1) }),   // GlassCandle
        new CraftRecipe(2065, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // GlassChandelier
        new CraftRecipe(2085, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(170, -1, 3) }),   // GlassLamp
        new CraftRecipe(2097, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 5),new CraftRequirement(8, -1, 3) }),   // GlassCandelabra
        new CraftRecipe(2414, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 5),new CraftRequirement(225, -1, 2) }),   // GlassSofa
        new CraftRecipe(4112, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 6) }),   // ToiletGlass
        new CraftRecipe(2632, 1, 302, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 10) }),   // GlassWorkBench
        new CraftRecipe(1127, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 1) }),   // CrispyHoneyBlock
        new CraftRecipe(1707, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 4) }),   // HoneyChair
        new CraftRecipe(1711, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 6) }),   // HoneyDoor
        new CraftRecipe(2844, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 6),new CraftRequirement(206, -1, 1) }),   // HoneySink
        new CraftRecipe(1717, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 8) }),   // HoneyTable
        new CraftRecipe(2251, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 10) }),   // HoneyWorkBench
        new CraftRecipe(1721, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 15),new CraftRequirement(225, -1, 5) }),   // HoneyBed
        new CraftRecipe(2255, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(1125, -1, 15),new CraftRequirement(149, -1, 1) }),   // HoneyPiano
        new CraftRecipe(2124, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 14) }),   // HoneyBathtub
        new CraftRecipe(2249, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 8),new CraftRequirement(22, 28, 2) }),   // HoneyChest
        new CraftRecipe(2240, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(1125, -1, 10) }),   // HoneyClock
        new CraftRecipe(2023, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 20),new CraftRequirement(149, -1, 10) }),   // HoneyBookcase
        new CraftRecipe(2035, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 6),new CraftRequirement(8, -1, 1) }),   // HoneyLantern
        new CraftRecipe(2648, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 4),new CraftRequirement(8, -1, 1) }),   // HoneyCandle
        new CraftRecipe(2058, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // HoneyChandelier
        new CraftRecipe(2095, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 5),new CraftRequirement(8, -1, 3) }),   // HoneyCandelabra
        new CraftRecipe(2129, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(1125, -1, 3) }),   // HoneyLamp
        new CraftRecipe(2257, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 1) }),   // HoneyCup
        new CraftRecipe(2395, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 16) }),   // HoneyDresser
        new CraftRecipe(2411, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 5),new CraftRequirement(225, -1, 2) }),   // HoneySofa
        new CraftRecipe(4113, 1, 308, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 6) }),   // ToiletHoney
        new CraftRecipe(824, 25, 305, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 25),new CraftRequirement(75, -1, 1) }),   // SunplateBlock
        new CraftRecipe(825, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 1) }),   // DiscWall
        new CraftRecipe(826, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 4) }),   // SkywareChair
        new CraftRecipe(2606, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(824, -1, 10) }),   // SkywareClock
        new CraftRecipe(838, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 8),new CraftRequirement(22, 28, 2) }),   // SkywareChest
        new CraftRecipe(3899, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(824, -1, 15) }),   // SkywareClock2
        new CraftRecipe(837, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 6) }),   // SkywareDoor
        new CraftRecipe(2834, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 6),new CraftRequirement(206, -1, 1) }),   // SkywareSink
        new CraftRecipe(830, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 8) }),   // SkywareTable
        new CraftRecipe(2029, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 20),new CraftRequirement(149, -1, 10) }),   // SkywareBookcase
        new CraftRecipe(2070, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 15),new CraftRequirement(225, -1, 5) }),   // SkywareBed
        new CraftRecipe(2080, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 14) }),   // SkywareBathtub
        new CraftRecipe(2042, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 6),new CraftRequirement(8, -1, 1) }),   // SkywareLantern
        new CraftRecipe(2053, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 4),new CraftRequirement(8, -1, 1) }),   // SkywareCandle
        new CraftRecipe(2063, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // SkywareChandelier
        new CraftRecipe(2090, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(824, -1, 3) }),   // SkywareLamp
        new CraftRecipe(2102, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 5),new CraftRequirement(8, -1, 3) }),   // SkywareCandelabra
        new CraftRecipe(2384, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(824, -1, 15),new CraftRequirement(149, -1, 1) }),   // SkywarePiano
        new CraftRecipe(2394, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 16) }),   // SkywareDresser
        new CraftRecipe(2410, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 5),new CraftRequirement(225, -1, 2) }),   // SkywareSofa
        new CraftRecipe(2631, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 10) }),   // SkywareWorkbench
        new CraftRecipe(4104, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(824, -1, 6) }),   // ToiletSunplate
        new CraftRecipe(765, 1, 305, CraftEnvironment.Water, new[] { new CraftRequirement(751, -1, 1) }),   // RainCloud
        new CraftRecipe(3756, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 1) }),   // SnowCloudBlock
        new CraftRecipe(5569, 1, 305, CraftEnvironment.Lava, new[] { new CraftRequirement(751, -1, 1) }),   // LavaCloud
        new CraftRecipe(5570, 10, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 10),new CraftRequirement(75, -1, 1) }),   // StarCloud
        new CraftRecipe(5571, 10, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 10),new CraftRequirement(662, -1, 1) }),   // RainbowCloud
        new CraftRecipe(2595, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(1101, -1, 10) }),   // LihzahrdClock
        new CraftRecipe(1143, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 4) }),   // LihzahrdChair
        new CraftRecipe(1137, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 6) }),   // LihzahrdDoor
        new CraftRecipe(2836, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 6),new CraftRequirement(206, -1, 1) }),   // LihzahrdSink
        new CraftRecipe(1144, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 8) }),   // LihzahrdTable
        new CraftRecipe(1142, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 8),new CraftRequirement(22, 28, 2) }),   // LihzahrdChest
        new CraftRecipe(1145, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 10) }),   // LihzahrdWorkBench
        new CraftRecipe(2030, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 20),new CraftRequirement(149, -1, 10) }),   // LihzahrdBookcase
        new CraftRecipe(2069, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 15),new CraftRequirement(225, -1, 5) }),   // LihzahrdBed
        new CraftRecipe(2079, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 14) }),   // LihzahrdBathtub
        new CraftRecipe(2041, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 6),new CraftRequirement(8, -1, 1) }),   // LihzahrdLantern
        new CraftRecipe(2052, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 4),new CraftRequirement(8, -1, 1) }),   // LihzahrdCandle
        new CraftRecipe(2062, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // LihzahrdChandelier
        new CraftRecipe(2089, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(1101, -1, 3) }),   // LihzahrdLamp
        new CraftRecipe(2101, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 5),new CraftRequirement(8, -1, 3) }),   // LihzahrdCandelabra
        new CraftRecipe(2385, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(1101, -1, 15),new CraftRequirement(149, -1, 1) }),   // LihzahrdPiano
        new CraftRecipe(2396, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 16) }),   // LihzahrdDresser
        new CraftRecipe(2416, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 5),new CraftRequirement(225, -1, 2) }),   // LihzahrdSofa
        new CraftRecipe(4106, 1, 303, CraftEnvironment.None, new[] { new CraftRequirement(1101, -1, 6) }),   // ToiletLihzhard
        new CraftRecipe(2848, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 6),new CraftRequirement(206, -1, 1) }),   // FrozenSink
        new CraftRecipe(2248, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 8) }),   // FrozenTable
        new CraftRecipe(2635, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 5),new CraftRequirement(225, -1, 2) }),   // FrozenSofa
        new CraftRecipe(2252, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 10) }),   // FrozenWorkBench
        new CraftRecipe(2031, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 20),new CraftRequirement(149, -1, 10) }),   // FrozenBookcase
        new CraftRecipe(2247, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(664, -1, 15),new CraftRequirement(149, -1, 1) }),   // FrozenPiano
        new CraftRecipe(2068, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 15),new CraftRequirement(225, -1, 5) }),   // FrozenBed
        new CraftRecipe(2076, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 14) }),   // FrozenBathtub
        new CraftRecipe(681, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 8),new CraftRequirement(22, 28, 2) }),   // IceChest
        new CraftRecipe(2594, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(664, -1, 10) }),   // FrozenClock
        new CraftRecipe(2044, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 6) }),   // FrozenDoor
        new CraftRecipe(3913, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 16) }),   // FrozenDresser
        new CraftRecipe(2040, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 6),new CraftRequirement(974, -1, 1) }),   // FrozenLantern
        new CraftRecipe(2049, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 4),new CraftRequirement(974, -1, 1) }),   // FrozenCandle
        new CraftRecipe(2059, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 4),new CraftRequirement(974, -1, 4),new CraftRequirement(85, -1, 1) }),   // FrozenChandelier
        new CraftRecipe(2086, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(974, -1, 1),new CraftRequirement(664, -1, 3) }),   // FrozenLamp
        new CraftRecipe(2100, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 5),new CraftRequirement(974, -1, 3) }),   // FrozenCandelabra
        new CraftRecipe(2288, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 4) }),   // FrozenChair
        new CraftRecipe(4111, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 6) }),   // ToiletFrozen
        new CraftRecipe(1708, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 4) }),   // SteampunkChair
        new CraftRecipe(2649, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 4),new CraftRequirement(8, -1, 1) }),   // SteampunkCandle
        new CraftRecipe(2655, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // SteampunkChandelier
        new CraftRecipe(1712, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 6) }),   // SteampunkDoor
        new CraftRecipe(2638, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 16) }),   // SteampunkDresser
        new CraftRecipe(2845, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 6),new CraftRequirement(206, -1, 1) }),   // SteampunkSink
        new CraftRecipe(1718, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 8) }),   // SteampunkTable
        new CraftRecipe(2253, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 10) }),   // SteampunkWorkBench
        new CraftRecipe(1722, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 15),new CraftRequirement(225, -1, 5) }),   // SteampunkBed
        new CraftRecipe(2256, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(1344, -1, 15),new CraftRequirement(149, -1, 1) }),   // SteampunkPiano
        new CraftRecipe(2125, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 14) }),   // SteampunkBathtub
        new CraftRecipe(2250, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 8),new CraftRequirement(22, 28, 2) }),   // SteampunkChest
        new CraftRecipe(2241, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(1344, -1, 10) }),   // SteampunkClock
        new CraftRecipe(2024, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 20),new CraftRequirement(149, -1, 10) }),   // SteampunkBookcase
        new CraftRecipe(2036, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 6),new CraftRequirement(8, -1, 1) }),   // SteampunkLantern
        new CraftRecipe(2096, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 5),new CraftRequirement(8, -1, 3) }),   // SteampunkCandelabra
        new CraftRecipe(2130, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(1344, -1, 3) }),   // SteampunkLamp
        new CraftRecipe(2412, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 5),new CraftRequirement(225, -1, 2) }),   // SteampunkSofa
        new CraftRecipe(4114, 1, 307, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 6) }),   // ToiletSteampunk
        new CraftRecipe(894, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 20) }),   // CactusHelmet
        new CraftRecipe(895, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 30) }),   // CactusBreastplate
        new CraftRecipe(896, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 25) }),   // CactusLeggings
        new CraftRecipe(881, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 10) }),   // CactusSword
        new CraftRecipe(882, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 15) }),   // CactusPickaxe
        new CraftRecipe(750, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 1) }),   // CactusWall
        new CraftRecipe(816, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 6) }),   // CactusDoor
        new CraftRecipe(807, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 4) }),   // CactusChair
        new CraftRecipe(2616, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 8),new CraftRequirement(22, 28, 2) }),   // CactusChest
        new CraftRecipe(2592, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(276, -1, 10) }),   // CactusClock
        new CraftRecipe(812, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 10) }),   // CactusWorkBench
        new CraftRecipe(2020, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 20),new CraftRequirement(149, -1, 10) }),   // CactusBookcase
        new CraftRecipe(2066, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 15),new CraftRequirement(225, -1, 5) }),   // CactusBed
        new CraftRecipe(2072, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 14) }),   // CactusBathtub
        new CraftRecipe(2032, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 6),new CraftRequirement(8, -1, 1) }),   // CactusLantern
        new CraftRecipe(2045, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 4),new CraftRequirement(8, -1, 1) }),   // CactusCandle
        new CraftRecipe(2055, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // CactusChandelier
        new CraftRecipe(2082, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(276, -1, 3) }),   // CactusLamp
        new CraftRecipe(2092, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 5),new CraftRequirement(8, -1, 3) }),   // CactusCandelabra
        new CraftRecipe(2382, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(276, -1, 15),new CraftRequirement(149, -1, 1) }),   // CactusPiano
        new CraftRecipe(2392, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 16) }),   // CactusDresser
        new CraftRecipe(2408, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 5),new CraftRequirement(225, -1, 2) }),   // CactusSofa
        new CraftRecipe(2854, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 6),new CraftRequirement(206, -1, 1) }),   // CactusSink
        new CraftRecipe(2743, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 8) }),   // CactusTable
        new CraftRecipe(4100, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 6) }),   // ToiletCactus
        new CraftRecipe(2661, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 14) }),   // PumpkinBathtub
        new CraftRecipe(2669, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 15),new CraftRequirement(225, -1, 5) }),   // PumpkinBed
        new CraftRecipe(2670, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 20),new CraftRequirement(149, -1, 10) }),   // PumpkinBookcase
        new CraftRecipe(2668, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 5),new CraftRequirement(8, -1, 3) }),   // PumpkinCandelabra
        new CraftRecipe(2603, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(1725, -1, 10) }),   // PumpkinClock
        new CraftRecipe(1793, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 6) }),   // PumpkinDoor
        new CraftRecipe(2637, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 16) }),   // PumpkinDresser
        new CraftRecipe(1792, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 4) }),   // PumpkinChair
        new CraftRecipe(2656, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // PumpkinChandelier
        new CraftRecipe(2619, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 8),new CraftRequirement(22, 28, 2) }),   // PumpkinChest
        new CraftRecipe(2671, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(1725, -1, 15),new CraftRequirement(149, -1, 1) }),   // PumpkinPiano
        new CraftRecipe(2846, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 6),new CraftRequirement(206, -1, 1) }),   // PumpkinSink
        new CraftRecipe(1794, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 8) }),   // PumpkinTable
        new CraftRecipe(1795, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 10) }),   // PumpkinWorkBench
        new CraftRecipe(1813, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 6),new CraftRequirement(8, -1, 1) }),   // JackOLantern
        new CraftRecipe(2643, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(1725, -1, 3) }),   // PumpkinLamp
        new CraftRecipe(2641, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 6),new CraftRequirement(8, -1, 1) }),   // PumpkinLantern
        new CraftRecipe(1808, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 4),new CraftRequirement(8, -1, 1) }),   // HangingJackOLantern
        new CraftRecipe(2054, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 4),new CraftRequirement(8, -1, 1) }),   // PumpkinCandle
        new CraftRecipe(4115, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 6) }),   // ToiletPumpkin
        new CraftRecipe(1812, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 12),new CraftRequirement(8, -1, 4) }),   // Jackelier
        new CraftRecipe(2415, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 5),new CraftRequirement(225, -1, 2) }),   // PumpkinSofa
        new CraftRecipe(1731, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 20) }),   // PumpkinHelmet
        new CraftRecipe(1732, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 30) }),   // PumpkinBreastplate
        new CraftRecipe(1733, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 25) }),   // PumpkinLeggings
        new CraftRecipe(2605, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(1729, -1, 10) }),   // SpookyClock
        new CraftRecipe(1815, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 6) }),   // SpookyDoor
        new CraftRecipe(1814, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 4) }),   // SpookyChair
        new CraftRecipe(2620, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 8),new CraftRequirement(22, 28, 2) }),   // SpookyChest
        new CraftRecipe(2847, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 6),new CraftRequirement(206, -1, 1) }),   // SpookySink
        new CraftRecipe(1816, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 8) }),   // SpookyTable
        new CraftRecipe(1817, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 10) }),   // SpookyWorkBench
        new CraftRecipe(2028, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 20),new CraftRequirement(149, -1, 10) }),   // SpookyBookcase
        new CraftRecipe(2071, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 15),new CraftRequirement(225, -1, 5) }),   // SpookyBed
        new CraftRecipe(2081, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 14) }),   // SpookyBathtub
        new CraftRecipe(2043, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 6),new CraftRequirement(8, -1, 1) }),   // SpookyLantern
        new CraftRecipe(2650, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 4),new CraftRequirement(8, -1, 1) }),   // SpookyCandle
        new CraftRecipe(2064, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // SpookyChandelier
        new CraftRecipe(2091, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(1729, -1, 3) }),   // SpookyLamp
        new CraftRecipe(2103, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 5),new CraftRequirement(8, -1, 3) }),   // SpookyCandelabra
        new CraftRecipe(2383, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(1729, -1, 15),new CraftRequirement(149, -1, 1) }),   // SpookyPiano
        new CraftRecipe(2393, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 16) }),   // SpookyDresser
        new CraftRecipe(2409, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 5),new CraftRequirement(225, -1, 2) }),   // SpookySofa
        new CraftRecipe(4116, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1729, -1, 6) }),   // ToiletSpooky
        new CraftRecipe(763, 1, 218, CraftEnvironment.None, new[] { new CraftRequirement(836, -1, 2) }),   // FleshBlock
        new CraftRecipe(770, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 1) }),   // FleshBlockWall
        new CraftRecipe(2598, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(763, -1, 10) }),   // FleshClock
        new CraftRecipe(817, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 6) }),   // FleshDoor
        new CraftRecipe(2640, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 16) }),   // FleshDresser
        new CraftRecipe(809, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 4) }),   // FleshChair
        new CraftRecipe(2617, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 8),new CraftRequirement(22, 28, 2) }),   // FleshChest
        new CraftRecipe(813, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 10) }),   // FleshWorkBench
        new CraftRecipe(2832, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 6),new CraftRequirement(206, -1, 1) }),   // FleshSink
        new CraftRecipe(828, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 8) }),   // FleshTable
        new CraftRecipe(2246, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(763, -1, 15),new CraftRequirement(149, -1, 1) }),   // FleshPiano
        new CraftRecipe(2022, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 20),new CraftRequirement(149, -1, 10) }),   // FleshBookcase
        new CraftRecipe(2067, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 15),new CraftRequirement(225, -1, 5) }),   // FleshBed
        new CraftRecipe(2074, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 14) }),   // FleshBathtub
        new CraftRecipe(2034, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 6),new CraftRequirement(8, -1, 1) }),   // FleshLantern
        new CraftRecipe(2047, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 4),new CraftRequirement(8, -1, 1) }),   // FleshCandle
        new CraftRecipe(2057, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // FleshChandelier
        new CraftRecipe(2084, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(763, -1, 3) }),   // FleshLamp
        new CraftRecipe(2094, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 5),new CraftRequirement(8, -1, 3) }),   // FleshCandelabra
        new CraftRecipe(2634, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 5),new CraftRequirement(225, -1, 2) }),   // FleshSofa
        new CraftRecipe(4102, 1, 301, CraftEnvironment.None, new[] { new CraftRequirement(763, -1, 6) }),   // ToiletFlesh
        new CraftRecipe(762, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(23, -1, 1) }),   // SlimeBlock
        new CraftRecipe(769, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 1) }),   // SlimeBlockWall
        new CraftRecipe(767, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(762, -1, 1),new CraftRequirement(664, -1, 1) }),   // FrozenSlimeBlock
        new CraftRecipe(3113, 1, 220, CraftEnvironment.None, new[] { new CraftRequirement(3111, -1, 1) }),   // PinkSlimeBlock
        new CraftRecipe(1126, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(1124, -1, 1) }),   // HiveWall
        new CraftRecipe(768, 4, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 1) }),   // BoneBlockWall
        new CraftRecipe(820, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 6) }),   // BoneDoor
        new CraftRecipe(2615, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 8),new CraftRequirement(22, 28, 2) }),   // BoneChest
        new CraftRecipe(2591, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6),new CraftRequirement(154, -1, 10) }),   // BoneClock
        new CraftRecipe(808, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4) }),   // BoneChair
        new CraftRecipe(811, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 10) }),   // BoneWorkBench
        new CraftRecipe(2831, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 6),new CraftRequirement(206, -1, 1) }),   // BoneSink
        new CraftRecipe(827, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 8) }),   // BoneTable
        new CraftRecipe(2138, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 20),new CraftRequirement(149, -1, 10) }),   // BoneBookcase
        new CraftRecipe(2140, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 15),new CraftRequirement(225, -1, 5) }),   // BoneBed
        new CraftRecipe(2128, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 14) }),   // BoneBathtub
        new CraftRecipe(2144, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // BoneChandelier
        new CraftRecipe(2152, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 5),new CraftRequirement(8, -1, 3) }),   // BoneCandelabra
        new CraftRecipe(2134, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(154, -1, 3) }),   // BoneLamp
        new CraftRecipe(2148, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 6),new CraftRequirement(8, -1, 1) }),   // BoneLantern
        new CraftRecipe(2381, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 19),new CraftRequirement(149, -1, 1) }),   // BonePiano
        new CraftRecipe(2391, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 16) }),   // BoneDresser
        new CraftRecipe(4101, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 6) }),   // ToiletBone
        new CraftRecipe(2407, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 5),new CraftRequirement(225, -1, 2) }),   // BoneSofa
        new CraftRecipe(2618, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(173, -1, 6),new CraftRequirement(174, -1, 2),new CraftRequirement(22, 28, 2) }),   // ObsidianChest
        new CraftRecipe(2840, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(173, -1, 4),new CraftRequirement(174, -1, 2),new CraftRequirement(206, -1, 1) }),   // ObsidianSink
        new CraftRecipe(4110, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(173, -1, 4),new CraftRequirement(174, -1, 2) }),   // ToiletObsidian
        new CraftRecipe(2613, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(139, -1, 8),new CraftRequirement(22, 28, 2) }),   // PinkDungeonChest
        new CraftRecipe(2839, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(139, -1, 6),new CraftRequirement(206, -1, 1) }),   // PinkDungeonSink
        new CraftRecipe(4109, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(139, -1, 6) }),   // ToiletDungeonPink
        new CraftRecipe(2614, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(134, -1, 8),new CraftRequirement(22, 28, 2) }),   // BlueDungeonChest
        new CraftRecipe(2837, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(134, -1, 6),new CraftRequirement(206, -1, 1) }),   // BlueDungeonSink
        new CraftRecipe(4107, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(134, -1, 6) }),   // ToiletDungeonBlue
        new CraftRecipe(2612, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(137, -1, 8),new CraftRequirement(22, 28, 2) }),   // GreenDungeonChest
        new CraftRecipe(2838, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(137, -1, 6),new CraftRequirement(206, -1, 1) }),   // GreenDungeonSink
        new CraftRecipe(4108, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(137, -1, 6) }),   // ToiletDungeonGreen
        new CraftRecipe(361, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(362, -1, 10),new CraftRequirement(9, 25, 5) }),   // GoblinBattleStandard
        new CraftRecipe(225, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(150, -1, 7) }),   // Silk
        new CraftRecipe(337, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 3) }),   // RedBanner
        new CraftRecipe(338, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 3) }),   // GreenBanner
        new CraftRecipe(339, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 3) }),   // BlueBanner
        new CraftRecipe(340, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 3) }),   // YellowBanner
        new CraftRecipe(5497, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 3) }),   // PinkBanner
        new CraftRecipe(5498, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 3) }),   // WhiteBanner
        new CraftRecipe(255, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(195, -1, 3) }),   // GreenThread
        new CraftRecipe(247, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(255, -1, 3) }),   // HerosHat
        new CraftRecipe(248, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(255, -1, 3) }),   // HerosShirt
        new CraftRecipe(249, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(255, -1, 3) }),   // HerosPants
        new CraftRecipe(3773, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 15),new CraftRequirement(3794, -1, 5) }),   // AncientArmorHat
        new CraftRecipe(3774, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 15),new CraftRequirement(3794, -1, 5) }),   // AncientArmorShirt
        new CraftRecipe(3775, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 15),new CraftRequirement(3794, -1, 5) }),   // AncientArmorPants
        new CraftRecipe(240, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(254, -1, 3) }),   // TuxedoShirt
        new CraftRecipe(241, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(254, -1, 3) }),   // TuxedoPants
        new CraftRecipe(4132, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(981, -1, 3) }),   // MaidHead2
        new CraftRecipe(4133, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(981, -1, 3) }),   // MaidShirt2
        new CraftRecipe(4134, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(981, -1, 3) }),   // MaidPants2
        new CraftRecipe(4128, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(254, -1, 3) }),   // MaidHead
        new CraftRecipe(4129, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(254, -1, 3) }),   // MaidShirt
        new CraftRecipe(4130, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(254, -1, 3) }),   // MaidPants
        new CraftRecipe(4652, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(255, -1, 3) }),   // SuperHeroMask
        new CraftRecipe(4653, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(255, -1, 3) }),   // SuperHeroCostume
        new CraftRecipe(4654, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(255, -1, 3) }),   // SuperHeroTights
        new CraftRecipe(5045, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(1330, -1, 10),new CraftRequirement(154, -1, 10) }),   // PlaguebringerHelmet
        new CraftRecipe(5046, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(1330, -1, 10),new CraftRequirement(1050, -1, 1) }),   // PlaguebringerChestplate
        new CraftRecipe(5047, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(1330, -1, 10),new CraftRequirement(1050, -1, 1) }),   // PlaguebringerGreaves
        new CraftRecipe(5048, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(133, -1, 10),new CraftRequirement(1992, -1, 3) }),   // RoninHat
        new CraftRecipe(5049, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(1992, -1, 3) }),   // RoninShirt
        new CraftRecipe(5050, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(1992, -1, 3) }),   // RoninPants
        new CraftRecipe(5051, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 10),new CraftRequirement(154, -1, 10) }),   // TimelessTravelerHood
        new CraftRecipe(5052, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(68, -1, 10),new CraftRequirement(1050, -1, 1) }),   // TimelessTravelerRobe
        new CraftRecipe(5053, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(68, -1, 10),new CraftRequirement(1050, -1, 1) }),   // TimelessTravelerBottom
        new CraftRecipe(5054, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 20),new CraftRequirement(2, -1, 10),new CraftRequirement(313, -1, 1) }),   // FloretProtectorHelmet
        new CraftRecipe(5055, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(2, -1, 15) }),   // FloretProtectorChestplate
        new CraftRecipe(5056, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(2, -1, 15) }),   // FloretProtectorLegs
        new CraftRecipe(5057, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(275, -1, 15),new CraftRequirement(75, -1, 5) }),   // CapricornMask
        new CraftRecipe(5058, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(75, -1, 5),new CraftRequirement(1037, -1, 1) }),   // CapricornChestplate
        new CraftRecipe(5060, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(75, -1, 5),new CraftRequirement(1037, -1, 1) }),   // CapricornTail
        new CraftRecipe(5061, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 10),new CraftRequirement(530, -1, 10) }),   // TVHeadMask
        new CraftRecipe(5062, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(1016, -1, 1) }),   // TVHeadSuit
        new CraftRecipe(5063, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(1016, -1, 1) }),   // TVHeadPants
        new CraftRecipe(5102, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20) }),   // WilsonShirt
        new CraftRecipe(5103, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20) }),   // WilsonPants
        new CraftRecipe(5115, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20) }),   // WillowShirt
        new CraftRecipe(5116, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20) }),   // WillowSkirt
        new CraftRecipe(5457, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(521, -1, 10),new CraftRequirement(5322, -1, 1),new CraftRequirement(68, -1, 10) }),   // DeadCellsBeheadedHead
        new CraftRecipe(5457, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(521, -1, 10),new CraftRequirement(5322, -1, 1),new CraftRequirement(1330, -1, 10) }),   // DeadCellsBeheadedHead
        new CraftRecipe(5458, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(3794, -1, 5),new CraftRequirement(216, -1, 1) }),   // DeadCellsBeheadedBody
        new CraftRecipe(5459, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(3794, -1, 5),new CraftRequirement(362, -1, 5) }),   // DeadCellsBeheadedLegs
        new CraftRecipe(5646, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20) }),   // BlueBikiniBody
        new CraftRecipe(5647, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20) }),   // BlueBikiniLegs
        new CraftRecipe(5648, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20) }),   // RedSwimsuit
        new CraftRecipe(5649, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20) }),   // GreenSwimshorts
        new CraftRecipe(5650, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20) }),   // GraySwimshorts
        new CraftRecipe(262, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20) }),   // Robe
        new CraftRecipe(3266, 1, 77, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 10),new CraftRequirement(173, -1, 20),new CraftRequirement(86, -1, 5) }),   // ObsidianHelm
        new CraftRecipe(3267, 1, 77, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 10),new CraftRequirement(173, -1, 20),new CraftRequirement(86, -1, 10) }),   // ObsidianShirt
        new CraftRecipe(3268, 1, 77, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 10),new CraftRequirement(173, -1, 20),new CraftRequirement(86, -1, 5) }),   // ObsidianPants
        new CraftRecipe(3266, 1, 77, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 10),new CraftRequirement(173, -1, 20),new CraftRequirement(1329, -1, 5) }),   // ObsidianHelm
        new CraftRecipe(3267, 1, 77, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 10),new CraftRequirement(173, -1, 20),new CraftRequirement(1329, -1, 10) }),   // ObsidianShirt
        new CraftRecipe(3268, 1, 77, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 10),new CraftRequirement(173, -1, 20),new CraftRequirement(1329, -1, 5) }),   // ObsidianPants
        new CraftRecipe(1282, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(262, -1, 1),new CraftRequirement(181, -1, 10) }),   // AmethystRobe
        new CraftRecipe(1283, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(262, -1, 1),new CraftRequirement(180, -1, 10) }),   // TopazRobe
        new CraftRecipe(1284, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(262, -1, 1),new CraftRequirement(177, -1, 10) }),   // SapphireRobe
        new CraftRecipe(1285, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(262, -1, 1),new CraftRequirement(179, -1, 10) }),   // EmeraldRobe
        new CraftRecipe(1286, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(262, -1, 1),new CraftRequirement(178, -1, 10) }),   // RubyRobe
        new CraftRecipe(1287, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(262, -1, 1),new CraftRequirement(182, -1, 10) }),   // DiamondRobe
        new CraftRecipe(4256, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(262, -1, 1),new CraftRequirement(999, -1, 10) }),   // AmberRobe
        new CraftRecipe(4242, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1050, -1, 1) }),   // GolfBallDyedBlack
        new CraftRecipe(4243, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1015, -1, 1) }),   // GolfBallDyedBlue
        new CraftRecipe(4244, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(2874, -1, 1) }),   // GolfBallDyedBrown
        new CraftRecipe(4245, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1013, -1, 1) }),   // GolfBallDyedCyan
        new CraftRecipe(4246, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1011, -1, 1) }),   // GolfBallDyedGreen
        new CraftRecipe(4247, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1010, -1, 1) }),   // GolfBallDyedLimeGreen
        new CraftRecipe(4248, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1008, -1, 1) }),   // GolfBallDyedOrange
        new CraftRecipe(4249, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1018, -1, 1) }),   // GolfBallDyedPink
        new CraftRecipe(4250, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1016, -1, 1) }),   // GolfBallDyedPurple
        new CraftRecipe(4251, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1007, -1, 1) }),   // GolfBallDyedRed
        new CraftRecipe(4252, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1014, -1, 1) }),   // GolfBallDyedSkyBlue
        new CraftRecipe(4253, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1012, -1, 1) }),   // GolfBallDyedTeal
        new CraftRecipe(4254, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1017, -1, 1) }),   // GolfBallDyedViolet
        new CraftRecipe(4255, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3989, -1, 1),new CraftRequirement(1009, -1, 1) }),   // GolfBallDyedYellow
        new CraftRecipe(3306, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(150, -1, 30) }),   // WhiteString
        new CraftRecipe(3293, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1007, -1, 1) }),   // RedString
        new CraftRecipe(3294, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1008, -1, 1) }),   // OrangeString
        new CraftRecipe(3295, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1009, -1, 1) }),   // YellowString
        new CraftRecipe(3296, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1010, -1, 1) }),   // LimeString
        new CraftRecipe(3297, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1011, -1, 1) }),   // GreenString
        new CraftRecipe(3298, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1012, -1, 1) }),   // TealString
        new CraftRecipe(3299, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1013, -1, 1) }),   // CyanString
        new CraftRecipe(3300, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1014, -1, 1) }),   // SkyBlueString
        new CraftRecipe(3301, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1015, -1, 1) }),   // BlueString
        new CraftRecipe(3302, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1016, -1, 1) }),   // PurpleString
        new CraftRecipe(3303, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1017, -1, 1) }),   // VioletString
        new CraftRecipe(3304, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1018, -1, 1) }),   // PinkString
        new CraftRecipe(3308, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1050, -1, 1) }),   // BlackString
        new CraftRecipe(3305, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(2874, -1, 1) }),   // BrownString
        new CraftRecipe(3307, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(1066, -1, 1) }),   // RainbowString
        new CraftRecipe(5547, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(3309, -1, 1) }),   // StrungCounterweight
        new CraftRecipe(5547, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(3310, -1, 1) }),   // StrungCounterweight
        new CraftRecipe(5547, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(3311, -1, 1) }),   // StrungCounterweight
        new CraftRecipe(5547, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(3312, -1, 1) }),   // StrungCounterweight
        new CraftRecipe(5547, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(3313, -1, 1) }),   // StrungCounterweight
        new CraftRecipe(5547, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3306, -1, 1),new CraftRequirement(3314, -1, 1) }),   // StrungCounterweight
        new CraftRecipe(3366, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3334, -1, 1),new CraftRequirement(5547, -1, 1) }),   // YoyoBag
        new CraftRecipe(5541, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(5540, -1, 1),new CraftRequirement(3366, -1, 1) }),   // MagicYoyoBag
        new CraftRecipe(259, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(68, -1, 5) }),   // Leather
        new CraftRecipe(252, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(259, -1, 15) }),   // ArchaeologistsJacket
        new CraftRecipe(253, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(259, -1, 15) }),   // ArchaeologistsPants
        new CraftRecipe(978, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(803, -1, 1),new CraftRequirement(981, -1, 3) }),   // PinkEskimoHood
        new CraftRecipe(979, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(804, -1, 1),new CraftRequirement(981, -1, 3) }),   // PinkEskimoCoat
        new CraftRecipe(980, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(805, -1, 1),new CraftRequirement(981, -1, 3) }),   // PinkEskimoPants
        new CraftRecipe(3365, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 10) }),   // Chimney
        new CraftRecipe(4075, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 4) }),   // WeatherVane
        new CraftRecipe(4064, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 12) }),   // PicnicTable
        new CraftRecipe(4065, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 12),new CraftRequirement(225, -1, 3) }),   // PicnicTableWithCloth
        new CraftRecipe(4859, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4858, -1, 1),new CraftRequirement(313, -1, 1) }),   // PotSuspendedDaybloom
        new CraftRecipe(4860, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4858, -1, 1),new CraftRequirement(314, -1, 1) }),   // PotSuspendedMoonglow
        new CraftRecipe(4861, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4858, -1, 1),new CraftRequirement(317, -1, 1) }),   // PotSuspendedWaterleaf
        new CraftRecipe(4862, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4858, -1, 1),new CraftRequirement(2358, -1, 1) }),   // PotSuspendedShiverthorn
        new CraftRecipe(4863, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4858, -1, 1),new CraftRequirement(315, -1, 1) }),   // PotSuspendedBlinkroot
        new CraftRecipe(4864, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4858, -1, 1),new CraftRequirement(316, -1, 1) }),   // PotSuspendedDeathweedCorrupt
        new CraftRecipe(4865, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4858, -1, 1),new CraftRequirement(316, -1, 1) }),   // PotSuspendedDeathweedCrimson
        new CraftRecipe(4866, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4858, -1, 1),new CraftRequirement(318, -1, 1) }),   // PotSuspendedFireblossom
        new CraftRecipe(4867, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4858, -1, 1),new CraftRequirement(8, -1, 3) }),   // BrazierSuspended
        new CraftRecipe(4852, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(27, -1, 1),new CraftRequirement(181, -1, 1) }),   // GemTreeAmethystSeed
        new CraftRecipe(4851, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(27, -1, 1),new CraftRequirement(180, -1, 1) }),   // GemTreeTopazSeed
        new CraftRecipe(4853, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(27, -1, 1),new CraftRequirement(177, -1, 1) }),   // GemTreeSapphireSeed
        new CraftRecipe(4854, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(27, -1, 1),new CraftRequirement(179, -1, 1) }),   // GemTreeEmeraldSeed
        new CraftRecipe(4855, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(27, -1, 1),new CraftRequirement(178, -1, 1) }),   // GemTreeRubySeed
        new CraftRecipe(4856, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(27, -1, 1),new CraftRequirement(182, -1, 1) }),   // GemTreeDiamondSeed
        new CraftRecipe(4857, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(27, -1, 1),new CraftRequirement(999, -1, 1) }),   // GemTreeAmberSeed
        new CraftRecipe(4869, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4868, -1, 3) }),   // VolcanoLarge
        new CraftRecipe(3364, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 10),new CraftRequirement(9, 25, 4),new CraftRequirement(8, -1, 2) }),   // Fireplace
        new CraftRecipe(33, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3, 26, 20),new CraftRequirement(9, 25, 4),new CraftRequirement(8, -1, 3) }),   // Furnace
        new CraftRecipe(360, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50) }),   // ArmorStatue
        new CraftRecipe(444, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(261, -1, 5) }),   // FishStatue
        new CraftRecipe(3653, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(2002, -1, 5) }),   // WormStatue
        new CraftRecipe(3651, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(2018, 2, 5) }),   // SquirrelStatue
        new CraftRecipe(3652, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(1998, 5, 5) }),   // ButterflyStatue
        new CraftRecipe(3654, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(1992, 6, 5) }),   // FireflyStatue
        new CraftRecipe(3655, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(2157, 1, 5) }),   // ScorpionStatue
        new CraftRecipe(3656, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(2006, 7, 5) }),   // SnailStatue
        new CraftRecipe(3658, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(2003, -1, 5) }),   // MouseStatue
        new CraftRecipe(3659, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(2123, 4, 5) }),   // DuckStatue
        new CraftRecipe(3660, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(2205, -1, 5) }),   // PenguinStatue
        new CraftRecipe(3661, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(2121, -1, 5) }),   // FrogStatue
        new CraftRecipe(3662, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(3194, 3, 5) }),   // BuggyStatue
        new CraftRecipe(445, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(2019, -1, 5) }),   // BunnyStatue
        new CraftRecipe(464, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(2015, 0, 5) }),   // BirdStatue
        new CraftRecipe(3657, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(2740, -1, 5) }),   // GrasshopperStatue
        new CraftRecipe(4342, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(4334, 8, 5) }),   // DragonflyStatue
        new CraftRecipe(4360, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(4359, -1, 5) }),   // SeagullStatue
        new CraftRecipe(4397, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(4395, -1, 5) }),   // OwlStatue
        new CraftRecipe(4466, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(4464, 9, 5) }),   // TurtleStatue
        new CraftRecipe(5317, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(5212, 10, 5) }),   // MacawStatue
        new CraftRecipe(5318, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(5311, -1, 5) }),   // ToucanStatue
        new CraftRecipe(5319, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(5312, 11, 5) }),   // CockatielStatue
        new CraftRecipe(20, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(12, -1, 3) }),   // CopperBar
        new CraftRecipe(3509, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 8),new CraftRequirement(9, 25, 4) }),   // CopperPickaxe
        new CraftRecipe(3506, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 6),new CraftRequirement(9, 25, 3) }),   // CopperAxe
        new CraftRecipe(3505, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 8),new CraftRequirement(9, 25, 3) }),   // CopperHammer
        new CraftRecipe(3508, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 6) }),   // CopperBroadsword
        new CraftRecipe(3507, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 5) }),   // CopperShortsword
        new CraftRecipe(3504, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 7) }),   // CopperBow
        new CraftRecipe(739, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 7),new CraftRequirement(181, -1, 8) }),   // AmethystStaff
        new CraftRecipe(89, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 12) }),   // CopperHelmet
        new CraftRecipe(80, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 20) }),   // CopperChainmail
        new CraftRecipe(76, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 16) }),   // CopperGreaves
        new CraftRecipe(15, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 10),new CraftRequirement(85, -1, 1) }),   // CopperWatch
        new CraftRecipe(106, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(20, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // CopperChandelier
        new CraftRecipe(703, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(699, -1, 3) }),   // TinBar
        new CraftRecipe(3503, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 8),new CraftRequirement(9, 25, 4) }),   // TinPickaxe
        new CraftRecipe(3500, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 6),new CraftRequirement(9, 25, 3) }),   // TinAxe
        new CraftRecipe(3499, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 8),new CraftRequirement(9, 25, 3) }),   // TinHammer
        new CraftRecipe(3502, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 6) }),   // TinBroadsword
        new CraftRecipe(3501, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 5) }),   // TinShortsword
        new CraftRecipe(3498, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 7) }),   // TinBow
        new CraftRecipe(740, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 7),new CraftRequirement(180, -1, 8) }),   // TopazStaff
        new CraftRecipe(687, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 12) }),   // TinHelmet
        new CraftRecipe(688, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 20) }),   // TinChainmail
        new CraftRecipe(689, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 16) }),   // TinGreaves
        new CraftRecipe(707, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 10),new CraftRequirement(85, -1, 1) }),   // TinWatch
        new CraftRecipe(710, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(703, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // TinChandelier
        new CraftRecipe(22, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(11, -1, 3) }),   // IronBar
        new CraftRecipe(35, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 5) }),   // IronAnvil
        new CraftRecipe(2291, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 8) }),   // ReinforcedFishingPole
        new CraftRecipe(1, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 10),new CraftRequirement(9, 25, 3) }),   // IronPickaxe
        new CraftRecipe(10, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 8),new CraftRequirement(9, 25, 3) }),   // IronAxe
        new CraftRecipe(7, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 8),new CraftRequirement(9, 25, 3) }),   // IronHammer
        new CraftRecipe(4711, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 12),new CraftRequirement(9, 25, 3) }),   // GravediggerShovel
        new CraftRecipe(4, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 8) }),   // IronBroadsword
        new CraftRecipe(6, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 6) }),   // IronShortsword
        new CraftRecipe(99, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 7) }),   // IronBow
        new CraftRecipe(90, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 15) }),   // IronHelmet
        new CraftRecipe(81, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 25) }),   // IronChainmail
        new CraftRecipe(77, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 20) }),   // IronGreaves
        new CraftRecipe(704, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(700, -1, 3) }),   // LeadBar
        new CraftRecipe(716, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 5) }),   // LeadAnvil
        new CraftRecipe(3497, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 10),new CraftRequirement(9, 25, 3) }),   // LeadPickaxe
        new CraftRecipe(3494, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 8),new CraftRequirement(9, 25, 3) }),   // LeadAxe
        new CraftRecipe(3493, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 8),new CraftRequirement(9, 25, 3) }),   // LeadHammer
        new CraftRecipe(3496, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 8) }),   // LeadBroadsword
        new CraftRecipe(3495, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 6) }),   // LeadShortsword
        new CraftRecipe(3492, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 7) }),   // LeadBow
        new CraftRecipe(690, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 15) }),   // LeadHelmet
        new CraftRecipe(691, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 25) }),   // LeadChainmail
        new CraftRecipe(692, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 20) }),   // LeadGreaves
        new CraftRecipe(205, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 2) }),   // EmptyBucket
        new CraftRecipe(1128, 1, 215, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 1),new CraftRequirement(205, -1, 1) }),   // HoneyBucket
        new CraftRecipe(5364, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3467, -1, 10),new CraftRequirement(3031, -1, 1) }),   // BottomlessShimmerBucket
        new CraftRecipe(5304, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3032, -1, 1),new CraftRequirement(4872, -1, 1),new CraftRequirement(5303, -1, 1) }),   // UltraAbsorbantSponge
        new CraftRecipe(1140, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, -1, 4) }),   // IronDoor
        new CraftRecipe(1139, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(704, -1, 4) }),   // LeadDoor
        new CraftRecipe(2172, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(9, 25, 12),new CraftRequirement(22, 28, 8) }),   // HeavyWorkBench
        new CraftRecipe(2194, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 18),new CraftRequirement(8, -1, 8) }),   // GlassKiln
        new CraftRecipe(348, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 8) }),   // TrashCan
        new CraftRecipe(336, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 14) }),   // Bathtub
        new CraftRecipe(358, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 6) }),   // Toilet
        new CraftRecipe(4127, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(182, -1, 6) }),   // ToiletDiamond
        new CraftRecipe(4731, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(358, -1, 1),new CraftRequirement(1570, -1, 1) }),   // TerraToilet
        new CraftRecipe(2841, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 3),new CraftRequirement(206, -1, 1) }),   // MetalSink
        new CraftRecipe(345, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 10),new CraftRequirement(9, 25, 2) }),   // CookingPot
        new CraftRecipe(85, 15, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 1) }),   // Chain
        new CraftRecipe(5328, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(85, -1, 2),new CraftRequirement(154, -1, 5) }),   // ChestLock
        new CraftRecipe(4422, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 1) }),   // Grate
        new CraftRecipe(21, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(14, -1, 4) }),   // SilverBar
        new CraftRecipe(3515, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 10),new CraftRequirement(9, 25, 4) }),   // SilverPickaxe
        new CraftRecipe(3512, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 8),new CraftRequirement(9, 25, 3) }),   // SilverAxe
        new CraftRecipe(3511, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 8),new CraftRequirement(9, 25, 3) }),   // SilverHammer
        new CraftRecipe(3514, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 8) }),   // SilverBroadsword
        new CraftRecipe(3513, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 6) }),   // SilverShortsword
        new CraftRecipe(3510, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 7) }),   // SilverBow
        new CraftRecipe(741, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 7),new CraftRequirement(177, -1, 8) }),   // SapphireStaff
        new CraftRecipe(91, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 15) }),   // SilverHelmet
        new CraftRecipe(82, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 25) }),   // SilverChainmail
        new CraftRecipe(78, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 20) }),   // SilverGreaves
        new CraftRecipe(16, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 10),new CraftRequirement(85, -1, 1) }),   // SilverWatch
        new CraftRecipe(107, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(21, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // SilverChandelier
        new CraftRecipe(705, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(701, -1, 4) }),   // TungstenBar
        new CraftRecipe(3491, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 10),new CraftRequirement(9, 25, 4) }),   // TungstenPickaxe
        new CraftRecipe(3488, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 8),new CraftRequirement(9, 25, 3) }),   // TungstenAxe
        new CraftRecipe(3487, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 8),new CraftRequirement(9, 25, 3) }),   // TungstenHammer
        new CraftRecipe(3490, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 8) }),   // TungstenBroadsword
        new CraftRecipe(3489, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 6) }),   // TungstenShortsword
        new CraftRecipe(3486, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 7) }),   // TungstenBow
        new CraftRecipe(742, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 7),new CraftRequirement(179, -1, 8) }),   // EmeraldStaff
        new CraftRecipe(693, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 15) }),   // TungstenHelmet
        new CraftRecipe(694, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 25) }),   // TungstenChainmail
        new CraftRecipe(695, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 20) }),   // TungstenGreaves
        new CraftRecipe(708, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 10),new CraftRequirement(85, -1, 1) }),   // TungstenWatch
        new CraftRecipe(711, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(705, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // TungstenChandelier
        new CraftRecipe(19, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(13, -1, 4) }),   // GoldBar
        new CraftRecipe(3521, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 10),new CraftRequirement(9, 25, 4) }),   // GoldPickaxe
        new CraftRecipe(3518, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 8),new CraftRequirement(9, 25, 3) }),   // GoldAxe
        new CraftRecipe(3517, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 8),new CraftRequirement(9, 25, 3) }),   // GoldHammer
        new CraftRecipe(3520, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 8) }),   // GoldBroadsword
        new CraftRecipe(3519, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 6) }),   // GoldShortsword
        new CraftRecipe(3516, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 7) }),   // GoldBow
        new CraftRecipe(743, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 7),new CraftRequirement(178, -1, 8) }),   // RubyStaff
        new CraftRecipe(92, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 20) }),   // GoldHelmet
        new CraftRecipe(83, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 30) }),   // GoldChainmail
        new CraftRecipe(79, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 25) }),   // GoldGreaves
        new CraftRecipe(17, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 10),new CraftRequirement(85, -1, 1) }),   // GoldWatch
        new CraftRecipe(264, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 5),new CraftRequirement(178, -1, 1) }),   // GoldCrown
        new CraftRecipe(108, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // GoldChandelier
        new CraftRecipe(105, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 1),new CraftRequirement(8, -1, 1) }),   // Candle
        new CraftRecipe(148, 1, 125, CraftEnvironment.Water, new[] { new CraftRequirement(105, -1, 1) }),   // WaterCandle
        new CraftRecipe(3117, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 2),new CraftRequirement(3114, -1, 1) }),   // PeaceCandle
        new CraftRecipe(5322, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 3),new CraftRequirement(8, -1, 1) }),   // ShadowCandle
        new CraftRecipe(5322, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 3),new CraftRequirement(8, -1, 1) }),   // ShadowCandle
        new CraftRecipe(349, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(19, -1, 5),new CraftRequirement(8, -1, 3) }),   // Candelabra
        new CraftRecipe(706, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(702, -1, 4) }),   // PlatinumBar
        new CraftRecipe(3485, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 10),new CraftRequirement(9, 25, 4) }),   // PlatinumPickaxe
        new CraftRecipe(3482, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 8),new CraftRequirement(9, 25, 3) }),   // PlatinumAxe
        new CraftRecipe(3481, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 8),new CraftRequirement(9, 25, 3) }),   // PlatinumHammer
        new CraftRecipe(3484, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 8) }),   // PlatinumBroadsword
        new CraftRecipe(3483, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 6) }),   // PlatinumShortsword
        new CraftRecipe(3480, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 7) }),   // PlatinumBow
        new CraftRecipe(744, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 7),new CraftRequirement(182, -1, 8) }),   // DiamondStaff
        new CraftRecipe(696, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 20) }),   // PlatinumHelmet
        new CraftRecipe(697, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 30) }),   // PlatinumChainmail
        new CraftRecipe(698, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 25) }),   // PlatinumGreaves
        new CraftRecipe(709, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 10),new CraftRequirement(85, -1, 1) }),   // PlatinumWatch
        new CraftRecipe(715, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 5),new CraftRequirement(178, -1, 1) }),   // PlatinumCrown
        new CraftRecipe(712, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // PlatinumChandelier
        new CraftRecipe(713, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 1),new CraftRequirement(8, -1, 1) }),   // PlatinumCandle
        new CraftRecipe(148, 1, 125, CraftEnvironment.Water, new[] { new CraftRequirement(713, -1, 1) }),   // WaterCandle
        new CraftRecipe(3117, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 2),new CraftRequirement(3114, -1, 1) }),   // PeaceCandle
        new CraftRecipe(714, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(706, -1, 5),new CraftRequirement(8, -1, 3) }),   // PlatinumCandelabra
        new CraftRecipe(355, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(19, -1, 30) }),   // Throne
        new CraftRecipe(355, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(706, -1, 30) }),   // Throne
        new CraftRecipe(5473, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(150, -1, 30),new CraftRequirement(9, 25, 10) }),   // CobWhip
        new CraftRecipe(5456, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 300),new CraftRequirement(194, -1, 5),new CraftRequirement(86, -1, 5) }),   // DeadCellsMushroomBoiSummonItem
        new CraftRecipe(5456, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(183, -1, 300),new CraftRequirement(194, -1, 5),new CraftRequirement(1329, -1, 5) }),   // DeadCellsMushroomBoiSummonItem
        new CraftRecipe(6191, 1, 125, CraftEnvironment.None, new[] { new CraftRequirement(4293, 18, 1),new CraftRequirement(520, -1, 12),new CraftRequirement(521, -1, 12) }),   // PalworldLittleKinshipPeach
        new CraftRecipe(6192, 1, 125, CraftEnvironment.None, new[] { new CraftRequirement(1291, -1, 1),new CraftRequirement(520, -1, 12),new CraftRequirement(521, -1, 12) }),   // PalworldKinshipPeach
        new CraftRecipe(6174, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5667, -1, 1),new CraftRequirement(6191, -1, 1) }),   // PalworldTrustyDigtoise
        new CraftRecipe(6150, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5665, -1, 1),new CraftRequirement(6191, -1, 1) }),   // PalworldMountTrustyChillet
        new CraftRecipe(6151, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5666, -1, 1),new CraftRequirement(6191, -1, 1) }),   // PalworldMountTrustyChilletIgnis
        new CraftRecipe(6149, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5664, -1, 1),new CraftRequirement(6192, -1, 1) }),   // PalworldMinionTrustyFoxsparks
        new CraftRecipe(6148, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5663, -1, 1),new CraftRequirement(6192, -1, 1) }),   // PalworldMinionTrustyCattiva
        new CraftRecipe(6190, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(149, -1, 1),new CraftRequirement(3467, -1, 10) }),   // OldStyleParkourBook
        new CraftRecipe(5068, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 10),new CraftRequirement(5070, -1, 8),new CraftRequirement(19, -1, 8) }),   // FlinxFurCoat
        new CraftRecipe(5068, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 10),new CraftRequirement(5070, -1, 8),new CraftRequirement(706, -1, 8) }),   // FlinxFurCoat
        new CraftRecipe(5069, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5070, -1, 6),new CraftRequirement(19, -1, 10) }),   // FlinxStaff
        new CraftRecipe(5069, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5070, -1, 6),new CraftRequirement(706, -1, 10) }),   // FlinxStaff
        new CraftRecipe(57, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(56, -1, 3) }),   // DemoniteBar
        new CraftRecipe(2293, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 8) }),   // FisherofSouls
        new CraftRecipe(44, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 8) }),   // DemonBow
        new CraftRecipe(45, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 10) }),   // WarAxeoftheNight
        new CraftRecipe(46, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 10) }),   // LightsBane
        new CraftRecipe(102, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 15),new CraftRequirement(86, -1, 10) }),   // ShadowHelmet
        new CraftRecipe(101, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 25),new CraftRequirement(86, -1, 20) }),   // ShadowScalemail
        new CraftRecipe(100, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 20),new CraftRequirement(86, -1, 15) }),   // ShadowGreaves
        new CraftRecipe(103, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 12),new CraftRequirement(86, -1, 6) }),   // NightmarePickaxe
        new CraftRecipe(104, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 10),new CraftRequirement(86, -1, 5) }),   // TheBreaker
        new CraftRecipe(3279, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 10) }),   // CorruptYoyo
        new CraftRecipe(5474, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 12) }),   // CorruptWhip
        new CraftRecipe(5107, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 12),new CraftRequirement(180, -1, 5) }),   // Magiluminescence
        new CraftRecipe(1257, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(880, -1, 3) }),   // CrimtaneBar
        new CraftRecipe(2421, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 8) }),   // Fleshcatcher
        new CraftRecipe(796, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 8) }),   // TendonBow
        new CraftRecipe(799, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 10) }),   // BloodLustCluster
        new CraftRecipe(795, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 10) }),   // BloodButcherer
        new CraftRecipe(792, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 15),new CraftRequirement(1329, -1, 10) }),   // CrimsonHelmet
        new CraftRecipe(793, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 25),new CraftRequirement(1329, -1, 20) }),   // CrimsonScalemail
        new CraftRecipe(794, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 20),new CraftRequirement(1329, -1, 15) }),   // CrimsonGreaves
        new CraftRecipe(798, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 12),new CraftRequirement(1329, -1, 6) }),   // DeathbringerPickaxe
        new CraftRecipe(797, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 10),new CraftRequirement(1329, -1, 5) }),   // FleshGrinder
        new CraftRecipe(801, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 10),new CraftRequirement(1329, -1, 5) }),   // TheMeatball
        new CraftRecipe(3280, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 10) }),   // CrimsonYoyo
        new CraftRecipe(5475, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 12) }),   // CrimsonWhip
        new CraftRecipe(5107, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 12),new CraftRequirement(180, -1, 5) }),   // Magiluminescence
        new CraftRecipe(84, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(85, -1, 3),new CraftRequirement(118, -1, 1) }),   // GrapplingHook
        new CraftRecipe(1236, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(181, -1, 15) }),   // AmethystHook
        new CraftRecipe(1237, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(180, -1, 15) }),   // TopazHook
        new CraftRecipe(1238, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(177, -1, 15) }),   // SapphireHook
        new CraftRecipe(1239, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(179, -1, 15) }),   // EmeraldHook
        new CraftRecipe(1240, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(178, -1, 15) }),   // RubyHook
        new CraftRecipe(1241, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(182, -1, 15) }),   // DiamondHook
        new CraftRecipe(4257, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(999, -1, 15) }),   // AmberHook
        new CraftRecipe(1522, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(181, -1, 15) }),   // LargeAmethyst
        new CraftRecipe(1523, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(180, -1, 15) }),   // LargeTopaz
        new CraftRecipe(1524, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(177, -1, 15) }),   // LargeSapphire
        new CraftRecipe(1525, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(179, -1, 15) }),   // LargeEmerald
        new CraftRecipe(1526, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(178, -1, 15) }),   // LargeRuby
        new CraftRecipe(1527, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(182, -1, 15) }),   // LargeDiamond
        new CraftRecipe(3643, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(999, -1, 15) }),   // LargeAmber
        new CraftRecipe(3648, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(181, -1, 5),new CraftRequirement(3, -1, 10) }),   // GemLockAmethyst
        new CraftRecipe(3647, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(180, -1, 5),new CraftRequirement(3, -1, 10) }),   // GemLockTopaz
        new CraftRecipe(3646, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(179, -1, 5),new CraftRequirement(3, -1, 10) }),   // GemLockEmerald
        new CraftRecipe(3645, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(177, -1, 5),new CraftRequirement(3, -1, 10) }),   // GemLockSapphire
        new CraftRecipe(3644, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(178, -1, 5),new CraftRequirement(3, -1, 10) }),   // GemLockRuby
        new CraftRecipe(3649, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(182, -1, 5),new CraftRequirement(3, -1, 10) }),   // GemLockDiamond
        new CraftRecipe(3650, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(999, -1, 5),new CraftRequirement(3, -1, 10) }),   // GemLockAmber
        new CraftRecipe(117, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(116, -1, 3) }),   // MeteoriteBar
        new CraftRecipe(198, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 15),new CraftRequirement(177, -1, 15) }),   // BluePhaseblade
        new CraftRecipe(199, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 15),new CraftRequirement(178, -1, 15) }),   // RedPhaseblade
        new CraftRecipe(200, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 15),new CraftRequirement(179, -1, 15) }),   // GreenPhaseblade
        new CraftRecipe(201, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 15),new CraftRequirement(181, -1, 15) }),   // PurplePhaseblade
        new CraftRecipe(202, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 15),new CraftRequirement(182, -1, 15) }),   // WhitePhaseblade
        new CraftRecipe(203, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 15),new CraftRequirement(180, -1, 15) }),   // YellowPhaseblade
        new CraftRecipe(4258, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 15),new CraftRequirement(999, -1, 15) }),   // OrangePhaseblade
        new CraftRecipe(5535, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 15),new CraftRequirement(4414, -1, 1) }),   // PinkPhaseblade
        new CraftRecipe(5670, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 15),new CraftRequirement(181, -1, 3),new CraftRequirement(180, -1, 3),new CraftRequirement(177, -1, 3),new CraftRequirement(179, -1, 3),new CraftRequirement(178, -1, 3),new CraftRequirement(182, -1, 3),new CraftRequirement(999, -1, 3) }),   // RainbowPhaseblade
        new CraftRecipe(3764, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(198, -1, 1),new CraftRequirement(502, -1, 25) }),   // BluePhasesaber
        new CraftRecipe(3765, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(199, -1, 1),new CraftRequirement(502, -1, 25) }),   // RedPhasesaber
        new CraftRecipe(3766, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(200, -1, 1),new CraftRequirement(502, -1, 25) }),   // GreenPhasesaber
        new CraftRecipe(3767, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(201, -1, 1),new CraftRequirement(502, -1, 25) }),   // PurplePhasesaber
        new CraftRecipe(3768, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(202, -1, 1),new CraftRequirement(502, -1, 25) }),   // WhitePhasesaber
        new CraftRecipe(3769, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(203, -1, 1),new CraftRequirement(502, -1, 25) }),   // YellowPhasesaber
        new CraftRecipe(4259, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(4258, -1, 1),new CraftRequirement(502, -1, 25) }),   // OrangePhasesaber
        new CraftRecipe(5536, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(5535, -1, 1),new CraftRequirement(502, -1, 25) }),   // PinkPhasesaber
        new CraftRecipe(5671, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(5670, -1, 1),new CraftRequirement(502, -1, 25) }),   // RainbowPhasesaber
        new CraftRecipe(204, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 20) }),   // MeteorHamaxe
        new CraftRecipe(127, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 20) }),   // SpaceGun
        new CraftRecipe(197, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(98, -1, 1),new CraftRequirement(117, -1, 20),new CraftRequirement(75, -1, 5) }),   // StarCannon
        new CraftRecipe(5476, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 20),new CraftRequirement(85, -1, 10),new CraftRequirement(75, -1, 10) }),   // MeteorWhip
        new CraftRecipe(123, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 10) }),   // MeteorHelmet
        new CraftRecipe(124, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 20) }),   // MeteorSuit
        new CraftRecipe(125, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 15) }),   // MeteorLeggings
        new CraftRecipe(5510, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(3380, -1, 20),new CraftRequirement(999, -1, 5),new CraftRequirement(75, -1, 5) }),   // VelociraptorMountItem
        new CraftRecipe(4059, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(3380, -1, 12),new CraftRequirement(9, 25, 4) }),   // FossilPickaxe
        new CraftRecipe(3378, 20, 16, CraftEnvironment.None, new[] { new CraftRequirement(3380, -1, 1) }),   // BoneJavelin
        new CraftRecipe(3379, 30, 16, CraftEnvironment.None, new[] { new CraftRequirement(3380, -1, 1) }),   // BoneDagger
        new CraftRecipe(3377, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(3380, -1, 10),new CraftRequirement(999, -1, 8) }),   // AmberStaff
        new CraftRecipe(3374, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(3380, -1, 15) }),   // FossilHelm
        new CraftRecipe(3375, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(3380, -1, 25) }),   // FossilShirt
        new CraftRecipe(3376, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(3380, -1, 20) }),   // FossilPants
        new CraftRecipe(5074, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 90),new CraftRequirement(150, -1, 55) }),   // BoneWhip
        new CraftRecipe(151, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 40),new CraftRequirement(150, -1, 40) }),   // NecroHelmet
        new CraftRecipe(152, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 60),new CraftRequirement(150, -1, 50) }),   // NecroBreastplate
        new CraftRecipe(153, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 50),new CraftRequirement(150, -1, 45) }),   // NecroGreaves
        new CraftRecipe(190, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(209, -1, 12),new CraftRequirement(331, -1, 15),new CraftRequirement(210, -1, 3) }),   // BladeofGrass
        new CraftRecipe(191, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(331, -1, 6),new CraftRequirement(209, -1, 9) }),   // ThornChakram
        new CraftRecipe(185, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(331, -1, 12),new CraftRequirement(210, -1, 3) }),   // IvyWhip
        new CraftRecipe(5295, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(213, -1, 1),new CraftRequirement(3506, -1, 1),new CraftRequirement(331, -1, 12),new CraftRequirement(210, -1, 3) }),   // AcornAxe
        new CraftRecipe(5295, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(213, -1, 1),new CraftRequirement(3500, -1, 1),new CraftRequirement(331, -1, 12),new CraftRequirement(210, -1, 3) }),   // AcornAxe
        new CraftRecipe(3281, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(620, -1, 8),new CraftRequirement(209, -1, 12),new CraftRequirement(210, -1, 1),new CraftRequirement(331, -1, 9) }),   // JungleYoyo
        new CraftRecipe(4913, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(209, -1, 15),new CraftRequirement(210, -1, 3),new CraftRequirement(331, -1, 12) }),   // ThornWhip
        new CraftRecipe(228, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(331, -1, 8) }),   // JungleHat
        new CraftRecipe(229, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(331, -1, 16),new CraftRequirement(209, -1, 10) }),   // JungleShirt
        new CraftRecipe(230, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(331, -1, 8),new CraftRequirement(210, -1, 2) }),   // JunglePants
        new CraftRecipe(2364, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2431, -1, 14) }),   // HornetStaff
        new CraftRecipe(2361, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2431, -1, 8) }),   // BeeHeadgear
        new CraftRecipe(2362, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2431, -1, 12) }),   // BeeBreastplate
        new CraftRecipe(2363, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2431, -1, 10) }),   // BeeGreaves
        new CraftRecipe(5294, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2431, -1, 14) }),   // HiveFive
        new CraftRecipe(175, 1, 77, CraftEnvironment.None, new[] { new CraftRequirement(174, -1, 3),new CraftRequirement(173, -1, 1) }),   // HellstoneBar
        new CraftRecipe(119, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(175, -1, 10),new CraftRequirement(55, -1, 1) }),   // Flamarang
        new CraftRecipe(120, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(175, -1, 15) }),   // MoltenFury
        new CraftRecipe(121, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(175, -1, 20) }),   // FieryGreatsword
        new CraftRecipe(122, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(175, -1, 20) }),   // MoltenPickaxe
        new CraftRecipe(217, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(175, -1, 15) }),   // MoltenHamaxe
        new CraftRecipe(219, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(175, -1, 10),new CraftRequirement(164, -1, 1) }),   // PhoenixBlaster
        new CraftRecipe(2365, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(175, -1, 17) }),   // ImpStaff
        new CraftRecipe(231, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(175, -1, 10) }),   // MoltenHelmet
        new CraftRecipe(232, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(175, -1, 20) }),   // MoltenBreastplate
        new CraftRecipe(233, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(175, -1, 15) }),   // MoltenGreaves
        new CraftRecipe(4821, 1, 77, CraftEnvironment.None, new[] { new CraftRequirement(175, -1, 15),new CraftRequirement(1991, -1, 1) }),   // FireproofBugNet
        new CraftRecipe(5129, 1, -1, CraftEnvironment.Honey, new[] { new CraftRequirement(3484, -1, 1),new CraftRequirement(5132, -1, 5) }),   // Flymeal
        new CraftRecipe(5129, 1, -1, CraftEnvironment.Honey, new[] { new CraftRequirement(3520, -1, 1),new CraftRequirement(5132, -1, 5) }),   // Flymeal
        new CraftRecipe(273, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(46, -1, 1),new CraftRequirement(155, -1, 1),new CraftRequirement(190, -1, 1),new CraftRequirement(121, -1, 1) }),   // NightsEdge
        new CraftRecipe(273, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(795, -1, 1),new CraftRequirement(155, -1, 1),new CraftRequirement(190, -1, 1),new CraftRequirement(121, -1, 1) }),   // NightsEdge
        new CraftRecipe(675, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(273, -1, 1),new CraftRequirement(547, -1, 20),new CraftRequirement(548, -1, 20),new CraftRequirement(549, -1, 20) }),   // TrueNightsEdge
        new CraftRecipe(674, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(368, -1, 1),new CraftRequirement(1006, -1, 24) }),   // TrueExcalibur
        new CraftRecipe(757, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(675, -1, 1),new CraftRequirement(674, -1, 1),new CraftRequirement(1570, -1, 1) }),   // TerraBlade
        new CraftRecipe(4956, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(757, -1, 1),new CraftRequirement(3063, -1, 1),new CraftRequirement(3065, -1, 1),new CraftRequirement(2880, -1, 1),new CraftRequirement(1826, -1, 1),new CraftRequirement(3018, -1, 1),new CraftRequirement(65, -1, 1),new CraftRequirement(1123, -1, 1),new CraftRequirement(989, -1, 1),new CraftRequirement(3507, -1, 1) }),   // Zenith
        new CraftRecipe(389, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(527, -1, 1),new CraftRequirement(528, -1, 1),new CraftRequirement(521, -1, 7),new CraftRequirement(520, -1, 7) }),   // DaoofPow
        new CraftRecipe(6155, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(4062, -1, 1),new CraftRequirement(391, 22, 24),new CraftRequirement(521, -1, 20) }),   // LightningStrike
        new CraftRecipe(5462, 1, 133, CraftEnvironment.None, new[] { new CraftRequirement(391, 22, 24),new CraftRequirement(175, -1, 20) }),   // DeadCellsFlint
        new CraftRecipe(381, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(364, -1, 3) }),   // CobaltBar
        new CraftRecipe(372, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 10) }),   // CobaltHelmet
        new CraftRecipe(373, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 10) }),   // CobaltMask
        new CraftRecipe(371, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 10) }),   // CobaltHat
        new CraftRecipe(374, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 20) }),   // CobaltBreastplate
        new CraftRecipe(375, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 15) }),   // CobaltLeggings
        new CraftRecipe(385, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 15) }),   // CobaltDrill
        new CraftRecipe(776, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 15) }),   // CobaltPickaxe
        new CraftRecipe(383, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 10) }),   // CobaltChainsaw
        new CraftRecipe(991, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 10) }),   // CobaltWaraxe
        new CraftRecipe(435, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 10) }),   // CobaltRepeater
        new CraftRecipe(483, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 8) }),   // CobaltSword
        new CraftRecipe(537, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(381, -1, 10) }),   // CobaltNaginata
        new CraftRecipe(1184, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(1104, -1, 3) }),   // PalladiumBar
        new CraftRecipe(1205, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 12) }),   // PalladiumMask
        new CraftRecipe(1206, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 12) }),   // PalladiumHelmet
        new CraftRecipe(1207, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 12) }),   // PalladiumHeadgear
        new CraftRecipe(1208, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 24) }),   // PalladiumBreastplate
        new CraftRecipe(1209, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 18) }),   // PalladiumLeggings
        new CraftRecipe(1189, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 18) }),   // PalladiumDrill
        new CraftRecipe(1188, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 18) }),   // PalladiumPickaxe
        new CraftRecipe(1190, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 12) }),   // PalladiumChainsaw
        new CraftRecipe(1222, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 12) }),   // PalladiumWaraxe
        new CraftRecipe(1187, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 12) }),   // PalladiumRepeater
        new CraftRecipe(1185, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 10) }),   // PalladiumSword
        new CraftRecipe(1186, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1184, -1, 12) }),   // PalladiumPike
        new CraftRecipe(382, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(365, -1, 4) }),   // MythrilBar
        new CraftRecipe(377, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 10) }),   // MythrilHelmet
        new CraftRecipe(378, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 10) }),   // MythrilHat
        new CraftRecipe(376, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 10) }),   // MythrilHood
        new CraftRecipe(379, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 20) }),   // MythrilChainmail
        new CraftRecipe(380, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 15) }),   // MythrilGreaves
        new CraftRecipe(386, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 15) }),   // MythrilDrill
        new CraftRecipe(777, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 15) }),   // MythrilPickaxe
        new CraftRecipe(384, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 10) }),   // MythrilChainsaw
        new CraftRecipe(992, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 10) }),   // MythrilWaraxe
        new CraftRecipe(436, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 10) }),   // MythrilRepeater
        new CraftRecipe(484, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 8) }),   // MythrilSword
        new CraftRecipe(390, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 10) }),   // MythrilHalberd
        new CraftRecipe(525, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(382, -1, 10) }),   // MythrilAnvil
        new CraftRecipe(1191, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(1105, -1, 4) }),   // OrichalcumBar
        new CraftRecipe(1210, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 12) }),   // OrichalcumMask
        new CraftRecipe(1211, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 12) }),   // OrichalcumHelmet
        new CraftRecipe(1212, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 12) }),   // OrichalcumHeadgear
        new CraftRecipe(1213, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 24) }),   // OrichalcumBreastplate
        new CraftRecipe(1214, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 18) }),   // OrichalcumLeggings
        new CraftRecipe(1196, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 18) }),   // OrichalcumDrill
        new CraftRecipe(1195, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 18) }),   // OrichalcumPickaxe
        new CraftRecipe(1197, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 12) }),   // OrichalcumChainsaw
        new CraftRecipe(1223, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 12) }),   // OrichalcumWaraxe
        new CraftRecipe(1194, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 12) }),   // OrichalcumRepeater
        new CraftRecipe(1192, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 10) }),   // OrichalcumSword
        new CraftRecipe(1193, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 12) }),   // OrichalcumHalberd
        new CraftRecipe(1220, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1191, -1, 12) }),   // OrichalcumAnvil
        new CraftRecipe(391, 1, 133, CraftEnvironment.None, new[] { new CraftRequirement(366, -1, 4) }),   // AdamantiteBar
        new CraftRecipe(401, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 12) }),   // AdamantiteHelmet
        new CraftRecipe(402, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 12) }),   // AdamantiteMask
        new CraftRecipe(400, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 12) }),   // AdamantiteHeadgear
        new CraftRecipe(403, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 24) }),   // AdamantiteBreastplate
        new CraftRecipe(404, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 18) }),   // AdamantiteLeggings
        new CraftRecipe(388, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 18) }),   // AdamantiteDrill
        new CraftRecipe(778, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 18) }),   // AdamantitePickaxe
        new CraftRecipe(387, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 12) }),   // AdamantiteChainsaw
        new CraftRecipe(993, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 12) }),   // AdamantiteWaraxe
        new CraftRecipe(481, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 12) }),   // AdamantiteRepeater
        new CraftRecipe(482, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 12) }),   // AdamantiteSword
        new CraftRecipe(406, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, -1, 12) }),   // AdamantiteGlaive
        new CraftRecipe(524, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(366, -1, 30),new CraftRequirement(221, -1, 1) }),   // AdamantiteForge
        new CraftRecipe(1198, 1, 133, CraftEnvironment.None, new[] { new CraftRequirement(1106, -1, 4) }),   // TitaniumBar
        new CraftRecipe(1215, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 13) }),   // TitaniumMask
        new CraftRecipe(1216, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 13) }),   // TitaniumHelmet
        new CraftRecipe(1217, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 13) }),   // TitaniumHeadgear
        new CraftRecipe(1218, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 26) }),   // TitaniumBreastplate
        new CraftRecipe(1219, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 20) }),   // TitaniumLeggings
        new CraftRecipe(1203, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 20) }),   // TitaniumDrill
        new CraftRecipe(1202, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 20) }),   // TitaniumPickaxe
        new CraftRecipe(1204, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 13) }),   // TitaniumChainsaw
        new CraftRecipe(1224, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 13) }),   // TitaniumWaraxe
        new CraftRecipe(1201, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 13) }),   // TitaniumRepeater
        new CraftRecipe(1199, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 13) }),   // TitaniumSword
        new CraftRecipe(1200, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1198, -1, 13) }),   // TitaniumTrident
        new CraftRecipe(1221, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1106, -1, 30),new CraftRequirement(221, -1, 1) }),   // TitaniumForge
        new CraftRecipe(5588, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(88, -1, 1),new CraftRequirement(382, 21, 10) }),   // UpgradedMiningHead
        new CraftRecipe(5589, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(410, -1, 1),new CraftRequirement(382, 21, 10) }),   // UpgradedMiningBody
        new CraftRecipe(5590, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(411, -1, 1),new CraftRequirement(382, 21, 10) }),   // UpgradedMiningLegs
        new CraftRecipe(5596, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 10),new CraftRequirement(382, 21, 5) }),   // WeldingMask
        new CraftRecipe(5591, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2367, -1, 1),new CraftRequirement(382, 21, 10) }),   // UpgradedFishingHead
        new CraftRecipe(5592, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2368, -1, 1),new CraftRequirement(382, 21, 10) }),   // UpgradedFishingBody
        new CraftRecipe(5593, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2369, -1, 1),new CraftRequirement(382, 21, 10) }),   // UpgradedFishingLegs
        new CraftRecipe(2551, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2607, -1, 16) }),   // SpiderStaff
        new CraftRecipe(2366, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2607, -1, 24) }),   // QueenSpiderStaff
        new CraftRecipe(2370, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2607, -1, 8) }),   // SpiderMask
        new CraftRecipe(2371, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2607, -1, 16) }),   // SpiderBreastplate
        new CraftRecipe(2372, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2607, -1, 12) }),   // SpiderGreaves
        new CraftRecipe(684, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, 22, 10),new CraftRequirement(2161, -1, 1) }),   // FrostHelmet
        new CraftRecipe(685, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, 22, 20),new CraftRequirement(2161, -1, 1) }),   // FrostBreastplate
        new CraftRecipe(686, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, 22, 16),new CraftRequirement(2161, -1, 1) }),   // FrostLeggings
        new CraftRecipe(4911, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(520, -1, 8),new CraftRequirement(521, -1, 8),new CraftRequirement(2161, -1, 1) }),   // CoolWhip
        new CraftRecipe(3788, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(534, -1, 1),new CraftRequirement(527, -1, 2),new CraftRequirement(521, -1, 10) }),   // OnyxBlaster
        new CraftRecipe(3787, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(113, -1, 1),new CraftRequirement(528, -1, 2),new CraftRequirement(520, -1, 16) }),   // SkyFracture
        new CraftRecipe(3779, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3795, -1, 1),new CraftRequirement(3783, -1, 2),new CraftRequirement(521, -1, 12) }),   // SpiritFlame
        new CraftRecipe(3776, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, 22, 10),new CraftRequirement(3783, -1, 1) }),   // AncientBattleArmorHat
        new CraftRecipe(3777, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, 22, 20),new CraftRequirement(3783, -1, 1) }),   // AncientBattleArmorShirt
        new CraftRecipe(3778, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(391, 22, 16),new CraftRequirement(3783, -1, 1) }),   // AncientBattleArmorPants
        new CraftRecipe(559, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // HallowedMask
        new CraftRecipe(553, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // HallowedHelmet
        new CraftRecipe(558, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // HallowedHeadgear
        new CraftRecipe(4873, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // HallowedHood
        new CraftRecipe(5660, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // HallowedCrown
        new CraftRecipe(551, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 24) }),   // HallowedPlateMail
        new CraftRecipe(552, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 18) }),   // HallowedGreaves
        new CraftRecipe(4896, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // AncientHallowedMask
        new CraftRecipe(4897, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // AncientHallowedHelmet
        new CraftRecipe(4898, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // AncientHallowedHeadgear
        new CraftRecipe(4899, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // AncientHallowedHood
        new CraftRecipe(4900, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 24) }),   // AncientHallowedPlateMail
        new CraftRecipe(4901, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 18) }),   // AncientHallowedGreaves
        new CraftRecipe(579, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 18),new CraftRequirement(547, -1, 1),new CraftRequirement(548, -1, 1),new CraftRequirement(549, -1, 1) }),   // Drax
        new CraftRecipe(990, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 18),new CraftRequirement(547, -1, 1),new CraftRequirement(548, -1, 1),new CraftRequirement(549, -1, 1) }),   // PickaxeAxe
        new CraftRecipe(578, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // HallowedRepeater
        new CraftRecipe(368, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // Excalibur
        new CraftRecipe(550, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // Gungnir
        new CraftRecipe(4790, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // HallowJoustingLance
        new CraftRecipe(4678, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12) }),   // SwordWhip
        new CraftRecipe(4060, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12),new CraftRequirement(197, -1, 1) }),   // SuperStarCannon
        new CraftRecipe(1006, 1, 133, CraftEnvironment.None, new[] { new CraftRequirement(947, -1, 5) }),   // ChlorophyteBar
        new CraftRecipe(1002, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 12) }),   // ChlorophyteHelmet
        new CraftRecipe(1001, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 12) }),   // ChlorophyteMask
        new CraftRecipe(1003, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 12) }),   // ChlorophyteHeadgear
        new CraftRecipe(5524, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 12) }),   // ChlorophyteVisor
        new CraftRecipe(1004, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 24) }),   // ChlorophytePlateMail
        new CraftRecipe(1005, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 18) }),   // ChlorophyteGreaves
        new CraftRecipe(1231, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 18) }),   // ChlorophyteDrill
        new CraftRecipe(1230, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 18) }),   // ChlorophytePickaxe
        new CraftRecipe(1232, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 18) }),   // ChlorophyteChainsaw
        new CraftRecipe(1233, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 18) }),   // ChlorophyteGreataxe
        new CraftRecipe(1262, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 18) }),   // ChlorophyteJackhammer
        new CraftRecipe(1234, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 18) }),   // ChlorophyteWarhammer
        new CraftRecipe(1229, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 12) }),   // ChlorophyteShotbow
        new CraftRecipe(1227, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 12) }),   // ChlorophyteSaber
        new CraftRecipe(1226, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 12) }),   // ChlorophyteClaymore
        new CraftRecipe(1228, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 12) }),   // ChlorophytePartisan
        new CraftRecipe(2188, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1308, -1, 1),new CraftRequirement(1006, -1, 14) }),   // VenomStaff
        new CraftRecipe(5296, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 18),new CraftRequirement(997, -1, 1) }),   // ChlorophyteExtractinator
        new CraftRecipe(1316, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 12),new CraftRequirement(1328, -1, 1) }),   // TurtleHelmet
        new CraftRecipe(1317, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 24),new CraftRequirement(1328, -1, 1) }),   // TurtleScaleMail
        new CraftRecipe(1318, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 18),new CraftRequirement(1328, -1, 1) }),   // TurtleLeggings
        new CraftRecipe(2199, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2218, -1, 4),new CraftRequirement(1316, -1, 1) }),   // BeetleHelmet
        new CraftRecipe(2200, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2218, -1, 8),new CraftRequirement(1317, -1, 1) }),   // BeetleScaleMail
        new CraftRecipe(2201, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2218, -1, 8),new CraftRequirement(1317, -1, 1) }),   // BeetleShell
        new CraftRecipe(2202, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2218, -1, 6),new CraftRequirement(1318, -1, 1) }),   // BeetleLeggings
        new CraftRecipe(1552, 1, 247, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 1),new CraftRequirement(183, -1, 15) }),   // ShroomiteBar
        new CraftRecipe(1546, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1552, -1, 12) }),   // ShroomiteHeadgear
        new CraftRecipe(1547, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1552, -1, 12) }),   // ShroomiteMask
        new CraftRecipe(1548, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1552, -1, 12) }),   // ShroomiteHelmet
        new CraftRecipe(1549, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1552, -1, 24) }),   // ShroomiteBreastplate
        new CraftRecipe(1550, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1552, -1, 18) }),   // ShroomiteLeggings
        new CraftRecipe(2176, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1552, -1, 18) }),   // ShroomiteDiggingClaw
        new CraftRecipe(3261, 2, 133, CraftEnvironment.None, new[] { new CraftRequirement(1006, -1, 2),new CraftRequirement(1508, -1, 1) }),   // SpectreBar
        new CraftRecipe(2189, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3261, -1, 12) }),   // SpectreMask
        new CraftRecipe(1503, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3261, -1, 12) }),   // SpectreHood
        new CraftRecipe(1504, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3261, -1, 24) }),   // SpectreRobe
        new CraftRecipe(1505, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3261, -1, 18) }),   // SpectrePants
        new CraftRecipe(1506, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3261, -1, 18) }),   // SpectrePickaxe
        new CraftRecipe(1507, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3261, -1, 18) }),   // SpectreHamaxe
        new CraftRecipe(1543, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3261, -1, 8) }),   // SpectrePaintbrush
        new CraftRecipe(1544, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3261, -1, 8) }),   // SpectrePaintRoller
        new CraftRecipe(1545, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3261, -1, 8) }),   // SpectrePaintScraper
        new CraftRecipe(3467, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3460, -1, 4) }),   // LunarBar
        new CraftRecipe(3567, 333, 412, CraftEnvironment.None, new[] { new CraftRequirement(3467, -1, 1) }),   // MoonlordBullet
        new CraftRecipe(3568, 333, 412, CraftEnvironment.None, new[] { new CraftRequirement(3467, -1, 1) }),   // MoonlordArrow
        new CraftRecipe(4318, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3460, -1, 25) }),   // VoidMonolith
        new CraftRecipe(3572, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 6),new CraftRequirement(3458, -1, 6),new CraftRequirement(3459, -1, 6),new CraftRequirement(3456, -1, 6) }),   // LunarHook
        new CraftRecipe(3458, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 1),new CraftRequirement(3457, -1, 1),new CraftRequirement(3459, -1, 1) }),   // FragmentSolar
        new CraftRecipe(3539, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 15) }),   // SolarMonolith
        new CraftRecipe(2763, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 10),new CraftRequirement(3467, -1, 8) }),   // SolarFlareHelmet
        new CraftRecipe(2764, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 20),new CraftRequirement(3467, -1, 16) }),   // SolarFlareBreastplate
        new CraftRecipe(2765, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 15),new CraftRequirement(3467, -1, 12) }),   // SolarFlareLeggings
        new CraftRecipe(2786, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 12),new CraftRequirement(3467, -1, 10) }),   // SolarFlarePickaxe
        new CraftRecipe(2784, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 12),new CraftRequirement(3467, -1, 10) }),   // SolarFlareDrill
        new CraftRecipe(3522, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 14),new CraftRequirement(3467, -1, 12) }),   // LunarHamaxeSolar
        new CraftRecipe(3473, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 18) }),   // SolarEruption
        new CraftRecipe(3543, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 18) }),   // DayBreak
        new CraftRecipe(3456, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 1),new CraftRequirement(3457, -1, 1),new CraftRequirement(3459, -1, 1) }),   // FragmentVortex
        new CraftRecipe(3536, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 15) }),   // VortexMonolith
        new CraftRecipe(2757, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 10),new CraftRequirement(3467, -1, 8) }),   // VortexHelmet
        new CraftRecipe(2758, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 20),new CraftRequirement(3467, -1, 16) }),   // VortexBreastplate
        new CraftRecipe(2759, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 15),new CraftRequirement(3467, -1, 12) }),   // VortexLeggings
        new CraftRecipe(2776, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 12),new CraftRequirement(3467, -1, 10) }),   // VortexPickaxe
        new CraftRecipe(2774, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 12),new CraftRequirement(3467, -1, 10) }),   // VortexDrill
        new CraftRecipe(3523, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 14),new CraftRequirement(3467, -1, 12) }),   // LunarHamaxeVortex
        new CraftRecipe(3475, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 18) }),   // VortexBeater
        new CraftRecipe(3540, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 18) }),   // Phantasm
        new CraftRecipe(3457, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 1),new CraftRequirement(3456, -1, 1),new CraftRequirement(3459, -1, 1) }),   // FragmentNebula
        new CraftRecipe(3537, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 15) }),   // NebulaMonolith
        new CraftRecipe(2760, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 10),new CraftRequirement(3467, -1, 8) }),   // NebulaHelmet
        new CraftRecipe(2761, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 20),new CraftRequirement(3467, -1, 16) }),   // NebulaBreastplate
        new CraftRecipe(2762, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 15),new CraftRequirement(3467, -1, 12) }),   // NebulaLeggings
        new CraftRecipe(2781, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 12),new CraftRequirement(3467, -1, 10) }),   // NebulaPickaxe
        new CraftRecipe(2779, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 12),new CraftRequirement(3467, -1, 10) }),   // NebulaDrill
        new CraftRecipe(3524, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 14),new CraftRequirement(3467, -1, 12) }),   // LunarHamaxeNebula
        new CraftRecipe(3476, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 18) }),   // NebulaArcanum
        new CraftRecipe(3542, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 18) }),   // NebulaBlaze
        new CraftRecipe(3459, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 1),new CraftRequirement(3456, -1, 1),new CraftRequirement(3457, -1, 1) }),   // FragmentStardust
        new CraftRecipe(3538, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 15) }),   // StardustMonolith
        new CraftRecipe(3381, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 10),new CraftRequirement(3467, -1, 8) }),   // StardustHelmet
        new CraftRecipe(3382, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 20),new CraftRequirement(3467, -1, 16) }),   // StardustBreastplate
        new CraftRecipe(3383, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 15),new CraftRequirement(3467, -1, 12) }),   // StardustLeggings
        new CraftRecipe(3466, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 12),new CraftRequirement(3467, -1, 10) }),   // StardustPickaxe
        new CraftRecipe(3464, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 12),new CraftRequirement(3467, -1, 10) }),   // StardustDrill
        new CraftRecipe(3525, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 14),new CraftRequirement(3467, -1, 12) }),   // LunarHamaxeStardust
        new CraftRecipe(3474, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 18) }),   // StardustCellStaff
        new CraftRecipe(3531, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 18) }),   // StardustDragonStaff
        new CraftRecipe(5479, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 18) }),   // ConstellationWhip
        new CraftRecipe(533, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(98, -1, 1),new CraftRequirement(324, -1, 1),new CraftRequirement(319, -1, 5),new CraftRequirement(548, -1, 20) }),   // Megashark
        new CraftRecipe(561, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1225, -1, 12),new CraftRequirement(520, -1, 15),new CraftRequirement(548, -1, 15) }),   // LightDisc
        new CraftRecipe(506, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 20),new CraftRequirement(324, -1, 1),new CraftRequirement(547, -1, 20) }),   // Flamethrower
        new CraftRecipe(2535, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(236, -1, 1),new CraftRequirement(38, -1, 2),new CraftRequirement(1225, -1, 12),new CraftRequirement(549, -1, 20) }),   // OpticStaff
        new CraftRecipe(494, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(508, -1, 1),new CraftRequirement(502, -1, 20),new CraftRequirement(521, -1, 8),new CraftRequirement(549, -1, 15) }),   // MagicalHarp
        new CraftRecipe(425, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(507, -1, 1),new CraftRequirement(501, -1, 25),new CraftRequirement(520, -1, 8),new CraftRequirement(549, -1, 10) }),   // FairyBell
        new CraftRecipe(2343, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 15),new CraftRequirement(9, 25, 10) }),   // Minecart
        new CraftRecipe(5125, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2343, -1, 1),new CraftRequirement(215, -1, 1) }),   // FartMinecart
        new CraftRecipe(5288, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(5125, -1, 1),new CraftRequirement(4731, -1, 1) }),   // TerraFartMinecart
        new CraftRecipe(4468, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2343, -1, 1),new CraftRequirement(2218, -1, 8) }),   // BeetleMinecart
        new CraftRecipe(4451, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2343, -1, 1),new CraftRequirement(1522, -1, 1) }),   // AmethystMinecart
        new CraftRecipe(4452, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2343, -1, 1),new CraftRequirement(1523, -1, 1) }),   // TopazMinecart
        new CraftRecipe(4453, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2343, -1, 1),new CraftRequirement(1524, -1, 1) }),   // SapphireMinecart
        new CraftRecipe(4454, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2343, -1, 1),new CraftRequirement(1525, -1, 1) }),   // EmeraldMinecart
        new CraftRecipe(4455, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2343, -1, 1),new CraftRequirement(1526, -1, 1) }),   // RubyMinecart
        new CraftRecipe(4456, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2343, -1, 1),new CraftRequirement(1527, -1, 1) }),   // DiamondMinecart
        new CraftRecipe(4467, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2343, -1, 1),new CraftRequirement(3643, -1, 1) }),   // AmberMinecart
        new CraftRecipe(4745, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 5),new CraftRequirement(9, 25, 10),new CraftRequirement(68, -1, 10) }),   // CoffinMinecart
        new CraftRecipe(4745, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 5),new CraftRequirement(9, 25, 10),new CraftRequirement(1330, -1, 10) }),   // CoffinMinecart
        new CraftRecipe(5289, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3354, -1, 1),new CraftRequirement(3355, -1, 1),new CraftRequirement(3356, -1, 1) }),   // MinecartPowerup
        new CraftRecipe(2768, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(3467, -1, 40),new CraftRequirement(1006, -1, 40),new CraftRequirement(1552, -1, 40),new CraftRequirement(3261, -1, 40),new CraftRequirement(175, -1, 40),new CraftRequirement(117, -1, 40) }),   // DrillContainmentUnit
        new CraftRecipe(5131, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4797, -1, 1),new CraftRequirement(4960, -1, 1) }),   // ResplendentDessert
        new CraftRecipe(5513, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5511, -1, 5),new CraftRequirement(2316, -1, 10) }),   // PufferfishPet
        new CraftRecipe(495, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(502, -1, 10),new CraftRequirement(526, -1, 2),new CraftRequirement(501, -1, 10),new CraftRequirement(520, -1, 8),new CraftRequirement(549, -1, 15) }),   // RainbowRod
        new CraftRecipe(493, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(320, -1, 10),new CraftRequirement(575, -1, 20),new CraftRequirement(520, -1, 15) }),   // AngelWings
        new CraftRecipe(492, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(320, -1, 10),new CraftRequirement(575, -1, 20),new CraftRequirement(521, -1, 15) }),   // DemonWings
        new CraftRecipe(761, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(501, -1, 99) }),   // FairyWings
        new CraftRecipe(785, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(1516, -1, 1) }),   // HarpyWings
        new CraftRecipe(749, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(1611, -1, 1) }),   // ButterflyWings
        new CraftRecipe(786, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(1517, -1, 1) }),   // BoneWings
        new CraftRecipe(821, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(1518, -1, 1) }),   // FlameWings
        new CraftRecipe(822, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(1519, -1, 1) }),   // FrozenWings
        new CraftRecipe(1165, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(1520, -1, 1) }),   // BatWings
        new CraftRecipe(1515, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(1521, -1, 1) }),   // BeeWings
        new CraftRecipe(1797, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(1811, -1, 1) }),   // TatteredFairyWings
        new CraftRecipe(1830, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(1831, -1, 1) }),   // SpookyWings
        new CraftRecipe(823, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(3261, -1, 10) }),   // GhostWings
        new CraftRecipe(2280, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(2218, -1, 8) }),   // BeetleWings
        new CraftRecipe(1866, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(575, -1, 20),new CraftRequirement(1552, -1, 18) }),   // Hoverboard
        new CraftRecipe(3468, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 14),new CraftRequirement(3467, -1, 10) }),   // WingsSolar
        new CraftRecipe(3469, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3456, -1, 14),new CraftRequirement(3467, -1, 10) }),   // WingsVortex
        new CraftRecipe(3470, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3457, -1, 14),new CraftRequirement(3467, -1, 10) }),   // WingsNebula
        new CraftRecipe(3471, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3459, -1, 14),new CraftRequirement(3467, -1, 10) }),   // WingsStardust
        new CraftRecipe(5627, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(5737, -1, 1),new CraftRequirement(575, -1, 20),new CraftRequirement(1225, -1, 12) }),   // ChippysWings
        new CraftRecipe(250, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(261, -1, 1),new CraftRequirement(126, -1, 1) }),   // FishBowl
        new CraftRecipe(4398, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4373, -1, 1),new CraftRequirement(126, -1, 1) }),   // PupfishBowl
        new CraftRecipe(2439, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2436, -1, 1),new CraftRequirement(126, -1, 1) }),   // BlueJellyfishJar
        new CraftRecipe(2440, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2437, -1, 1),new CraftRequirement(126, -1, 1) }),   // GreenJellyfishJar
        new CraftRecipe(2441, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2438, -1, 1),new CraftRequirement(126, -1, 1) }),   // PinkJellyfishJar
        new CraftRecipe(5133, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(5132, -1, 1) }),   // StinkbugCage
        new CraftRecipe(4846, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(4845, -1, 1) }),   // HellButterflyJar
        new CraftRecipe(4964, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(4961, -1, 1) }),   // EmpressButterflyJar
        new CraftRecipe(4655, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(4068, -1, 1) }),   // PinkFairyJar
        new CraftRecipe(4656, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(4069, -1, 1) }),   // GreenFairyJar
        new CraftRecipe(4657, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(31, -1, 1),new CraftRequirement(4070, -1, 1) }),   // BlueFairyJar
        new CraftRecipe(2208, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 16) }),   // Terrarium
        new CraftRecipe(2162, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2019, -1, 1) }),   // BunnyCage
        new CraftRecipe(2163, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2018, -1, 1) }),   // SquirrelCage
        new CraftRecipe(3565, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(3563, -1, 1) }),   // SquirrelOrangeCage
        new CraftRecipe(2206, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2205, -1, 1) }),   // PenguinCage
        new CraftRecipe(2165, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2123, -1, 1) }),   // DuckCage
        new CraftRecipe(2164, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2122, -1, 1) }),   // MallardDuckCage
        new CraftRecipe(2166, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2015, -1, 1) }),   // BirdCage
        new CraftRecipe(2167, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2016, -1, 1) }),   // BlueJayCage
        new CraftRecipe(2168, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2017, -1, 1) }),   // CardinalCage
        new CraftRecipe(5213, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(5212, -1, 1) }),   // ScarletMacawCage
        new CraftRecipe(5301, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(5300, -1, 1) }),   // BlueMacawCage
        new CraftRecipe(5314, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(5311, -1, 1) }),   // ToucanCage
        new CraftRecipe(5315, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(5312, -1, 1) }),   // YellowCockatielCage
        new CraftRecipe(5316, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(5313, -1, 1) }),   // GrayCockatielCage
        new CraftRecipe(2190, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2121, -1, 1) }),   // FrogCage
        new CraftRecipe(2174, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2006, -1, 1) }),   // SnailCage
        new CraftRecipe(2175, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2007, -1, 1) }),   // GlowingSnailCage
        new CraftRecipe(2186, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2157, -1, 1) }),   // ScorpionCage
        new CraftRecipe(2187, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2156, -1, 1) }),   // BlackScorpionCage
        new CraftRecipe(2191, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2003, -1, 1) }),   // MouseCage
        new CraftRecipe(4376, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4375, -1, 1) }),   // RatCage
        new CraftRecipe(2207, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2002, -1, 1) }),   // WormCage
        new CraftRecipe(4364, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4363, -1, 1) }),   // MaggotCage
        new CraftRecipe(2741, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2740, -1, 1) }),   // GrasshopperCage
        new CraftRecipe(4380, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4361, -1, 1) }),   // LadybugCage
        new CraftRecipe(4461, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4464, -1, 1) }),   // TurtleCage
        new CraftRecipe(4462, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4465, -1, 1) }),   // TurtleJungleCage
        new CraftRecipe(4473, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4374, -1, 1) }),   // GrebeCage
        new CraftRecipe(4474, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4359, -1, 1) }),   // SeagullCage
        new CraftRecipe(4475, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4418, -1, 1) }),   // WaterStriderCage
        new CraftRecipe(4481, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4480, -1, 1) }),   // SeahorseCage
        new CraftRecipe(5512, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(5511, -1, 1) }),   // PufferfishCage
        new CraftRecipe(4396, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4395, -1, 1) }),   // OwlCage
        new CraftRecipe(4850, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4849, -1, 1) }),   // MagmaSnailCage
        new CraftRecipe(4963, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2673, -1, 1) }),   // TruffleWormCage
        new CraftRecipe(4882, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4838, -1, 1) }),   // AmethystBunnyCage
        new CraftRecipe(4883, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4839, -1, 1) }),   // TopazBunnyCage
        new CraftRecipe(4884, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4840, -1, 1) }),   // SapphireBunnyCage
        new CraftRecipe(4885, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4841, -1, 1) }),   // EmeraldBunnyCage
        new CraftRecipe(4886, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4842, -1, 1) }),   // RubyBunnyCage
        new CraftRecipe(4887, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4843, -1, 1) }),   // DiamondBunnyCage
        new CraftRecipe(4888, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4844, -1, 1) }),   // AmberBunnyCage
        new CraftRecipe(4889, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4831, -1, 1) }),   // AmethystSquirrelCage
        new CraftRecipe(4890, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4832, -1, 1) }),   // TopazSquirrelCage
        new CraftRecipe(4891, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4833, -1, 1) }),   // SapphireSquirrelCage
        new CraftRecipe(4892, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4834, -1, 1) }),   // EmeraldSquirrelCage
        new CraftRecipe(4893, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4835, -1, 1) }),   // RubySquirrelCage
        new CraftRecipe(4894, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4836, -1, 1) }),   // DiamondSquirrelCage
        new CraftRecipe(4895, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4837, -1, 1) }),   // AmberSquirrelCage
        new CraftRecipe(4483, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4482, -1, 1) }),   // GoldSeahorseCage
        new CraftRecipe(4476, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4419, -1, 1) }),   // GoldWaterStriderCage
        new CraftRecipe(4275, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4274, -1, 1),new CraftRequirement(126, -1, 1) }),   // GoldGoldfishBowl
        new CraftRecipe(3072, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2891, -1, 1),new CraftRequirement(31, -1, 1) }),   // GoldButterflyCage
        new CraftRecipe(4333, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4340, -1, 1),new CraftRequirement(31, -1, 1) }),   // GoldDragonflyJar
        new CraftRecipe(3070, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2889, -1, 1) }),   // GoldBirdCage
        new CraftRecipe(3071, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2890, -1, 1) }),   // GoldBunnyCage
        new CraftRecipe(3566, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(3564, -1, 1) }),   // SquirrelGoldCage
        new CraftRecipe(3073, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2892, -1, 1) }),   // GoldFrogCage
        new CraftRecipe(3074, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2893, -1, 1) }),   // GoldGrasshopperCage
        new CraftRecipe(3075, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2894, -1, 1) }),   // GoldMouseCage
        new CraftRecipe(3076, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(2895, -1, 1) }),   // GoldWormCage
        new CraftRecipe(4399, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(4362, -1, 1) }),   // GoldLadybugCage
        new CraftRecipe(3254, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(3191, -1, 1) }),   // CageEnchantedNightcrawler
        new CraftRecipe(3255, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(3194, -1, 1) }),   // CageBuggy
        new CraftRecipe(3256, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(3192, -1, 1) }),   // CageGrubby
        new CraftRecipe(3257, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2208, -1, 1),new CraftRequirement(3193, -1, 1) }),   // CageSluggy
        new CraftRecipe(1085, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1073, -1, 2) }),   // DeepRedPaint
        new CraftRecipe(1086, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1074, -1, 2) }),   // DeepOrangePaint
        new CraftRecipe(1087, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1075, -1, 2) }),   // DeepYellowPaint
        new CraftRecipe(1088, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1076, -1, 2) }),   // DeepLimePaint
        new CraftRecipe(1089, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1077, -1, 2) }),   // DeepGreenPaint
        new CraftRecipe(1090, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1078, -1, 2) }),   // DeepTealPaint
        new CraftRecipe(1091, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1079, -1, 2) }),   // DeepCyanPaint
        new CraftRecipe(1092, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1080, -1, 2) }),   // DeepSkyBluePaint
        new CraftRecipe(1093, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1081, -1, 2) }),   // DeepBluePaint
        new CraftRecipe(1094, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1082, -1, 2) }),   // DeepPurplePaint
        new CraftRecipe(1095, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1083, -1, 2) }),   // DeepVioletPaint
        new CraftRecipe(1096, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1084, -1, 2) }),   // DeepPinkPaint
        new CraftRecipe(1007, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1115, -1, 1) }),   // RedDye
        new CraftRecipe(1008, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1114, -1, 1) }),   // OrangeDye
        new CraftRecipe(1009, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1110, -1, 1) }),   // YellowDye
        new CraftRecipe(1010, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1112, -1, 1) }),   // LimeDye
        new CraftRecipe(1011, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1108, -1, 1) }),   // GreenDye
        new CraftRecipe(1012, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1107, -1, 1) }),   // TealDye
        new CraftRecipe(1013, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1116, -1, 1) }),   // CyanDye
        new CraftRecipe(1014, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1109, -1, 1) }),   // SkyBlueDye
        new CraftRecipe(1015, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1111, -1, 1) }),   // BlueDye
        new CraftRecipe(1016, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1118, -1, 1) }),   // PurpleDye
        new CraftRecipe(1017, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1117, -1, 1) }),   // VioletDye
        new CraftRecipe(1018, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1113, -1, 1) }),   // PinkDye
        new CraftRecipe(1050, 2, 228, CraftEnvironment.None, new[] { new CraftRequirement(1119, -1, 1) }),   // BlackDye
        new CraftRecipe(1031, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1007, -1, 1),new CraftRequirement(1008, -1, 1),new CraftRequirement(1009, -1, 1) }),   // FlameDye
        new CraftRecipe(1033, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1009, -1, 1),new CraftRequirement(1010, -1, 1),new CraftRequirement(1011, -1, 1) }),   // GreenFlameDye
        new CraftRecipe(1035, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1013, -1, 1),new CraftRequirement(1014, -1, 1),new CraftRequirement(1015, -1, 1) }),   // BlueFlameDye
        new CraftRecipe(1063, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1031, -1, 2) }),   // IntenseFlameDye
        new CraftRecipe(1064, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1033, -1, 2) }),   // IntenseGreenFlameDye
        new CraftRecipe(1065, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1035, -1, 2) }),   // IntenseBlueFlameDye
        new CraftRecipe(1032, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1031, -1, 1),new CraftRequirement(1050, -1, 1) }),   // FlameAndBlackDye
        new CraftRecipe(1034, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1033, -1, 1),new CraftRequirement(1050, -1, 1) }),   // GreenFlameAndBlackDye
        new CraftRecipe(1036, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1035, -1, 1),new CraftRequirement(1050, -1, 1) }),   // BlueFlameAndBlackDye
        new CraftRecipe(3550, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1031, -1, 1),new CraftRequirement(1037, -1, 1) }),   // FlameAndSilverDye
        new CraftRecipe(3551, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1033, -1, 1),new CraftRequirement(1037, -1, 1) }),   // GreenFlameAndSilverDye
        new CraftRecipe(3552, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1035, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BlueFlameAndSilverDye
        new CraftRecipe(1068, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1008, -1, 1),new CraftRequirement(1009, -1, 1),new CraftRequirement(1010, -1, 1) }),   // YellowGradientDye
        new CraftRecipe(1069, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1012, -1, 1),new CraftRequirement(1013, -1, 1),new CraftRequirement(1014, -1, 1) }),   // CyanGradientDye
        new CraftRecipe(1070, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1016, -1, 1),new CraftRequirement(1017, -1, 1),new CraftRequirement(1018, -1, 1) }),   // VioletGradientDye
        new CraftRecipe(1066, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1068, -1, 1),new CraftRequirement(1069, -1, 1),new CraftRequirement(1070, -1, 1) }),   // RainbowDye
        new CraftRecipe(1067, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1066, -1, 2) }),   // IntenseRainbowDye
        new CraftRecipe(1019, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1007, -1, 1),new CraftRequirement(1050, -1, 1) }),   // RedandBlackDye
        new CraftRecipe(1020, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1008, -1, 1),new CraftRequirement(1050, -1, 1) }),   // OrangeandBlackDye
        new CraftRecipe(1021, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1009, -1, 1),new CraftRequirement(1050, -1, 1) }),   // YellowandBlackDye
        new CraftRecipe(1022, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1010, -1, 1),new CraftRequirement(1050, -1, 1) }),   // LimeandBlackDye
        new CraftRecipe(1023, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1011, -1, 1),new CraftRequirement(1050, -1, 1) }),   // GreenandBlackDye
        new CraftRecipe(1024, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1012, -1, 1),new CraftRequirement(1050, -1, 1) }),   // TealandBlackDye
        new CraftRecipe(1025, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1013, -1, 1),new CraftRequirement(1050, -1, 1) }),   // CyanandBlackDye
        new CraftRecipe(1026, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1014, -1, 1),new CraftRequirement(1050, -1, 1) }),   // SkyBlueandBlackDye
        new CraftRecipe(1027, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1015, -1, 1),new CraftRequirement(1050, -1, 1) }),   // BlueandBlackDye
        new CraftRecipe(1028, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1016, -1, 1),new CraftRequirement(1050, -1, 1) }),   // PurpleandBlackDye
        new CraftRecipe(1029, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1017, -1, 1),new CraftRequirement(1050, -1, 1) }),   // VioletandBlackDye
        new CraftRecipe(1030, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1018, -1, 1),new CraftRequirement(1050, -1, 1) }),   // PinkandBlackDye
        new CraftRecipe(2875, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(2874, -1, 1),new CraftRequirement(1050, -1, 1) }),   // BrownAndBlackDye
        new CraftRecipe(1038, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1007, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightRedDye
        new CraftRecipe(1039, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1008, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightOrangeDye
        new CraftRecipe(1040, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1009, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightYellowDye
        new CraftRecipe(1041, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1010, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightLimeDye
        new CraftRecipe(1042, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1011, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightGreenDye
        new CraftRecipe(1043, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1012, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightTealDye
        new CraftRecipe(1044, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1013, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightCyanDye
        new CraftRecipe(1045, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1014, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightSkyBlueDye
        new CraftRecipe(1046, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1015, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightBlueDye
        new CraftRecipe(1047, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1016, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightPurpleDye
        new CraftRecipe(1048, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1017, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightVioletDye
        new CraftRecipe(1049, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1018, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightPinkDye
        new CraftRecipe(2876, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(2874, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrightBrownDye
        new CraftRecipe(1051, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1007, -1, 1),new CraftRequirement(1037, -1, 1) }),   // RedandSilverDye
        new CraftRecipe(1052, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1008, -1, 1),new CraftRequirement(1037, -1, 1) }),   // OrangeandSilverDye
        new CraftRecipe(1053, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1009, -1, 1),new CraftRequirement(1037, -1, 1) }),   // YellowandSilverDye
        new CraftRecipe(1054, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1010, -1, 1),new CraftRequirement(1037, -1, 1) }),   // LimeandSilverDye
        new CraftRecipe(1055, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1011, -1, 1),new CraftRequirement(1037, -1, 1) }),   // GreenandSilverDye
        new CraftRecipe(1056, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1012, -1, 1),new CraftRequirement(1037, -1, 1) }),   // TealandSilverDye
        new CraftRecipe(1057, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1013, -1, 1),new CraftRequirement(1037, -1, 1) }),   // CyanandSilverDye
        new CraftRecipe(1058, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1014, -1, 1),new CraftRequirement(1037, -1, 1) }),   // SkyBlueandSilverDye
        new CraftRecipe(1059, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1015, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BlueandSilverDye
        new CraftRecipe(1060, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1016, -1, 1),new CraftRequirement(1037, -1, 1) }),   // PurpleandSilverDye
        new CraftRecipe(1061, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1017, -1, 1),new CraftRequirement(1037, -1, 1) }),   // VioletandSilverDye
        new CraftRecipe(1062, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1018, -1, 1),new CraftRequirement(1037, -1, 1) }),   // PinkandSilverDye
        new CraftRecipe(2877, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(2874, -1, 1),new CraftRequirement(1037, -1, 1) }),   // BrownAndSilverDye
        new CraftRecipe(3559, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1037, -1, 1),new CraftRequirement(1050, -1, 1) }),   // SilverAndBlackDye
        new CraftRecipe(3557, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1037, -1, 1),new CraftRequirement(1050, -1, 1) }),   // BlackAndWhiteDye
        new CraftRecipe(3558, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(1037, -1, 2) }),   // BrightSilverDye
        new CraftRecipe(3562, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3561, -1, 1),new CraftRequirement(3111, -1, 10) }),   // PinkGelDye
        new CraftRecipe(3535, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(3533, -1, 1),new CraftRequirement(502, -1, 20) }),   // ShiftingPearlSandsDye
        new CraftRecipe(3526, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(3458, -1, 10) }),   // SolarDye
        new CraftRecipe(3528, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(3456, -1, 10) }),   // VortexDye
        new CraftRecipe(3527, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(3457, -1, 10) }),   // NebulaDye
        new CraftRecipe(3529, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(3459, -1, 10) }),   // StardustDye
        new CraftRecipe(3530, 1, 228, CraftEnvironment.None, new[] { new CraftRequirement(126, -1, 1),new CraftRequirement(3467, -1, 5) }),   // VoidDye
        new CraftRecipe(2750, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(117, -1, 20),new CraftRequirement(501, -1, 10),new CraftRequirement(520, -1, 10) }),   // MeteorStaff
        new CraftRecipe(394, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(187, -1, 1),new CraftRequirement(268, -1, 1) }),   // DivingGear
        new CraftRecipe(1860, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(394, -1, 1),new CraftRequirement(1303, -1, 1) }),   // JellyfishDivingGear
        new CraftRecipe(1861, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1860, -1, 1),new CraftRequirement(950, -1, 1) }),   // ArcticDivingGear
        new CraftRecipe(395, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(17, -1, 1),new CraftRequirement(18, -1, 1),new CraftRequirement(393, -1, 1) }),   // GPS
        new CraftRecipe(395, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(709, -1, 1),new CraftRequirement(18, -1, 1),new CraftRequirement(393, -1, 1) }),   // GPS
        new CraftRecipe(3036, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3120, -1, 1),new CraftRequirement(3037, -1, 1),new CraftRequirement(3096, -1, 1) }),   // FishFinder
        new CraftRecipe(3121, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3102, -1, 1),new CraftRequirement(3099, -1, 1),new CraftRequirement(3119, -1, 1) }),   // GoblinTech
        new CraftRecipe(3122, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3095, -1, 1),new CraftRequirement(3118, -1, 1),new CraftRequirement(3084, -1, 1) }),   // REK
        new CraftRecipe(3123, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(395, -1, 1),new CraftRequirement(3036, -1, 1),new CraftRequirement(3121, -1, 1),new CraftRequirement(3122, -1, 1) }),   // PDA
        new CraftRecipe(50, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 10),new CraftRequirement(19, -1, 8),new CraftRequirement(182, -1, 3) }),   // MagicMirror
        new CraftRecipe(50, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(170, -1, 10),new CraftRequirement(706, -1, 8),new CraftRequirement(182, -1, 3) }),   // MagicMirror
        new CraftRecipe(3124, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3123, -1, 1),new CraftRequirement(50, -1, 1) }),   // CellPhone
        new CraftRecipe(3124, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3123, -1, 1),new CraftRequirement(3199, -1, 1) }),   // CellPhone
        new CraftRecipe(5437, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3124, -1, 1),new CraftRequirement(4263, -1, 1),new CraftRequirement(4819, -1, 1) }),   // ShellphoneDummy
        new CraftRecipe(4000, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(555, -1, 1),new CraftRequirement(2219, -1, 1) }),   // MagnetFlower
        new CraftRecipe(4001, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(555, -1, 1),new CraftRequirement(532, -1, 1) }),   // ManaCloak
        new CraftRecipe(3996, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(2423, -1, 1),new CraftRequirement(976, -1, 1) }),   // FrogWebbing
        new CraftRecipe(3994, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(2423, -1, 1),new CraftRequirement(187, -1, 1) }),   // FrogFlipper
        new CraftRecipe(3995, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3994, -1, 1),new CraftRequirement(976, -1, 1) }),   // FrogGear
        new CraftRecipe(3995, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3996, -1, 1),new CraftRequirement(187, -1, 1) }),   // FrogGear
        new CraftRecipe(3997, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(938, -1, 1),new CraftRequirement(1253, -1, 1) }),   // FrozenShield
        new CraftRecipe(3998, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(938, -1, 1),new CraftRequirement(3016, -1, 1) }),   // HeroShield
        new CraftRecipe(4008, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(88, -1, 1),new CraftRequirement(3109, -1, 1) }),   // UltrabrightHelmet
        new CraftRecipe(3992, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(897, -1, 1),new CraftRequirement(3016, -1, 1) }),   // BerserkerGlove
        new CraftRecipe(4006, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1321, -1, 1),new CraftRequirement(3015, -1, 1) }),   // StalkersQuiver
        new CraftRecipe(4005, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1858, -1, 1),new CraftRequirement(3015, -1, 1) }),   // ReconScope
        new CraftRecipe(3991, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(555, -1, 1),new CraftRequirement(3015, -1, 1) }),   // ArcaneFlower
        new CraftRecipe(6175, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6172, -1, 1),new CraftRequirement(3015, -1, 1) }),   // ScoutSling
        new CraftRecipe(6176, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6172, -1, 1),new CraftRequirement(1248, -1, 1) }),   // TemplarSling
        new CraftRecipe(6177, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6172, -1, 1),new CraftRequirement(156, -1, 1) }),   // RoyalGuardHarness
        new CraftRecipe(6178, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6167, -1, 1),new CraftRequirement(1322, -1, 1) }),   // Pyroclast
        new CraftRecipe(6179, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6167, -1, 1),new CraftRequirement(6159, -1, 1) }),   // ArmletOfRuin
        new CraftRecipe(6180, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6166, -1, 1),new CraftRequirement(554, -1, 1) }),   // SeraphNecklace
        new CraftRecipe(6181, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6166, -1, 1),new CraftRequirement(4002, -1, 1) }),   // PhoenixQuiver
        new CraftRecipe(6182, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6159, -1, 1),new CraftRequirement(211, -1, 1) }),   // WickedClaws
        new CraftRecipe(6183, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6156, -1, 1),new CraftRequirement(397, -1, 1) }),   // SilverShield
        new CraftRecipe(6184, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6165, -1, 1),new CraftRequirement(1132, -1, 1) }),   // SweetBarb
        new CraftRecipe(6185, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6165, -1, 1),new CraftRequirement(535, -1, 1) }),   // CatalystBand
        new CraftRecipe(6186, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6157, -1, 1),new CraftRequirement(532, -1, 1) }),   // DruidicSerpentCloak
        new CraftRecipe(6187, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1290, -1, 1),new CraftRequirement(554, -1, 1) }),   // CrossedHeartNecklace
        new CraftRecipe(6188, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(982, -1, 1),new CraftRequirement(156, -1, 1) }),   // RestorationShield
        new CraftRecipe(6189, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(982, -1, 1),new CraftRequirement(963, -1, 1) }),   // MysticArtsSash
        new CraftRecipe(4007, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3212, -1, 1),new CraftRequirement(1132, -1, 1) }),   // StingerNecklace
        new CraftRecipe(3990, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3200, -1, 1),new CraftRequirement(2423, -1, 1) }),   // AmphibianBoots
        new CraftRecipe(4002, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1321, -1, 1),new CraftRequirement(1322, -1, 1) }),   // MoltenQuiver
        new CraftRecipe(3993, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(405, -1, 1),new CraftRequirement(3017, -1, 1) }),   // FairyBoots
        new CraftRecipe(396, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(158, -1, 1),new CraftRequirement(193, -1, 1) }),   // ObsidianHorseshoe
        new CraftRecipe(397, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(156, -1, 1),new CraftRequirement(193, -1, 1) }),   // ObsidianShield
        new CraftRecipe(1613, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(397, -1, 1),new CraftRequirement(1612, -1, 1) }),   // AnkhShield
        new CraftRecipe(1724, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(53, -1, 1),new CraftRequirement(215, -1, 1) }),   // FartinaJar
        new CraftRecipe(399, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(53, -1, 1),new CraftRequirement(159, -1, 1) }),   // CloudinaBalloon
        new CraftRecipe(1163, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(987, -1, 1),new CraftRequirement(159, -1, 1) }),   // BlizzardinaBalloon
        new CraftRecipe(983, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(857, -1, 1),new CraftRequirement(159, -1, 1) }),   // SandstorminaBalloon
        new CraftRecipe(1863, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1724, -1, 1),new CraftRequirement(159, -1, 1) }),   // FartInABalloon
        new CraftRecipe(3241, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3201, -1, 1),new CraftRequirement(3225, -1, 1) }),   // SharkronBalloon
        new CraftRecipe(1250, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(399, -1, 1),new CraftRequirement(158, -1, 1) }),   // BlueHorseshoeBalloon
        new CraftRecipe(1251, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1163, -1, 1),new CraftRequirement(158, -1, 1) }),   // WhiteHorseshoeBalloon
        new CraftRecipe(1252, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(983, -1, 1),new CraftRequirement(158, -1, 1) }),   // YellowHorseshoeBalloon
        new CraftRecipe(3250, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1863, -1, 1),new CraftRequirement(158, -1, 1) }),   // BalloonHorseshoeFart
        new CraftRecipe(3251, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1249, -1, 1),new CraftRequirement(158, -1, 1) }),   // BalloonHorseshoeHoney
        new CraftRecipe(3252, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3241, -1, 1),new CraftRequirement(158, -1, 1) }),   // BalloonHorseshoeSharkron
        new CraftRecipe(1164, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(399, -1, 1),new CraftRequirement(1163, -1, 1),new CraftRequirement(983, -1, 1) }),   // BundleofBalloons
        new CraftRecipe(5331, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1164, -1, 1),new CraftRequirement(158, -1, 1) }),   // HorseshoeBundle
        new CraftRecipe(5331, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(399, -1, 1),new CraftRequirement(1163, -1, 1),new CraftRequirement(983, -1, 1),new CraftRequirement(158, -1, 1) }),   // HorseshoeBundle
        new CraftRecipe(5331, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1250, -1, 1),new CraftRequirement(1163, 13, 1),new CraftRequirement(983, 14, 1) }),   // HorseshoeBundle
        new CraftRecipe(5331, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1251, -1, 1),new CraftRequirement(399, 12, 1),new CraftRequirement(983, 14, 1) }),   // HorseshoeBundle
        new CraftRecipe(5331, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1252, -1, 1),new CraftRequirement(399, 12, 1),new CraftRequirement(1163, 13, 1) }),   // HorseshoeBundle
        new CraftRecipe(1249, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(159, -1, 1),new CraftRequirement(1132, -1, 1) }),   // HoneyBalloon
        new CraftRecipe(857, 1, 125, CraftEnvironment.None, new[] { new CraftRequirement(53, -1, 1),new CraftRequirement(3783, -1, 1) }),   // SandstorminaBottle
        new CraftRecipe(987, 1, 125, CraftEnvironment.None, new[] { new CraftRequirement(53, -1, 1),new CraftRequirement(2161, -1, 1) }),   // BlizzardinaBottle
        new CraftRecipe(405, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(54, -1, 1),new CraftRequirement(128, -1, 1) }),   // SpectreBoots
        new CraftRecipe(405, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1579, -1, 1),new CraftRequirement(128, -1, 1) }),   // SpectreBoots
        new CraftRecipe(405, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3200, -1, 1),new CraftRequirement(128, -1, 1) }),   // SpectreBoots
        new CraftRecipe(405, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(4055, -1, 1),new CraftRequirement(128, -1, 1) }),   // SpectreBoots
        new CraftRecipe(898, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(405, -1, 1),new CraftRequirement(212, -1, 1),new CraftRequirement(285, -1, 1) }),   // LightningBoots
        new CraftRecipe(1862, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(898, -1, 1),new CraftRequirement(950, -1, 1) }),   // FrostsparkBoots
        new CraftRecipe(5000, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1862, -1, 1),new CraftRequirement(908, -1, 1) }),   // TerrasparkBoots
        new CraftRecipe(193, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(173, -1, 20) }),   // ObsidianSkull
        new CraftRecipe(3999, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(193, -1, 1),new CraftRequirement(906, -1, 1) }),   // LavaSkull
        new CraftRecipe(4004, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(193, -1, 1),new CraftRequirement(1323, -1, 1) }),   // ObsidianSkullRose
        new CraftRecipe(4003, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(4004, -1, 1),new CraftRequirement(906, -1, 1) }),   // MoltenSkullRose
        new CraftRecipe(4003, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3999, -1, 1),new CraftRequirement(1323, -1, 1) }),   // MoltenSkullRose
        new CraftRecipe(4003, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3999, -1, 1),new CraftRequirement(4004, -1, 1) }),   // MoltenSkullRose
        new CraftRecipe(4038, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(906, -1, 1),new CraftRequirement(193, -1, 1) }),   // MoltenCharm
        new CraftRecipe(907, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(863, -1, 1),new CraftRequirement(193, -1, 1) }),   // ObsidianWaterWalkingBoots
        new CraftRecipe(908, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(907, -1, 1),new CraftRequirement(906, -1, 1),new CraftRequirement(1323, -1, 1) }),   // LavaWaders
        new CraftRecipe(908, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(4038, -1, 1),new CraftRequirement(863, -1, 1),new CraftRequirement(1323, -1, 1) }),   // LavaWaders
        new CraftRecipe(908, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(4038, -1, 1),new CraftRequirement(907, -1, 1),new CraftRequirement(1323, -1, 1) }),   // LavaWaders
        new CraftRecipe(908, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(4003, -1, 1),new CraftRequirement(907, -1, 1) }),   // LavaWaders
        new CraftRecipe(908, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(4003, -1, 1),new CraftRequirement(863, -1, 1) }),   // LavaWaders
        new CraftRecipe(4874, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(4822, -1, 1),new CraftRequirement(405, -1, 1) }),   // HellfireTreads
        new CraftRecipe(555, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(223, -1, 1),new CraftRequirement(189, -1, 1) }),   // ManaFlower
        new CraftRecipe(3034, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3033, -1, 1),new CraftRequirement(855, -1, 1) }),   // CoinRing
        new CraftRecipe(3035, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3034, -1, 1),new CraftRequirement(854, -1, 1) }),   // GreedyRing
        new CraftRecipe(897, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(536, -1, 1),new CraftRequirement(211, -1, 1) }),   // PowerGlove
        new CraftRecipe(936, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(897, -1, 1),new CraftRequirement(935, -1, 1) }),   // MechanicalGlove
        new CraftRecipe(1343, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1322, -1, 1),new CraftRequirement(936, -1, 1) }),   // FireGauntlet
        new CraftRecipe(1864, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1845, -1, 1),new CraftRequirement(1167, -1, 1) }),   // PapyrusScarab
        new CraftRecipe(982, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(111, -1, 1),new CraftRequirement(49, -1, 1) }),   // ManaRegenerationBand
        new CraftRecipe(1595, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(111, -1, 1),new CraftRequirement(216, -1, 1) }),   // MagicCuffs
        new CraftRecipe(2221, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(2219, -1, 1),new CraftRequirement(1595, -1, 1) }),   // CelestialCuffs
        new CraftRecipe(2220, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(2219, -1, 1),new CraftRequirement(935, -1, 1) }),   // CelestialEmblem
        new CraftRecipe(860, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(49, -1, 1),new CraftRequirement(535, -1, 1) }),   // CharmofMyths
        new CraftRecipe(1865, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(899, -1, 1),new CraftRequirement(900, -1, 1) }),   // CelestialStone
        new CraftRecipe(861, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(485, -1, 1),new CraftRequirement(497, -1, 1) }),   // MoonShell
        new CraftRecipe(3110, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1865, -1, 1),new CraftRequirement(861, -1, 1) }),   // CelestialShell
        new CraftRecipe(862, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(532, -1, 1),new CraftRequirement(554, -1, 1) }),   // StarVeil
        new CraftRecipe(1247, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1132, -1, 1),new CraftRequirement(532, -1, 1) }),   // BeeCloak
        new CraftRecipe(1578, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1132, -1, 1),new CraftRequirement(1290, -1, 1) }),   // SweetheartNecklace
        new CraftRecipe(976, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(953, -1, 1),new CraftRequirement(975, -1, 1) }),   // TigerClimbingGear
        new CraftRecipe(984, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(976, -1, 1),new CraftRequirement(977, -1, 1),new CraftRequirement(963, -1, 1) }),   // MasterNinjaGear
        new CraftRecipe(3061, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(2214, -1, 1),new CraftRequirement(2215, -1, 1),new CraftRequirement(2216, -1, 1),new CraftRequirement(2217, -1, 1) }),   // ArchitectGizmoPack
        new CraftRecipe(5126, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3061, -1, 1),new CraftRequirement(4056, -1, 1),new CraftRequirement(5010, -1, 1),new CraftRequirement(4341, -1, 1) }),   // HandOfCreation
        new CraftRecipe(3721, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(2373, -1, 1),new CraftRequirement(2375, -1, 1),new CraftRequirement(2374, -1, 1) }),   // AnglerTackleBag
        new CraftRecipe(5064, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3721, -1, 1),new CraftRequirement(4881, -1, 1) }),   // LavaproofTackleBag
        new CraftRecipe(5140, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(5139, -1, 1),new CraftRequirement(75, -1, 5) }),   // FishingBobberGlowingStar
        new CraftRecipe(5144, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(5140, -1, 1),new CraftRequirement(4389, -1, 5) }),   // FishingBobberGlowingArgon
        new CraftRecipe(5142, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(5140, -1, 1),new CraftRequirement(4377, -1, 5) }),   // FishingBobberGlowingKrypton
        new CraftRecipe(5141, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(5140, -1, 1),new CraftRequirement(4354, -1, 5) }),   // FishingBobberGlowingLava
        new CraftRecipe(5146, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(5140, -1, 1),new CraftRequirement(5128, -1, 5) }),   // FishingBobberGlowingRainbow
        new CraftRecipe(5145, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(5140, -1, 1),new CraftRequirement(5127, -1, 5) }),   // FishingBobberGlowingViolet
        new CraftRecipe(5143, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(5140, -1, 1),new CraftRequirement(4378, -1, 5) }),   // FishingBobberGlowingXenon
        new CraftRecipe(901, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(886, -1, 1),new CraftRequirement(892, -1, 1) }),   // ArmorBracing
        new CraftRecipe(902, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(887, -1, 1),new CraftRequirement(885, -1, 1) }),   // MedicatedBandage
        new CraftRecipe(903, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(889, -1, 1),new CraftRequirement(893, -1, 1) }),   // ThePlan
        new CraftRecipe(904, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(890, -1, 1),new CraftRequirement(891, -1, 1) }),   // CountercurseMantra
        new CraftRecipe(5354, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(888, -1, 1),new CraftRequirement(3781, -1, 1) }),   // ReflectiveShades
        new CraftRecipe(1612, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(901, -1, 1),new CraftRequirement(902, -1, 1),new CraftRequirement(903, -1, 1),new CraftRequirement(904, -1, 1),new CraftRequirement(5354, -1, 1) }),   // AnkhCharm
        new CraftRecipe(935, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(490, -1, 1),new CraftRequirement(548, -1, 5),new CraftRequirement(549, -1, 5),new CraftRequirement(547, -1, 5) }),   // AvengerEmblem
        new CraftRecipe(935, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(491, -1, 1),new CraftRequirement(548, -1, 5),new CraftRequirement(549, -1, 5),new CraftRequirement(547, -1, 5) }),   // AvengerEmblem
        new CraftRecipe(935, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(489, -1, 1),new CraftRequirement(548, -1, 5),new CraftRequirement(549, -1, 5),new CraftRequirement(547, -1, 5) }),   // AvengerEmblem
        new CraftRecipe(935, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(2998, -1, 1),new CraftRequirement(548, -1, 5),new CraftRequirement(549, -1, 5),new CraftRequirement(547, -1, 5) }),   // AvengerEmblem
        new CraftRecipe(1301, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(935, -1, 1),new CraftRequirement(1248, -1, 1) }),   // DestroyerEmblem
        new CraftRecipe(1858, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1300, -1, 1),new CraftRequirement(1301, -1, 1) }),   // SniperScope
        new CraftRecipe(518, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(531, -1, 1),new CraftRequirement(502, -1, 20),new CraftRequirement(520, -1, 15) }),   // CrystalStorm
        new CraftRecipe(519, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(531, -1, 1),new CraftRequirement(522, -1, 20),new CraftRequirement(521, -1, 15) }),   // CursedFlames
        new CraftRecipe(1336, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(531, -1, 1),new CraftRequirement(1332, -1, 20),new CraftRequirement(521, -1, 15) }),   // GoldenShower
        new CraftRecipe(37, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(38, -1, 2) }),   // Goggles
        new CraftRecipe(266, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(324, -1, 1),new CraftRequirement(323, -1, 10),new CraftRequirement(180, -1, 5) }),   // Sandgun
        new CraftRecipe(237, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(236, -1, 2) }),   // Sunglasses
        new CraftRecipe(109, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(75, -1, 5) }),   // ManaCrystal
        new CraftRecipe(3625, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(509, -1, 1),new CraftRequirement(851, -1, 1),new CraftRequirement(850, -1, 1),new CraftRequirement(3612, -1, 1),new CraftRequirement(510, -1, 1) }),   // MulticolorWrench
        new CraftRecipe(3611, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(3625, -1, 1),new CraftRequirement(3619, -1, 1),new CraftRequirement(2799, -1, 1),new CraftRequirement(530, -1, 60) }),   // WireKite
        new CraftRecipe(3620, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(849, -1, 50),new CraftRequirement(22, 28, 10),new CraftRequirement(530, -1, 10) }),   // ActuationRod
        new CraftRecipe(511, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 1),new CraftRequirement(530, -1, 1) }),   // ActiveStoneBlock
        new CraftRecipe(512, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(26, -1, 4),new CraftRequirement(530, -1, 1) }),   // InactiveStoneBlock
        new CraftRecipe(3617, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(171, -1, 1),new CraftRequirement(22, 28, 4),new CraftRequirement(530, -1, 4) }),   // AnnouncementBox
        new CraftRecipe(581, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 10),new CraftRequirement(530, -1, 2) }),   // InletPump
        new CraftRecipe(582, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(22, 28, 10),new CraftRequirement(530, -1, 2) }),   // OutletPump
        new CraftRecipe(583, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(17, -1, 1),new CraftRequirement(530, -1, 1) }),   // Timer1Second
        new CraftRecipe(583, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(709, -1, 1),new CraftRequirement(530, -1, 1) }),   // Timer1Second
        new CraftRecipe(584, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(16, -1, 1),new CraftRequirement(530, -1, 1) }),   // Timer3Second
        new CraftRecipe(584, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(708, -1, 1),new CraftRequirement(530, -1, 1) }),   // Timer3Second
        new CraftRecipe(585, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(15, -1, 1),new CraftRequirement(530, -1, 1) }),   // Timer5Second
        new CraftRecipe(585, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(707, -1, 1),new CraftRequirement(530, -1, 1) }),   // Timer5Second
        new CraftRecipe(3632, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(542, 32, 1),new CraftRequirement(22, 28, 2),new CraftRequirement(530, -1, 1) }),   // WeightedPressurePlateCyan
        new CraftRecipe(3630, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(542, 32, 1),new CraftRequirement(22, 28, 2),new CraftRequirement(530, -1, 1) }),   // WeightedPressurePlateOrange
        new CraftRecipe(3626, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(542, 32, 1),new CraftRequirement(22, 28, 2),new CraftRequirement(530, -1, 1) }),   // WeightedPressurePlatePink
        new CraftRecipe(3631, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(542, 32, 1),new CraftRequirement(22, 28, 2),new CraftRequirement(530, -1, 1) }),   // WeightedPressurePlatePurple
        new CraftRecipe(3613, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(520, -1, 5),new CraftRequirement(22, 28, 1),new CraftRequirement(530, -1, 1) }),   // LogicSensor_Sun
        new CraftRecipe(3614, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(521, -1, 5),new CraftRequirement(22, 28, 1),new CraftRequirement(530, -1, 1) }),   // LogicSensor_Moon
        new CraftRecipe(3615, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(549, -1, 1),new CraftRequirement(22, 28, 1),new CraftRequirement(530, -1, 1) }),   // LogicSensor_Above
        new CraftRecipe(3726, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 5),new CraftRequirement(3182, -1, 1),new CraftRequirement(530, -1, 1) }),   // LogicSensor_Water
        new CraftRecipe(3727, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 5),new CraftRequirement(3184, -1, 1),new CraftRequirement(530, -1, 1) }),   // LogicSensor_Lava
        new CraftRecipe(3728, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 5),new CraftRequirement(3185, -1, 1),new CraftRequirement(530, -1, 1) }),   // LogicSensor_Honey
        new CraftRecipe(3729, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1344, -1, 5),new CraftRequirement(3182, -1, 1),new CraftRequirement(3184, -1, 1),new CraftRequirement(3185, -1, 1),new CraftRequirement(530, -1, 1) }),   // LogicSensor_Liquid
        new CraftRecipe(5135, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(539, -1, 1),new CraftRequirement(2607, -1, 2) }),   // VenomDartTrap
        new CraftRecipe(580, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(167, -1, 3),new CraftRequirement(530, -1, 1) }),   // Explosives
        new CraftRecipe(5327, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(167, -1, 3),new CraftRequirement(343, -1, 1) }),   // TNTBarrel
        new CraftRecipe(540, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 6) }),   // Boulder
        new CraftRecipe(5383, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(540, -1, 1),new CraftRequirement(3111, -1, 5) }),   // BouncyBoulder
        new CraftRecipe(5516, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(5395, -1, 20) }),   // Poulder
        new CraftRecipe(5520, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(540, -1, 1),new CraftRequirement(4825, -1, 1) }),   // LavaBoulder
        new CraftRecipe(5521, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(540, -1, 1),new CraftRequirement(150, -1, 200) }),   // SpiderBoulder
        new CraftRecipe(5522, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(540, -1, 1) }),   // Ghoulder
        new CraftRecipe(5514, 1, 125, CraftEnvironment.None, new[] { new CraftRequirement(540, -1, 1),new CraftRequirement(75, -1, 50) }),   // RainbowBoulder
        new CraftRecipe(5384, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(540, -1, 1),new CraftRequirement(29, -1, 1) }),   // LifeCrystalBoulder
        new CraftRecipe(4390, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(276, -1, 6) }),   // RollingCactus
        new CraftRecipe(5066, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1124, -1, 5) }),   // BeeHive
        new CraftRecipe(5067, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3271, -1, 5),new CraftRequirement(323, -1, 1) }),   // AntlionEggs
        new CraftRecipe(5471, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(150, -1, 1) }),   // CobwebReplica
        new CraftRecipe(150, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(5471, -1, 1) }),   // Cobweb
        new CraftRecipe(5467, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(61, -1, 15),new CraftRequirement(57, -1, 3),new CraftRequirement(86, -1, 3) }),   // DemonAltarReplica
        new CraftRecipe(5468, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(836, -1, 15),new CraftRequirement(1257, -1, 3),new CraftRequirement(1329, -1, 3) }),   // CrimsonAltarReplica
        new CraftRecipe(5469, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(57, -1, 5),new CraftRequirement(86, -1, 5) }),   // ShadowOrbReplica
        new CraftRecipe(5470, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(1257, -1, 5),new CraftRequirement(1329, -1, 5) }),   // CrimsonHeartReplica
        new CraftRecipe(5286, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(29, -1, 1) }),   // RepairedLifeCrystal
        new CraftRecipe(5287, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(109, -1, 1) }),   // RepairedManaCrystal
        new CraftRecipe(5320, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(28, -1, 1) }),   // PlaceableHealingPotion
        new CraftRecipe(5321, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(110, -1, 1) }),   // PlaceableManaPotion
        new CraftRecipe(5345, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(4392, -1, 20),new CraftRequirement(530, -1, 6),new CraftRequirement(129, -1, 10) }),   // EchoMonolith
        new CraftRecipe(3393, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(3391, -1, 1),new CraftRequirement(3392, -1, 1) }),   // CrawdadBanner
        new CraftRecipe(3391, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(3392, -1, 1),new CraftRequirement(3393, -1, 1) }),   // SalamanderBanner
        new CraftRecipe(3392, 1, 86, CraftEnvironment.None, new[] { new CraftRequirement(3391, -1, 1),new CraftRequirement(3393, -1, 1) }),   // GiantShellyBanner
        new CraftRecipe(4391, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(664, -1, 1) }),   // ThinIce
        new CraftRecipe(1290, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(111, -1, 1),new CraftRequirement(29, -1, 1) }),   // PanicNecklace
        new CraftRecipe(111, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1290, -1, 1),new CraftRequirement(109, -1, 1) }),   // BandofStarpower
        new CraftRecipe(2193, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4142, -1, 1),new CraftRequirement(521, -1, 10) }),   // FleshCloningVaat
        new CraftRecipe(4142, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2193, -1, 1),new CraftRequirement(521, -1, 10) }),   // LesionStation
        new CraftRecipe(4355, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 50),new CraftRequirement(540, -1, 5) }),   // BoulderStatue
        new CraftRecipe(4640, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(181, -1, 1),new CraftRequirement(3, -1, 1) }),   // AmethystStoneBlock
        new CraftRecipe(4641, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(180, -1, 1),new CraftRequirement(3, -1, 1) }),   // TopazStoneBlock
        new CraftRecipe(4642, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(177, -1, 1),new CraftRequirement(3, -1, 1) }),   // SapphireStoneBlock
        new CraftRecipe(4643, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(179, -1, 1),new CraftRequirement(3, -1, 1) }),   // EmeraldStoneBlock
        new CraftRecipe(4644, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(178, -1, 1),new CraftRequirement(3, -1, 1) }),   // RubyStoneBlock
        new CraftRecipe(4645, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(182, -1, 1),new CraftRequirement(3, -1, 1) }),   // DiamondStoneBlock
        new CraftRecipe(4646, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(999, -1, 1),new CraftRequirement(3, -1, 1) }),   // AmberStoneBlock
        new CraftRecipe(565, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(562, -1, 1),new CraftRequirement(563, -1, 1),new CraftRequirement(564, -1, 1),new CraftRequirement(566, -1, 1),new CraftRequirement(567, -1, 1),new CraftRequirement(568, -1, 1),new CraftRequirement(569, -1, 1),new CraftRequirement(570, -1, 1),new CraftRequirement(571, -1, 1),new CraftRequirement(572, -1, 1),new CraftRequirement(573, -1, 1),new CraftRequirement(574, -1, 1) }),   // MusicBoxTitle
        new CraftRecipe(4356, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(4078, -1, 1),new CraftRequirement(4080, -1, 1),new CraftRequirement(4081, -1, 1),new CraftRequirement(4082, -1, 1),new CraftRequirement(4357, -1, 1),new CraftRequirement(4358, -1, 1),new CraftRequirement(4421, -1, 1),new CraftRequirement(4606, -1, 1),new CraftRequirement(5006, -1, 1),new CraftRequirement(4979, -1, 1),new CraftRequirement(4985, -1, 1),new CraftRequirement(4990, -1, 1) }),   // MusicBoxTitleAlt
        new CraftRecipe(4992, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(1603, -1, 1),new CraftRequirement(1602, -1, 1),new CraftRequirement(4079, -1, 1),new CraftRequirement(4077, -1, 1),new CraftRequirement(1607, -1, 1),new CraftRequirement(4991, -1, 1) }),   // MusicBoxConsoleTitle
        new CraftRecipe(4237, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(562, -1, 1),new CraftRequirement(2860, -1, 25) }),   // MusicBoxDayRemix
        new CraftRecipe(5638, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(576, -1, 1),new CraftRequirement(5573, -1, 2),new CraftRequirement(8, -1, 101) }),   // MusicBoxTorchGod
        new CraftRecipe(43, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(38, -1, 6) }),   // SuspiciousLookingEye
        new CraftRecipe(5120, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(5070, -1, 3),new CraftRequirement(56, -1, 5),new CraftRequirement(38, -1, 1) }),   // DeerThing
        new CraftRecipe(5120, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(5070, -1, 3),new CraftRequirement(880, -1, 5),new CraftRequirement(38, -1, 1) }),   // DeerThing
        new CraftRecipe(4131, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 30),new CraftRequirement(331, -1, 15),new CraftRequirement(86, -1, 30) }),   // VoidLens
        new CraftRecipe(4131, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 30),new CraftRequirement(331, -1, 15),new CraftRequirement(1329, -1, 30) }),   // VoidLens
        new CraftRecipe(4076, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 15),new CraftRequirement(331, -1, 8),new CraftRequirement(1329, -1, 15) }),   // VoidVault
        new CraftRecipe(4076, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 15),new CraftRequirement(331, -1, 8),new CraftRequirement(86, -1, 15) }),   // VoidVault
        new CraftRecipe(70, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(67, -1, 30),new CraftRequirement(68, -1, 15) }),   // WormFood
        new CraftRecipe(1331, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(2886, -1, 30),new CraftRequirement(1330, -1, 15) }),   // BloodySpine
        new CraftRecipe(1133, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(1125, -1, 5),new CraftRequirement(209, -1, 1),new CraftRequirement(1124, -1, 5),new CraftRequirement(1134, -1, 1) }),   // Abeemination
        new CraftRecipe(560, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(23, -1, 20),new CraftRequirement(264, -1, 1) }),   // SlimeCrown
        new CraftRecipe(560, 1, 26, CraftEnvironment.None, new[] { new CraftRequirement(23, -1, 20),new CraftRequirement(715, -1, 1) }),   // SlimeCrown
        new CraftRecipe(544, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(38, -1, 3),new CraftRequirement(22, 28, 5),new CraftRequirement(520, -1, 6) }),   // MechanicalEye
        new CraftRecipe(556, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(68, -1, 6),new CraftRequirement(22, 28, 5),new CraftRequirement(521, -1, 6) }),   // MechanicalWorm
        new CraftRecipe(556, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1330, -1, 6),new CraftRequirement(22, 28, 5),new CraftRequirement(521, -1, 6) }),   // MechanicalWorm
        new CraftRecipe(557, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(154, -1, 30),new CraftRequirement(22, 28, 5),new CraftRequirement(520, -1, 3),new CraftRequirement(521, -1, 3) }),   // MechanicalSkull
        new CraftRecipe(5334, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(544, -1, 1),new CraftRequirement(557, -1, 1),new CraftRequirement(556, -1, 1) }),   // MechdusaSummon
        new CraftRecipe(1844, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(1725, -1, 30),new CraftRequirement(1508, -1, 5),new CraftRequirement(1225, -1, 10) }),   // PumpkinMoonMedallion
        new CraftRecipe(1958, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(225, -1, 20),new CraftRequirement(1508, -1, 5),new CraftRequirement(547, -1, 5) }),   // NaughtyPresent
        new CraftRecipe(2767, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(2766, -1, 8) }),   // SolarTablet
        new CraftRecipe(3601, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(3458, -1, 12),new CraftRequirement(3456, -1, 12),new CraftRequirement(3457, -1, 12),new CraftRequirement(3459, -1, 12) }),   // CelestialSigil
        new CraftRecipe(5104, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5105, -1, 1) }),   // WilsonBeardShort
        new CraftRecipe(5104, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5106, -1, 1) }),   // WilsonBeardShort
        new CraftRecipe(5105, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5106, -1, 1) }),   // WilsonBeardLong
        new CraftRecipe(5644, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(50, 24, 1),new CraftRequirement(38, -1, 2),new CraftRequirement(2997, -1, 4) }),   // ScryingOrb
        new CraftRecipe(5659, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(5661, -1, 1),new CraftRequirement(575, -1, 20),new CraftRequirement(1225, -1, 12) }),   // HeroicisWings
        new CraftRecipe(6166, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(999, -1, 3),new CraftRequirement(320, -1, 7),new CraftRequirement(21, 29, 5) }),   // HarpyCharm
        new CraftRecipe(6161, 1, 17, CraftEnvironment.None, new[] { new CraftRequirement(133, -1, 30),new CraftRequirement(28, -1, 1),new CraftRequirement(313, -1, 1),new CraftRequirement(315, -1, 1) }),   // ClayPotMinion
        new CraftRecipe(6164, 1, 134, CraftEnvironment.None, new[] { new CraftRequirement(527, -1, 1),new CraftRequirement(528, -1, 1),new CraftRequirement(3783, -1, 2),new CraftRequirement(521, -1, 5),new CraftRequirement(520, -1, 5) }),   // ForbiddenMinion
        new CraftRecipe(6163, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6156, -1, 1),new CraftRequirement(6159, -1, 1) }),   // TwilightGrasp
        new CraftRecipe(6162, 1, 114, CraftEnvironment.None, new[] { new CraftRequirement(6157, -1, 1),new CraftRequirement(6158, -1, 1) }),   // OuroborosRing
        new CraftRecipe(71, 100, -1, CraftEnvironment.None, new[] { new CraftRequirement(72, -1, 1) }),   // CopperCoin
        new CraftRecipe(72, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(71, -1, 100) }),   // SilverCoin
        new CraftRecipe(72, 100, -1, CraftEnvironment.None, new[] { new CraftRequirement(73, -1, 1) }),   // SilverCoin
        new CraftRecipe(73, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(72, -1, 100) }),   // GoldCoin
        new CraftRecipe(73, 100, -1, CraftEnvironment.None, new[] { new CraftRequirement(74, -1, 1) }),   // GoldCoin
        new CraftRecipe(74, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(73, -1, 100) }),   // PlatinumCoin
        new CraftRecipe(4229, 10, 412, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 10),new CraftRequirement(3458, -1, 1) }),   // SolarBrick
        new CraftRecipe(4233, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 1) }),   // SolarBrickWall
        new CraftRecipe(4145, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 14) }),   // SolarBathtub
        new CraftRecipe(4146, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 15),new CraftRequirement(225, -1, 5) }),   // SolarBed
        new CraftRecipe(4147, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 20),new CraftRequirement(149, -1, 10) }),   // SolarBookcase
        new CraftRecipe(4148, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 16) }),   // SolarDresser
        new CraftRecipe(4149, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 5),new CraftRequirement(8, -1, 3) }),   // SolarCandelabra
        new CraftRecipe(4150, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 4),new CraftRequirement(8, -1, 1) }),   // SolarCandle
        new CraftRecipe(4151, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 4) }),   // SolarChair
        new CraftRecipe(4152, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // SolarChandelier
        new CraftRecipe(4153, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 8),new CraftRequirement(22, 28, 2) }),   // SolarChest
        new CraftRecipe(4154, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // SolarClock
        new CraftRecipe(4155, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 6) }),   // SolarDoor
        new CraftRecipe(4156, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(4229, -1, 3) }),   // SolarLamp
        new CraftRecipe(4157, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 6),new CraftRequirement(8, -1, 1) }),   // SolarLantern
        new CraftRecipe(4158, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // SolarPiano
        new CraftRecipe(4160, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 6),new CraftRequirement(206, -1, 1) }),   // SolarSink
        new CraftRecipe(4161, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 5),new CraftRequirement(225, -1, 2) }),   // SolarSofa
        new CraftRecipe(4162, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 8) }),   // SolarTable
        new CraftRecipe(4163, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 10) }),   // SolarWorkbench
        new CraftRecipe(4165, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4229, -1, 6) }),   // SolarToilet
        new CraftRecipe(4230, 10, 412, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 10),new CraftRequirement(3456, -1, 1) }),   // VortexBrick
        new CraftRecipe(4234, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 1) }),   // VortexBrickWall
        new CraftRecipe(4166, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 14) }),   // VortexBathtub
        new CraftRecipe(4167, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 15),new CraftRequirement(225, -1, 5) }),   // VortexBed
        new CraftRecipe(4168, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 20),new CraftRequirement(149, -1, 10) }),   // VortexBookcase
        new CraftRecipe(4169, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 16) }),   // VortexDresser
        new CraftRecipe(4170, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 5),new CraftRequirement(8, -1, 3) }),   // VortexCandelabra
        new CraftRecipe(4171, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 4),new CraftRequirement(8, -1, 1) }),   // VortexCandle
        new CraftRecipe(4172, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 4) }),   // VortexChair
        new CraftRecipe(4173, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // VortexChandelier
        new CraftRecipe(4174, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 8),new CraftRequirement(22, 28, 2) }),   // VortexChest
        new CraftRecipe(4175, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // VortexClock
        new CraftRecipe(4176, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 6) }),   // VortexDoor
        new CraftRecipe(4177, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(4230, -1, 3) }),   // VortexLamp
        new CraftRecipe(4178, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 6),new CraftRequirement(8, -1, 1) }),   // VortexLantern
        new CraftRecipe(4179, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // VortexPiano
        new CraftRecipe(4181, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 6),new CraftRequirement(206, -1, 1) }),   // VortexSink
        new CraftRecipe(4182, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 5),new CraftRequirement(225, -1, 2) }),   // VortexSofa
        new CraftRecipe(4183, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 8) }),   // VortexTable
        new CraftRecipe(4184, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 10) }),   // VortexWorkbench
        new CraftRecipe(4186, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4230, -1, 6) }),   // VortexToilet
        new CraftRecipe(4231, 10, 412, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 10),new CraftRequirement(3457, -1, 1) }),   // NebulaBrick
        new CraftRecipe(4235, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 1) }),   // NebulaBrickWall
        new CraftRecipe(4187, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 14) }),   // NebulaBathtub
        new CraftRecipe(4188, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 15),new CraftRequirement(225, -1, 5) }),   // NebulaBed
        new CraftRecipe(4189, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 20),new CraftRequirement(149, -1, 10) }),   // NebulaBookcase
        new CraftRecipe(4190, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 16) }),   // NebulaDresser
        new CraftRecipe(4191, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 5),new CraftRequirement(8, -1, 3) }),   // NebulaCandelabra
        new CraftRecipe(4192, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 4),new CraftRequirement(8, -1, 1) }),   // NebulaCandle
        new CraftRecipe(4193, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 4) }),   // NebulaChair
        new CraftRecipe(4194, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // NebulaChandelier
        new CraftRecipe(4195, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 8),new CraftRequirement(22, 28, 2) }),   // NebulaChest
        new CraftRecipe(4196, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // NebulaClock
        new CraftRecipe(4197, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 6) }),   // NebulaDoor
        new CraftRecipe(4198, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(4231, -1, 3) }),   // NebulaLamp
        new CraftRecipe(4199, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 6),new CraftRequirement(8, -1, 1) }),   // NebulaLantern
        new CraftRecipe(4200, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // NebulaPiano
        new CraftRecipe(4202, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 6),new CraftRequirement(206, -1, 1) }),   // NebulaSink
        new CraftRecipe(4203, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 5),new CraftRequirement(225, -1, 2) }),   // NebulaSofa
        new CraftRecipe(4204, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 8) }),   // NebulaTable
        new CraftRecipe(4205, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 10) }),   // NebulaWorkbench
        new CraftRecipe(4207, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4231, -1, 6) }),   // NebulaToilet
        new CraftRecipe(4232, 10, 412, CraftEnvironment.None, new[] { new CraftRequirement(3, -1, 10),new CraftRequirement(3459, -1, 1) }),   // StardustBrick
        new CraftRecipe(4236, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 1) }),   // StardustBrickWall
        new CraftRecipe(4208, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 14) }),   // StardustBathtub
        new CraftRecipe(4209, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 15),new CraftRequirement(225, -1, 5) }),   // StardustBed
        new CraftRecipe(4210, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 20),new CraftRequirement(149, -1, 10) }),   // StardustBookcase
        new CraftRecipe(4211, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 16) }),   // StardustDresser
        new CraftRecipe(4212, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 5),new CraftRequirement(8, -1, 3) }),   // StardustCandelabra
        new CraftRecipe(4213, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 4),new CraftRequirement(8, -1, 1) }),   // StardustCandle
        new CraftRecipe(4214, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 4) }),   // StardustChair
        new CraftRecipe(4215, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // StardustChandelier
        new CraftRecipe(4216, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 8),new CraftRequirement(22, 28, 2) }),   // StardustChest
        new CraftRecipe(4217, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // StardustClock
        new CraftRecipe(4218, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 6) }),   // StardustDoor
        new CraftRecipe(4219, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(4232, -1, 3) }),   // StardustLamp
        new CraftRecipe(4220, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 6),new CraftRequirement(8, -1, 1) }),   // StardustLantern
        new CraftRecipe(4221, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // StardustPiano
        new CraftRecipe(4223, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 6),new CraftRequirement(206, -1, 1) }),   // StardustSink
        new CraftRecipe(4224, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 5),new CraftRequirement(225, -1, 2) }),   // StardustSofa
        new CraftRecipe(4225, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 8) }),   // StardustTable
        new CraftRecipe(4226, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 10) }),   // StardustWorkbench
        new CraftRecipe(4228, 1, 412, CraftEnvironment.None, new[] { new CraftRequirement(4232, -1, 6) }),   // StardustToilet
        new CraftRecipe(4139, 10, 18, CraftEnvironment.None, new[] { new CraftRequirement(150, -1, 10),new CraftRequirement(2607, -1, 1) }),   // SpiderBlock
        new CraftRecipe(4140, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 1) }),   // SpiderWall
        new CraftRecipe(3931, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 14) }),   // SpiderBathtub
        new CraftRecipe(3932, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 15),new CraftRequirement(225, -1, 5) }),   // SpiderBed
        new CraftRecipe(3933, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 20),new CraftRequirement(149, -1, 10) }),   // SpiderBookcase
        new CraftRecipe(3934, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 16) }),   // SpiderDresser
        new CraftRecipe(3935, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 5),new CraftRequirement(8, -1, 3) }),   // SpiderCandelabra
        new CraftRecipe(3936, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 4),new CraftRequirement(8, -1, 1) }),   // SpiderCandle
        new CraftRecipe(3937, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 4) }),   // SpiderChair
        new CraftRecipe(3938, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // SpiderChandelier
        new CraftRecipe(3939, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 8),new CraftRequirement(22, 28, 2) }),   // SpiderChest
        new CraftRecipe(3940, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // SpiderClock
        new CraftRecipe(3941, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 6) }),   // SpiderDoor
        new CraftRecipe(3942, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(4139, -1, 3) }),   // SpiderLamp
        new CraftRecipe(3943, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 6),new CraftRequirement(8, -1, 1) }),   // SpiderLantern
        new CraftRecipe(3944, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // SpiderPiano
        new CraftRecipe(3946, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 6),new CraftRequirement(206, -1, 1) }),   // SpiderSinkSpiderSinkDoesWhateverASpiderSinkDoes
        new CraftRecipe(3947, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 5),new CraftRequirement(225, -1, 2) }),   // SpiderSofa
        new CraftRecipe(3948, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 8) }),   // SpiderTable
        new CraftRecipe(3949, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 10) }),   // SpiderWorkbench
        new CraftRecipe(4125, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4139, -1, 6) }),   // ToiletSpider
        new CraftRecipe(3955, 1, 218, CraftEnvironment.None, new[] { new CraftRequirement(61, -1, 2) }),   // LesionBlock
        new CraftRecipe(3975, 1, 499, CraftEnvironment.None, new[] { new CraftRequirement(3955, -1, 10) }),   // LesionWorkbench
        new CraftRecipe(3956, 4, 18, CraftEnvironment.None, new[] { new CraftRequirement(3955, -1, 1) }),   // LesionBlockWall
        new CraftRecipe(3967, 1, 499, CraftEnvironment.None, new[] { new CraftRequirement(3955, -1, 6) }),   // LesionDoor
        new CraftRecipe(3963, 1, 499, CraftEnvironment.None, new[] { new CraftRequirement(3955, -1, 4) }),   // LesionChair
        new CraftRecipe(3974, 1, 499, CraftEnvironment.None, new[] { new CraftRequirement(3955, -1, 8) }),   // LesionTable
        new CraftRecipe(3968, 1, 499, CraftEnvironment.None, new[] { new CraftRequirement(3955, -1, 16) }),   // LesionDresser
        new CraftRecipe(3958, 1, 499, CraftEnvironment.None, new[] { new CraftRequirement(3955, -1, 14) }),   // LesionBathtub
        new CraftRecipe(4126, 1, 499, CraftEnvironment.None, new[] { new CraftRequirement(3955, -1, 6) }),   // ToiletLesion
        new CraftRecipe(4720, 2, 106, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 1) }),   // SandstoneColumn
        new CraftRecipe(4298, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 14) }),   // SandstoneBathtub
        new CraftRecipe(4299, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 15),new CraftRequirement(225, -1, 5) }),   // SandstoneBed
        new CraftRecipe(4300, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 20),new CraftRequirement(149, -1, 10) }),   // SandstoneBookcase
        new CraftRecipe(4301, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 16) }),   // SandstoneDresser
        new CraftRecipe(4302, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 5),new CraftRequirement(8, -1, 3) }),   // SandstoneCandelabra
        new CraftRecipe(4303, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 4),new CraftRequirement(8, -1, 1) }),   // SandstoneCandle
        new CraftRecipe(4304, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 4) }),   // SandstoneChair
        new CraftRecipe(4305, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // SandstoneChandelier
        new CraftRecipe(4267, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 8),new CraftRequirement(22, 28, 2) }),   // DesertChest
        new CraftRecipe(4306, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // SandstoneClock
        new CraftRecipe(4307, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 6) }),   // SandstoneDoor
        new CraftRecipe(4308, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(4051, -1, 3) }),   // SandstoneLamp
        new CraftRecipe(4309, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 6),new CraftRequirement(8, -1, 1) }),   // SandstoneLantern
        new CraftRecipe(4310, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // SandstonePiano
        new CraftRecipe(4312, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 6),new CraftRequirement(206, -1, 1) }),   // SandstoneSink
        new CraftRecipe(4313, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 5),new CraftRequirement(225, -1, 2) }),   // SandstoneSofa
        new CraftRecipe(4314, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 8) }),   // SandstoneTable
        new CraftRecipe(4315, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 10) }),   // SandstoneWorkbench
        new CraftRecipe(4316, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4051, -1, 6) }),   // SandstoneToilet
        new CraftRecipe(4566, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 14) }),   // BambooBathtub
        new CraftRecipe(4567, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 15),new CraftRequirement(225, -1, 5) }),   // BambooBed
        new CraftRecipe(4568, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 20),new CraftRequirement(149, -1, 10) }),   // BambooBookcase
        new CraftRecipe(4569, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 16) }),   // BambooDresser
        new CraftRecipe(4570, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 5),new CraftRequirement(8, -1, 3) }),   // BambooCandelabra
        new CraftRecipe(4571, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 4),new CraftRequirement(8, -1, 1) }),   // BambooCandle
        new CraftRecipe(4572, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 4) }),   // BambooChair
        new CraftRecipe(4573, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // BambooChandelier
        new CraftRecipe(4574, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 8),new CraftRequirement(22, 28, 2) }),   // BambooChest
        new CraftRecipe(4575, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // BambooClock
        new CraftRecipe(4576, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 6) }),   // BambooDoor
        new CraftRecipe(4577, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(4564, -1, 3) }),   // BambooLamp
        new CraftRecipe(4578, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 6),new CraftRequirement(8, -1, 1) }),   // BambooLantern
        new CraftRecipe(4579, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // BambooPiano
        new CraftRecipe(4581, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 6),new CraftRequirement(206, -1, 1) }),   // BambooSink
        new CraftRecipe(4582, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 5),new CraftRequirement(225, -1, 2) }),   // BambooSofa
        new CraftRecipe(4583, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 8) }),   // BambooTable
        new CraftRecipe(4584, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 10) }),   // BambooWorkbench
        new CraftRecipe(4586, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(4564, -1, 6) }),   // BambooToilet
        new CraftRecipe(5148, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 14) }),   // CoralBathtub
        new CraftRecipe(5149, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 15),new CraftRequirement(225, -1, 5) }),   // CoralBed
        new CraftRecipe(5150, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 20),new CraftRequirement(149, -1, 10) }),   // CoralBookcase
        new CraftRecipe(5151, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 16) }),   // CoralDresser
        new CraftRecipe(5152, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 5),new CraftRequirement(8, -1, 3) }),   // CoralCandelabra
        new CraftRecipe(5153, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 4),new CraftRequirement(8, -1, 1) }),   // CoralCandle
        new CraftRecipe(5154, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 4) }),   // CoralChair
        new CraftRecipe(5155, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // CoralChandelier
        new CraftRecipe(5156, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 8),new CraftRequirement(22, 28, 2) }),   // CoralChest
        new CraftRecipe(5157, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // CoralClock
        new CraftRecipe(5158, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 6) }),   // CoralDoor
        new CraftRecipe(5159, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5306, -1, 3) }),   // CoralLamp
        new CraftRecipe(5160, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 6),new CraftRequirement(8, -1, 1) }),   // CoralLantern
        new CraftRecipe(5161, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // CoralPiano
        new CraftRecipe(5163, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 6),new CraftRequirement(206, -1, 1) }),   // CoralSink
        new CraftRecipe(5164, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 5),new CraftRequirement(225, -1, 2) }),   // CoralSofa
        new CraftRecipe(5165, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 8) }),   // CoralTable
        new CraftRecipe(5166, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 10) }),   // CoralWorkbench
        new CraftRecipe(5168, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5306, -1, 6) }),   // CoralToilet
        new CraftRecipe(5169, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 14) }),   // BalloonBathtub
        new CraftRecipe(5170, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 15),new CraftRequirement(225, -1, 5) }),   // BalloonBed
        new CraftRecipe(5171, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 20),new CraftRequirement(149, -1, 10) }),   // BalloonBookcase
        new CraftRecipe(5172, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 16) }),   // BalloonDresser
        new CraftRecipe(5173, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 5),new CraftRequirement(8, -1, 3) }),   // BalloonCandelabra
        new CraftRecipe(5174, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 4),new CraftRequirement(8, -1, 1) }),   // BalloonCandle
        new CraftRecipe(5175, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 4) }),   // BalloonChair
        new CraftRecipe(5176, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // BalloonChandelier
        new CraftRecipe(5177, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 8),new CraftRequirement(22, 28, 2) }),   // BalloonChest
        new CraftRecipe(5178, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // BalloonClock
        new CraftRecipe(5179, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 6) }),   // BalloonDoor
        new CraftRecipe(5180, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(3738, 19, 3) }),   // BalloonLamp
        new CraftRecipe(5181, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 6),new CraftRequirement(8, -1, 1) }),   // BalloonLantern
        new CraftRecipe(5182, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // BalloonPiano
        new CraftRecipe(5184, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 6),new CraftRequirement(206, -1, 1) }),   // BalloonSink
        new CraftRecipe(5185, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 5),new CraftRequirement(225, -1, 2) }),   // BalloonSofa
        new CraftRecipe(5186, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 8) }),   // BalloonTable
        new CraftRecipe(5187, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 10) }),   // BalloonWorkbench
        new CraftRecipe(5189, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(3738, 19, 6) }),   // BalloonToilet
        new CraftRecipe(5279, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 20) }),   // AshWoodHelmet
        new CraftRecipe(5280, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 30) }),   // AshWoodBreastplate
        new CraftRecipe(5281, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 25) }),   // AshWoodGreaves
        new CraftRecipe(5284, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 7) }),   // AshWoodSword
        new CraftRecipe(5283, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 8) }),   // AshWoodHammer
        new CraftRecipe(5282, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 10) }),   // AshWoodBow
        new CraftRecipe(5190, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 14) }),   // AshWoodBathtub
        new CraftRecipe(5191, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 15),new CraftRequirement(225, -1, 5) }),   // AshWoodBed
        new CraftRecipe(5192, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 20),new CraftRequirement(149, -1, 10) }),   // AshWoodBookcase
        new CraftRecipe(5193, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 16) }),   // AshWoodDresser
        new CraftRecipe(5194, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 5),new CraftRequirement(8, -1, 3) }),   // AshWoodCandelabra
        new CraftRecipe(5195, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 4),new CraftRequirement(8, -1, 1) }),   // AshWoodCandle
        new CraftRecipe(5196, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 4) }),   // AshWoodChair
        new CraftRecipe(5197, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // AshWoodChandelier
        new CraftRecipe(5198, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 8),new CraftRequirement(22, 28, 2) }),   // AshWoodChest
        new CraftRecipe(5199, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // AshWoodClock
        new CraftRecipe(5200, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 6) }),   // AshWoodDoor
        new CraftRecipe(5201, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5215, -1, 3) }),   // AshWoodLamp
        new CraftRecipe(5202, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 6),new CraftRequirement(8, -1, 1) }),   // AshWoodLantern
        new CraftRecipe(5203, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // AshWoodPiano
        new CraftRecipe(5205, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 6),new CraftRequirement(206, -1, 1) }),   // AshWoodSink
        new CraftRecipe(5206, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 5),new CraftRequirement(225, -1, 2) }),   // AshWoodSofa
        new CraftRecipe(5207, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 8) }),   // AshWoodTable
        new CraftRecipe(5208, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 10) }),   // AshWoodWorkbench
        new CraftRecipe(5210, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5215, -1, 6) }),   // AshWoodToilet
        new CraftRecipe(5548, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 14) }),   // AetheriumBathtub
        new CraftRecipe(5549, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 15),new CraftRequirement(225, -1, 5) }),   // AetheriumBed
        new CraftRecipe(5550, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 20),new CraftRequirement(149, -1, 10) }),   // AetheriumBookcase
        new CraftRecipe(5551, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 16) }),   // AetheriumDresser
        new CraftRecipe(5552, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 5),new CraftRequirement(8, -1, 3) }),   // AetheriumCandelabra
        new CraftRecipe(5553, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 4),new CraftRequirement(8, -1, 1) }),   // AetheriumCandle
        new CraftRecipe(5554, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 4) }),   // AetheriumChair
        new CraftRecipe(5555, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // AetheriumChandelier
        new CraftRecipe(5556, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 8),new CraftRequirement(22, 28, 2) }),   // AetheriumChest
        new CraftRecipe(5557, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // AetheriumClock
        new CraftRecipe(5558, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 6) }),   // AetheriumDoor
        new CraftRecipe(5559, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5398, -1, 3) }),   // AetheriumLamp
        new CraftRecipe(5560, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 6),new CraftRequirement(8, -1, 1) }),   // AetheriumLantern
        new CraftRecipe(5561, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // AetheriumPiano
        new CraftRecipe(5563, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 6),new CraftRequirement(206, -1, 1) }),   // AetheriumSink
        new CraftRecipe(5564, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 5),new CraftRequirement(225, -1, 2) }),   // AetheriumSofa
        new CraftRecipe(5565, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 8) }),   // AetheriumTable
        new CraftRecipe(5566, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 10) }),   // AetheriumWorkbench
        new CraftRecipe(5568, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5398, -1, 6) }),   // AetheriumToilet
        new CraftRecipe(5601, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 14) }),   // FallenStarBathtub
        new CraftRecipe(5602, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 15),new CraftRequirement(225, -1, 5) }),   // FallenStarBed
        new CraftRecipe(5603, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 20),new CraftRequirement(149, -1, 10) }),   // FallenStarBookcase
        new CraftRecipe(5604, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 16) }),   // FallenStarDresser
        new CraftRecipe(5605, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 5),new CraftRequirement(8, -1, 3) }),   // FallenStarCandelabra
        new CraftRecipe(5606, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 4),new CraftRequirement(8, -1, 1) }),   // FallenStarCandle
        new CraftRecipe(5607, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 4) }),   // FallenStarChair
        new CraftRecipe(5608, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // FallenStarChandelier
        new CraftRecipe(5609, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 8),new CraftRequirement(22, 28, 2) }),   // FallenStarChest
        new CraftRecipe(5610, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // FallenStarClock
        new CraftRecipe(5611, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 6) }),   // FallenStarDoor
        new CraftRecipe(5612, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5622, -1, 3) }),   // FallenStarLamp
        new CraftRecipe(5613, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 6),new CraftRequirement(8, -1, 1) }),   // FallenStarLantern
        new CraftRecipe(5614, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // FallenStarPiano
        new CraftRecipe(5616, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 6),new CraftRequirement(206, -1, 1) }),   // FallenStarSink
        new CraftRecipe(5617, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 5),new CraftRequirement(225, -1, 2) }),   // FallenStarSofa
        new CraftRecipe(5618, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 8) }),   // FallenStarTable
        new CraftRecipe(5619, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 10) }),   // FallenStarWorkbench
        new CraftRecipe(5621, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5622, -1, 6) }),   // FallenStarToilet
        new CraftRecipe(5689, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 0),new CraftRequirement(14, -1, 0) }),   // FeywoodBathtub
        new CraftRecipe(5690, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 15),new CraftRequirement(225, -1, 5) }),   // FeywoodBed
        new CraftRecipe(5691, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 20),new CraftRequirement(149, -1, 10) }),   // FeywoodBookcase
        new CraftRecipe(5692, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 0),new CraftRequirement(16, -1, 0) }),   // FeywoodDresser
        new CraftRecipe(5693, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 5),new CraftRequirement(8, -1, 3) }),   // FeywoodCandelabra
        new CraftRecipe(5694, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 4),new CraftRequirement(8, -1, 1) }),   // FeywoodCandle
        new CraftRecipe(5695, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 0),new CraftRequirement(4, -1, 0) }),   // FeywoodChair
        new CraftRecipe(5696, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // FeywoodChandelier
        new CraftRecipe(5697, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 8),new CraftRequirement(22, 28, 2) }),   // FeywoodChest
        new CraftRecipe(5698, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // FeywoodClock
        new CraftRecipe(5699, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 0),new CraftRequirement(6, -1, 0) }),   // FeywoodDoor
        new CraftRecipe(5700, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5710, -1, 3) }),   // FeywoodLamp
        new CraftRecipe(5701, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 6),new CraftRequirement(8, -1, 1) }),   // FeywoodLantern
        new CraftRecipe(5702, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // FeywoodPiano
        new CraftRecipe(5704, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 6),new CraftRequirement(206, -1, 1) }),   // FeywoodSink
        new CraftRecipe(5705, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 5),new CraftRequirement(225, -1, 2) }),   // FeywoodSofa
        new CraftRecipe(5706, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 0),new CraftRequirement(8, -1, 0) }),   // FeywoodTable
        new CraftRecipe(5707, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 0),new CraftRequirement(10, -1, 0) }),   // FeywoodWorkbench
        new CraftRecipe(5709, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(5710, -1, 0),new CraftRequirement(6, -1, 0) }),   // FeywoodToilet
        new CraftRecipe(5712, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 0),new CraftRequirement(14, -1, 0) }),   // HallowedBathtub
        new CraftRecipe(5713, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 15),new CraftRequirement(225, -1, 5) }),   // HallowedBed
        new CraftRecipe(5714, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 20),new CraftRequirement(149, -1, 10) }),   // HallowedBookcase
        new CraftRecipe(5715, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 0),new CraftRequirement(16, -1, 0) }),   // HallowedDresser
        new CraftRecipe(5716, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 5),new CraftRequirement(8, -1, 3) }),   // HallowedCandelabra
        new CraftRecipe(5717, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 4),new CraftRequirement(8, -1, 1) }),   // HallowedCandle
        new CraftRecipe(5718, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 0),new CraftRequirement(4, -1, 0) }),   // HallowedChair
        new CraftRecipe(5719, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // HallowedChandelier
        new CraftRecipe(5720, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 8),new CraftRequirement(22, 28, 2) }),   // HallowedFurnitureChest
        new CraftRecipe(5721, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // HallowedClock
        new CraftRecipe(5722, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 0),new CraftRequirement(6, -1, 0) }),   // HallowedDoor
        new CraftRecipe(5723, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5733, -1, 3) }),   // HallowedLamp
        new CraftRecipe(5724, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 6),new CraftRequirement(8, -1, 1) }),   // HallowedLantern
        new CraftRecipe(5725, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // HallowedPiano
        new CraftRecipe(5727, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 6),new CraftRequirement(206, -1, 1) }),   // HallowedSink
        new CraftRecipe(5728, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 5),new CraftRequirement(225, -1, 2) }),   // HallowedSofa
        new CraftRecipe(5729, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 0),new CraftRequirement(8, -1, 0) }),   // HallowedTable
        new CraftRecipe(5730, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 0),new CraftRequirement(10, -1, 0) }),   // HallowedWorkbench
        new CraftRecipe(5732, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5733, -1, 0),new CraftRequirement(6, -1, 0) }),   // HallowedToilet
        new CraftRecipe(5858, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 0),new CraftRequirement(14, -1, 0) }),   // EasterBathtub
        new CraftRecipe(5859, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 15),new CraftRequirement(225, -1, 5) }),   // EasterBed
        new CraftRecipe(5860, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 20),new CraftRequirement(149, -1, 10) }),   // EasterBookcase
        new CraftRecipe(5868, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 0),new CraftRequirement(16, -1, 0) }),   // EasterDresser
        new CraftRecipe(5861, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 5),new CraftRequirement(8, -1, 3) }),   // EasterCandelabra
        new CraftRecipe(5862, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 4),new CraftRequirement(8, -1, 1) }),   // EasterCandle
        new CraftRecipe(5863, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 0),new CraftRequirement(4, -1, 0) }),   // EasterChair
        new CraftRecipe(5864, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // EasterChandelier
        new CraftRecipe(5865, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 8),new CraftRequirement(22, 28, 2) }),   // EasterChest
        new CraftRecipe(5866, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // EasterClock
        new CraftRecipe(5867, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 0),new CraftRequirement(6, -1, 0) }),   // EasterDoor
        new CraftRecipe(5869, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5920, -1, 3) }),   // EasterLamp
        new CraftRecipe(5870, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 6),new CraftRequirement(8, -1, 1) }),   // EasterLantern
        new CraftRecipe(5871, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // EasterPiano
        new CraftRecipe(5873, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 6),new CraftRequirement(206, -1, 1) }),   // EasterSink
        new CraftRecipe(5874, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 5),new CraftRequirement(225, -1, 2) }),   // EasterSofa
        new CraftRecipe(5875, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 0),new CraftRequirement(8, -1, 0) }),   // EasterTable
        new CraftRecipe(5877, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 0),new CraftRequirement(10, -1, 0) }),   // EasterWorkbench
        new CraftRecipe(5876, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5920, -1, 0),new CraftRequirement(6, -1, 0) }),   // EasterToilet
        new CraftRecipe(5739, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 0),new CraftRequirement(14, -1, 0) }),   // GothicBathtub
        new CraftRecipe(5740, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 15),new CraftRequirement(225, -1, 5) }),   // GothicBed
        new CraftRecipe(1512, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 20),new CraftRequirement(149, -1, 10) }),   // GothicBookcase
        new CraftRecipe(5741, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 0),new CraftRequirement(16, -1, 0) }),   // GothicDresser
        new CraftRecipe(5742, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 5),new CraftRequirement(8, -1, 3) }),   // GothicCandelabra
        new CraftRecipe(5743, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 4),new CraftRequirement(8, -1, 1) }),   // GothicCandle
        new CraftRecipe(1509, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 0),new CraftRequirement(4, -1, 0) }),   // GothicChair
        new CraftRecipe(5744, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // GothicChandelier
        new CraftRecipe(5745, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 8),new CraftRequirement(22, 28, 2) }),   // GothicChest
        new CraftRecipe(5746, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // GothicClock
        new CraftRecipe(5747, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 0),new CraftRequirement(6, -1, 0) }),   // GothicDoor
        new CraftRecipe(5748, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5922, -1, 3) }),   // GothicLamp
        new CraftRecipe(5749, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 6),new CraftRequirement(8, -1, 1) }),   // GothicLantern
        new CraftRecipe(5750, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // GothicPiano
        new CraftRecipe(5752, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 6),new CraftRequirement(206, -1, 1) }),   // GothicSink
        new CraftRecipe(5753, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 5),new CraftRequirement(225, -1, 2) }),   // GothicSofa
        new CraftRecipe(1510, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 0),new CraftRequirement(8, -1, 0) }),   // GothicTable
        new CraftRecipe(1511, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 0),new CraftRequirement(10, -1, 0) }),   // GothicWorkBench
        new CraftRecipe(5755, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(5922, -1, 0),new CraftRequirement(6, -1, 0) }),   // GothicToilet
        new CraftRecipe(5756, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 0),new CraftRequirement(14, -1, 0) }),   // DemoniteBathtub
        new CraftRecipe(5757, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 15),new CraftRequirement(225, -1, 5) }),   // DemoniteBed
        new CraftRecipe(5758, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 20),new CraftRequirement(149, -1, 10) }),   // DemoniteBookcase
        new CraftRecipe(5766, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 0),new CraftRequirement(16, -1, 0) }),   // DemoniteDresser
        new CraftRecipe(5759, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 5),new CraftRequirement(8, -1, 3) }),   // DemoniteCandelabra
        new CraftRecipe(5760, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 4),new CraftRequirement(8, -1, 1) }),   // DemoniteCandle
        new CraftRecipe(5761, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 0),new CraftRequirement(4, -1, 0) }),   // DemoniteChair
        new CraftRecipe(5762, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // DemoniteChandelier
        new CraftRecipe(5763, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 8),new CraftRequirement(22, 28, 2) }),   // DemoniteChest
        new CraftRecipe(5764, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // DemoniteClock
        new CraftRecipe(5765, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 0),new CraftRequirement(6, -1, 0) }),   // DemoniteDoor
        new CraftRecipe(5767, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(577, -1, 3) }),   // DemoniteLamp
        new CraftRecipe(5768, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 6),new CraftRequirement(8, -1, 1) }),   // DemoniteLantern
        new CraftRecipe(5769, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // DemonitePiano
        new CraftRecipe(5771, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 6),new CraftRequirement(206, -1, 1) }),   // DemoniteSink
        new CraftRecipe(5772, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 5),new CraftRequirement(225, -1, 2) }),   // DemoniteSofa
        new CraftRecipe(5773, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 0),new CraftRequirement(8, -1, 0) }),   // DemoniteTable
        new CraftRecipe(5775, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 0),new CraftRequirement(10, -1, 0) }),   // DemoniteWorkbench
        new CraftRecipe(5774, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(577, -1, 0),new CraftRequirement(6, -1, 0) }),   // DemoniteToilet
        new CraftRecipe(5777, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 0),new CraftRequirement(14, -1, 0) }),   // CrimtaneBathtub
        new CraftRecipe(5778, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 15),new CraftRequirement(225, -1, 5) }),   // CrimtaneBed
        new CraftRecipe(5779, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 20),new CraftRequirement(149, -1, 10) }),   // CrimtaneBookcase
        new CraftRecipe(5787, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 0),new CraftRequirement(16, -1, 0) }),   // CrimtaneDresser
        new CraftRecipe(5780, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 5),new CraftRequirement(8, -1, 3) }),   // CrimtaneCandelabra
        new CraftRecipe(5781, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 4),new CraftRequirement(8, -1, 1) }),   // CrimtaneCandle
        new CraftRecipe(5782, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 0),new CraftRequirement(4, -1, 0) }),   // CrimtaneChair
        new CraftRecipe(5783, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // CrimtaneChandelier
        new CraftRecipe(5784, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 8),new CraftRequirement(22, 28, 2) }),   // CrimtaneChest
        new CraftRecipe(5785, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // CrimtaneClock
        new CraftRecipe(5786, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 0),new CraftRequirement(6, -1, 0) }),   // CrimtaneDoor
        new CraftRecipe(5788, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(2793, -1, 3) }),   // CrimtaneLamp
        new CraftRecipe(5789, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 6),new CraftRequirement(8, -1, 1) }),   // CrimtaneLantern
        new CraftRecipe(5790, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // CrimtanePiano
        new CraftRecipe(5792, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 6),new CraftRequirement(206, -1, 1) }),   // CrimtaneSink
        new CraftRecipe(5793, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 5),new CraftRequirement(225, -1, 2) }),   // CrimtaneSofa
        new CraftRecipe(5794, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 0),new CraftRequirement(8, -1, 0) }),   // CrimtaneTable
        new CraftRecipe(5796, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 0),new CraftRequirement(10, -1, 0) }),   // CrimtaneWorkbench
        new CraftRecipe(5795, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(2793, -1, 0),new CraftRequirement(6, -1, 0) }),   // CrimtaneToilet
        new CraftRecipe(5798, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 0),new CraftRequirement(14, -1, 0) }),   // SnowBathtub
        new CraftRecipe(5799, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 15),new CraftRequirement(225, -1, 5) }),   // SnowBed
        new CraftRecipe(5800, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 20),new CraftRequirement(149, -1, 10) }),   // SnowBookcase
        new CraftRecipe(5808, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 0),new CraftRequirement(16, -1, 0) }),   // SnowDresser
        new CraftRecipe(5801, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 5),new CraftRequirement(8, -1, 3) }),   // SnowCandelabra
        new CraftRecipe(5802, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 4),new CraftRequirement(8, -1, 1) }),   // SnowCandle
        new CraftRecipe(5803, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 0),new CraftRequirement(4, -1, 0) }),   // SnowChair
        new CraftRecipe(5804, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // SnowChandelier
        new CraftRecipe(5805, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 8),new CraftRequirement(22, 28, 2) }),   // SnowChest
        new CraftRecipe(5806, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // SnowClock
        new CraftRecipe(5807, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 0),new CraftRequirement(6, -1, 0) }),   // SnowDoor
        new CraftRecipe(5809, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(593, -1, 3) }),   // SnowLamp
        new CraftRecipe(5810, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 6),new CraftRequirement(8, -1, 1) }),   // SnowLantern
        new CraftRecipe(5811, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // SnowPiano
        new CraftRecipe(5813, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 6),new CraftRequirement(206, -1, 1) }),   // SnowSink
        new CraftRecipe(5814, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 5),new CraftRequirement(225, -1, 2) }),   // SnowSofa
        new CraftRecipe(5815, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 0),new CraftRequirement(8, -1, 0) }),   // SnowTable
        new CraftRecipe(5817, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 0),new CraftRequirement(10, -1, 0) }),   // SnowWorkbench
        new CraftRecipe(5816, 1, 306, CraftEnvironment.None, new[] { new CraftRequirement(593, -1, 0),new CraftRequirement(6, -1, 0) }),   // SnowToilet
        new CraftRecipe(5819, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 0),new CraftRequirement(14, -1, 0) }),   // FlinxFurBathtub
        new CraftRecipe(5820, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 15),new CraftRequirement(225, -1, 5) }),   // FlinxFurBed
        new CraftRecipe(5821, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 20),new CraftRequirement(149, -1, 10) }),   // FlinxFurBookcase
        new CraftRecipe(5829, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 0),new CraftRequirement(16, -1, 0) }),   // FlinxFurDresser
        new CraftRecipe(5822, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 5),new CraftRequirement(8, -1, 3) }),   // FlinxFurCandelabra
        new CraftRecipe(5823, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 4),new CraftRequirement(8, -1, 1) }),   // FlinxFurCandle
        new CraftRecipe(5824, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 0),new CraftRequirement(4, -1, 0) }),   // FlinxFurChair
        new CraftRecipe(5825, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // FlinxFurChandelier
        new CraftRecipe(5826, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 8),new CraftRequirement(22, 28, 2) }),   // FlinxFurChest
        new CraftRecipe(5827, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // FlinxFurClock
        new CraftRecipe(5828, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 0),new CraftRequirement(6, -1, 0) }),   // FlinxFurDoor
        new CraftRecipe(5830, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5924, -1, 3) }),   // FlinxFurLamp
        new CraftRecipe(5831, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 6),new CraftRequirement(8, -1, 1) }),   // FlinxFurLantern
        new CraftRecipe(5832, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // FlinxFurPiano
        new CraftRecipe(5834, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 6),new CraftRequirement(206, -1, 1) }),   // FlinxFurSink
        new CraftRecipe(5835, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 5),new CraftRequirement(225, -1, 2) }),   // FlinxFurSofa
        new CraftRecipe(5836, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 0),new CraftRequirement(8, -1, 0) }),   // FlinxFurTable
        new CraftRecipe(5838, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 0),new CraftRequirement(10, -1, 0) }),   // FlinxFurWorkbench
        new CraftRecipe(5837, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5924, -1, 0),new CraftRequirement(6, -1, 0) }),   // FlinxFurToilet
        new CraftRecipe(5840, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 0),new CraftRequirement(14, -1, 0) }),   // PineBathtub
        new CraftRecipe(5841, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 15),new CraftRequirement(225, -1, 5) }),   // PineBed
        new CraftRecipe(5842, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 20),new CraftRequirement(149, -1, 10) }),   // PineBookcase
        new CraftRecipe(5848, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 0),new CraftRequirement(16, -1, 0) }),   // PineDresser
        new CraftRecipe(5843, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 5),new CraftRequirement(8, -1, 3) }),   // PineCandelabra
        new CraftRecipe(5844, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 4),new CraftRequirement(8, -1, 1) }),   // PineCandle
        new CraftRecipe(1925, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 0),new CraftRequirement(4, -1, 0) }),   // PineChair
        new CraftRecipe(5845, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // PineChandelier
        new CraftRecipe(5846, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 8),new CraftRequirement(22, 28, 2) }),   // PineChest
        new CraftRecipe(5847, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // PineClock
        new CraftRecipe(1924, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 0),new CraftRequirement(6, -1, 0) }),   // PineDoor
        new CraftRecipe(5849, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(1872, -1, 3) }),   // PineLamp
        new CraftRecipe(5850, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 6),new CraftRequirement(8, -1, 1) }),   // PineLantern
        new CraftRecipe(5851, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // PinePiano
        new CraftRecipe(5853, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 6),new CraftRequirement(206, -1, 1) }),   // PineSink
        new CraftRecipe(5854, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 5),new CraftRequirement(225, -1, 2) }),   // PineSofa
        new CraftRecipe(1926, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 0),new CraftRequirement(8, -1, 0) }),   // PineTable
        new CraftRecipe(5856, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 0),new CraftRequirement(10, -1, 0) }),   // PineWorkbench
        new CraftRecipe(5855, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(1872, -1, 0),new CraftRequirement(6, -1, 0) }),   // PineToilet
        new CraftRecipe(5840, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 0),new CraftRequirement(14, -1, 0) }),   // PineBathtub
        new CraftRecipe(5841, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 15),new CraftRequirement(225, -1, 5) }),   // PineBed
        new CraftRecipe(5842, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 20),new CraftRequirement(149, -1, 10) }),   // PineBookcase
        new CraftRecipe(5848, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 0),new CraftRequirement(16, -1, 0) }),   // PineDresser
        new CraftRecipe(5843, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 5),new CraftRequirement(8, -1, 3) }),   // PineCandelabra
        new CraftRecipe(5844, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 4),new CraftRequirement(8, -1, 1) }),   // PineCandle
        new CraftRecipe(1925, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 0),new CraftRequirement(4, -1, 0) }),   // PineChair
        new CraftRecipe(5845, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // PineChandelier
        new CraftRecipe(5846, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 8),new CraftRequirement(22, 28, 2) }),   // PineChest
        new CraftRecipe(5847, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // PineClock
        new CraftRecipe(1924, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 0),new CraftRequirement(6, -1, 0) }),   // PineDoor
        new CraftRecipe(5849, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5930, -1, 3) }),   // PineLamp
        new CraftRecipe(5850, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 6),new CraftRequirement(8, -1, 1) }),   // PineLantern
        new CraftRecipe(5851, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // PinePiano
        new CraftRecipe(5853, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 6),new CraftRequirement(206, -1, 1) }),   // PineSink
        new CraftRecipe(5854, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 5),new CraftRequirement(225, -1, 2) }),   // PineSofa
        new CraftRecipe(1926, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 0),new CraftRequirement(8, -1, 0) }),   // PineTable
        new CraftRecipe(5856, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 0),new CraftRequirement(10, -1, 0) }),   // PineWorkbench
        new CraftRecipe(5855, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5930, -1, 0),new CraftRequirement(6, -1, 0) }),   // PineToilet
        new CraftRecipe(5879, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 0),new CraftRequirement(14, -1, 0) }),   // StoneBathtub
        new CraftRecipe(5880, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 15),new CraftRequirement(225, -1, 5) }),   // StoneBed
        new CraftRecipe(5881, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 20),new CraftRequirement(149, -1, 10) }),   // StoneBookcase
        new CraftRecipe(5888, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 0),new CraftRequirement(16, -1, 0) }),   // StoneDresser
        new CraftRecipe(5882, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 5),new CraftRequirement(8, -1, 3) }),   // StoneCandelabra
        new CraftRecipe(5883, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 4),new CraftRequirement(8, -1, 1) }),   // StoneCandle
        new CraftRecipe(5884, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 0),new CraftRequirement(4, -1, 0) }),   // StoneChair
        new CraftRecipe(5885, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // StoneChandelier
        new CraftRecipe(5886, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 8),new CraftRequirement(22, 28, 2) }),   // StoneChest
        new CraftRecipe(5887, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // StoneClock
        new CraftRecipe(4415, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 0),new CraftRequirement(6, -1, 0) }),   // StoneDoor
        new CraftRecipe(5889, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(129, -1, 3) }),   // StoneLamp
        new CraftRecipe(5890, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 6),new CraftRequirement(8, -1, 1) }),   // StoneLantern
        new CraftRecipe(5891, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // StonePiano
        new CraftRecipe(5892, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 6),new CraftRequirement(206, -1, 1) }),   // StoneSink
        new CraftRecipe(5893, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 5),new CraftRequirement(225, -1, 2) }),   // StoneSofa
        new CraftRecipe(5894, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 0),new CraftRequirement(8, -1, 0) }),   // StoneTable
        new CraftRecipe(5896, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 0),new CraftRequirement(10, -1, 0) }),   // StoneWorkbench
        new CraftRecipe(5895, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(129, -1, 0),new CraftRequirement(6, -1, 0) }),   // StoneToilet
        new CraftRecipe(5898, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 0),new CraftRequirement(14, -1, 0) }),   // JellyfishBathtub
        new CraftRecipe(5899, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 15),new CraftRequirement(225, -1, 5) }),   // JellyfishBed
        new CraftRecipe(5900, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 20),new CraftRequirement(149, -1, 10) }),   // JellyfishBookcase
        new CraftRecipe(5908, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 0),new CraftRequirement(16, -1, 0) }),   // JellyfishDresser
        new CraftRecipe(5901, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 5),new CraftRequirement(8, -1, 3) }),   // JellyfishCandelabra
        new CraftRecipe(5902, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 4),new CraftRequirement(8, -1, 1) }),   // JellyfishCandle
        new CraftRecipe(5903, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 0),new CraftRequirement(4, -1, 0) }),   // JellyfishChair
        new CraftRecipe(5904, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // JellyfishChandelier
        new CraftRecipe(5905, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 8),new CraftRequirement(22, 28, 2) }),   // JellyfishChest
        new CraftRecipe(5906, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // JellyfishClock
        new CraftRecipe(5907, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 0),new CraftRequirement(6, -1, 0) }),   // JellyfishDoor
        new CraftRecipe(5909, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5926, -1, 3) }),   // JellyfishLamp
        new CraftRecipe(5910, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 6),new CraftRequirement(8, -1, 1) }),   // JellyfishLantern
        new CraftRecipe(5911, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // JellyfishPiano
        new CraftRecipe(5913, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 6),new CraftRequirement(206, -1, 1) }),   // JellyfishSink
        new CraftRecipe(5914, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 5),new CraftRequirement(225, -1, 2) }),   // JellyfishSofa
        new CraftRecipe(5915, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 0),new CraftRequirement(8, -1, 0) }),   // JellyfishTable
        new CraftRecipe(5917, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 0),new CraftRequirement(10, -1, 0) }),   // JellyfishWorkbench
        new CraftRecipe(5916, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(5926, -1, 0),new CraftRequirement(6, -1, 0) }),   // JellyfishToilet
        new CraftRecipe(5932, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 0),new CraftRequirement(14, -1, 0) }),   // HarpyBathtub
        new CraftRecipe(5933, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 15),new CraftRequirement(225, -1, 5) }),   // HarpyBed
        new CraftRecipe(5934, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 20),new CraftRequirement(149, -1, 10) }),   // HarpyBookcase
        new CraftRecipe(5942, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 0),new CraftRequirement(16, -1, 0) }),   // HarpyDresser
        new CraftRecipe(5935, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 5),new CraftRequirement(8, -1, 3) }),   // HarpyCandelabra
        new CraftRecipe(5936, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 4),new CraftRequirement(8, -1, 1) }),   // HarpyCandle
        new CraftRecipe(5937, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 0),new CraftRequirement(4, -1, 0) }),   // HarpyChair
        new CraftRecipe(5938, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // HarpyChandelier
        new CraftRecipe(5939, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 8),new CraftRequirement(22, 28, 2) }),   // HarpyChest
        new CraftRecipe(5940, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // HarpyClock
        new CraftRecipe(5941, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 0),new CraftRequirement(6, -1, 0) }),   // HarpyDoor
        new CraftRecipe(5943, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5953, -1, 3) }),   // HarpyLamp
        new CraftRecipe(5944, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 6),new CraftRequirement(8, -1, 1) }),   // HarpyLantern
        new CraftRecipe(5945, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // HarpyPiano
        new CraftRecipe(5947, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 6),new CraftRequirement(206, -1, 1) }),   // HarpySink
        new CraftRecipe(5948, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 5),new CraftRequirement(225, -1, 2) }),   // HarpySofa
        new CraftRecipe(5949, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 0),new CraftRequirement(8, -1, 0) }),   // HarpyTable
        new CraftRecipe(5951, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 0),new CraftRequirement(10, -1, 0) }),   // HarpyWorkbench
        new CraftRecipe(5950, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5953, -1, 0),new CraftRequirement(6, -1, 0) }),   // HarpyToilet
        new CraftRecipe(5955, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 0),new CraftRequirement(14, -1, 0) }),   // CloudBathtub
        new CraftRecipe(5956, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 15),new CraftRequirement(225, -1, 5) }),   // CloudBed
        new CraftRecipe(5957, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 20),new CraftRequirement(149, -1, 10) }),   // CloudBookcase
        new CraftRecipe(5965, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 0),new CraftRequirement(16, -1, 0) }),   // CloudDresser
        new CraftRecipe(5958, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 5),new CraftRequirement(8, -1, 3) }),   // CloudCandelabra
        new CraftRecipe(5959, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 4),new CraftRequirement(8, -1, 1) }),   // CloudCandle
        new CraftRecipe(5960, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 0),new CraftRequirement(4, -1, 0) }),   // CloudChair
        new CraftRecipe(5961, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // CloudChandelier
        new CraftRecipe(5962, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 8),new CraftRequirement(22, 28, 2) }),   // CloudChest
        new CraftRecipe(5963, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // CloudClock
        new CraftRecipe(5964, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 0),new CraftRequirement(6, -1, 0) }),   // CloudDoor
        new CraftRecipe(5966, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(751, -1, 3) }),   // CloudLamp
        new CraftRecipe(5967, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 6),new CraftRequirement(8, -1, 1) }),   // CloudLantern
        new CraftRecipe(5968, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // CloudPiano
        new CraftRecipe(5969, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 6),new CraftRequirement(206, -1, 1) }),   // CloudSink
        new CraftRecipe(5970, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 5),new CraftRequirement(225, -1, 2) }),   // CloudSofa
        new CraftRecipe(5971, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 0),new CraftRequirement(8, -1, 0) }),   // CloudTable
        new CraftRecipe(5973, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 0),new CraftRequirement(10, -1, 0) }),   // CloudWorkbench
        new CraftRecipe(5972, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(751, -1, 0),new CraftRequirement(6, -1, 0) }),   // CloudToilet
        new CraftRecipe(5975, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 0),new CraftRequirement(14, -1, 0) }),   // MoonplateBathtub
        new CraftRecipe(5976, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 15),new CraftRequirement(225, -1, 5) }),   // MoonplateBed
        new CraftRecipe(5977, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 20),new CraftRequirement(149, -1, 10) }),   // MoonplateBookcase
        new CraftRecipe(5985, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 0),new CraftRequirement(16, -1, 0) }),   // MoonplateDresser
        new CraftRecipe(5978, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 5),new CraftRequirement(8, -1, 3) }),   // MoonplateCandelabra
        new CraftRecipe(5979, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 4),new CraftRequirement(8, -1, 1) }),   // MoonplateCandle
        new CraftRecipe(5980, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 0),new CraftRequirement(4, -1, 0) }),   // MoonplateChair
        new CraftRecipe(5981, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // MoonplateChandelier
        new CraftRecipe(5982, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 8),new CraftRequirement(22, 28, 2) }),   // MoonplateChest
        new CraftRecipe(5983, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // MoonplateClock
        new CraftRecipe(5984, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 0),new CraftRequirement(6, -1, 0) }),   // MoonplateDoor
        new CraftRecipe(5986, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(5996, -1, 3) }),   // MoonplateLamp
        new CraftRecipe(5987, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 6),new CraftRequirement(8, -1, 1) }),   // MoonplateLantern
        new CraftRecipe(5988, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // MoonplatePiano
        new CraftRecipe(5990, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 6),new CraftRequirement(206, -1, 1) }),   // MoonplateSink
        new CraftRecipe(5991, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 5),new CraftRequirement(225, -1, 2) }),   // MoonplateSofa
        new CraftRecipe(5992, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 0),new CraftRequirement(8, -1, 0) }),   // MoonplateTable
        new CraftRecipe(5994, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 0),new CraftRequirement(10, -1, 0) }),   // MoonplateWorkbench
        new CraftRecipe(5993, 1, 305, CraftEnvironment.None, new[] { new CraftRequirement(5996, -1, 0),new CraftRequirement(6, -1, 0) }),   // MoonplateToilet
        new CraftRecipe(5998, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 0),new CraftRequirement(14, -1, 0) }),   // LibrarianBathtub
        new CraftRecipe(5999, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 15),new CraftRequirement(225, -1, 5) }),   // LibrarianBed
        new CraftRecipe(6000, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 20),new CraftRequirement(149, -1, 10) }),   // LibrarianBookcase
        new CraftRecipe(6008, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 0),new CraftRequirement(16, -1, 0) }),   // LibrarianDresser
        new CraftRecipe(6001, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 5),new CraftRequirement(8, -1, 3) }),   // LibrarianCandelabra
        new CraftRecipe(6002, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 4),new CraftRequirement(8, -1, 1) }),   // LibrarianCandle
        new CraftRecipe(6003, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 0),new CraftRequirement(4, -1, 0) }),   // LibrarianChair
        new CraftRecipe(6004, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // LibrarianChandelier
        new CraftRecipe(6005, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 8),new CraftRequirement(22, 28, 2) }),   // LibrarianChest
        new CraftRecipe(6006, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // LibrarianClock
        new CraftRecipe(6007, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 0),new CraftRequirement(6, -1, 0) }),   // LibrarianDoor
        new CraftRecipe(6009, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(6019, -1, 3) }),   // LibrarianLamp
        new CraftRecipe(6010, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 6),new CraftRequirement(8, -1, 1) }),   // LibrarianLantern
        new CraftRecipe(6011, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // LibrarianPiano
        new CraftRecipe(6013, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 6),new CraftRequirement(206, -1, 1) }),   // LibrarianSink
        new CraftRecipe(6014, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 5),new CraftRequirement(225, -1, 2) }),   // LibrarianSofa
        new CraftRecipe(6015, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 0),new CraftRequirement(8, -1, 0) }),   // LibrarianTable
        new CraftRecipe(6017, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 0),new CraftRequirement(10, -1, 0) }),   // LibrarianWorkbench
        new CraftRecipe(6016, 1, 101, CraftEnvironment.None, new[] { new CraftRequirement(6019, -1, 0),new CraftRequirement(6, -1, 0) }),   // LibrarianToilet
        new CraftRecipe(6021, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 0),new CraftRequirement(14, -1, 0) }),   // SpikeBathtub
        new CraftRecipe(6022, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 15),new CraftRequirement(225, -1, 5) }),   // SpikeBed
        new CraftRecipe(6023, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 20),new CraftRequirement(149, -1, 10) }),   // SpikeBookcase
        new CraftRecipe(6031, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 0),new CraftRequirement(16, -1, 0) }),   // SpikeDresser
        new CraftRecipe(6024, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 5),new CraftRequirement(8, -1, 3) }),   // SpikeCandelabra
        new CraftRecipe(6025, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 4),new CraftRequirement(8, -1, 1) }),   // SpikeCandle
        new CraftRecipe(6026, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 0),new CraftRequirement(4, -1, 0) }),   // SpikeChair
        new CraftRecipe(6027, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // SpikeChandelier
        new CraftRecipe(6028, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 8),new CraftRequirement(22, 28, 2) }),   // SpikeChest
        new CraftRecipe(6029, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // SpikeClock
        new CraftRecipe(6030, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 0),new CraftRequirement(6, -1, 0) }),   // SpikeDoor
        new CraftRecipe(6032, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(6042, -1, 3) }),   // SpikeLamp
        new CraftRecipe(6033, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 6),new CraftRequirement(8, -1, 1) }),   // SpikeLantern
        new CraftRecipe(6034, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // SpikePiano
        new CraftRecipe(6036, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 6),new CraftRequirement(206, -1, 1) }),   // SpikeSink
        new CraftRecipe(6037, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 5),new CraftRequirement(225, -1, 2) }),   // SpikeSofa
        new CraftRecipe(6038, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 0),new CraftRequirement(8, -1, 0) }),   // SpikeTable
        new CraftRecipe(6040, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 0),new CraftRequirement(10, -1, 0) }),   // SpikeWorkbench
        new CraftRecipe(6039, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6042, -1, 0),new CraftRequirement(6, -1, 0) }),   // SpikeToilet
        new CraftRecipe(6044, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 0),new CraftRequirement(14, -1, 0) }),   // OfficeBathtub
        new CraftRecipe(6045, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 15),new CraftRequirement(225, -1, 5) }),   // OfficeBed
        new CraftRecipe(6046, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 20),new CraftRequirement(149, -1, 10) }),   // OfficeBookcase
        new CraftRecipe(6054, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 0),new CraftRequirement(16, -1, 0) }),   // OfficeDresser
        new CraftRecipe(6047, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 5),new CraftRequirement(8, -1, 3) }),   // OfficeCandelabra
        new CraftRecipe(6048, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 4),new CraftRequirement(8, -1, 1) }),   // OfficeCandle
        new CraftRecipe(6049, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 0),new CraftRequirement(4, -1, 0) }),   // OfficeChair
        new CraftRecipe(6050, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // OfficeChandelier
        new CraftRecipe(6051, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 8),new CraftRequirement(22, 28, 2) }),   // OfficeChest
        new CraftRecipe(6052, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // OfficeClock
        new CraftRecipe(6053, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 0),new CraftRequirement(6, -1, 0) }),   // OfficeDoor
        new CraftRecipe(6055, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(6065, -1, 3) }),   // OfficeLamp
        new CraftRecipe(6056, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 6),new CraftRequirement(8, -1, 1) }),   // OfficeLantern
        new CraftRecipe(6057, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // OfficePiano
        new CraftRecipe(6059, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 6),new CraftRequirement(206, -1, 1) }),   // OfficeSink
        new CraftRecipe(6060, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 5),new CraftRequirement(225, -1, 2) }),   // OfficeSofa
        new CraftRecipe(6061, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 0),new CraftRequirement(8, -1, 0) }),   // OfficeTable
        new CraftRecipe(6063, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 0),new CraftRequirement(10, -1, 0) }),   // OfficeWorkbench
        new CraftRecipe(6062, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6065, -1, 0),new CraftRequirement(6, -1, 0) }),   // OfficeToilet
        new CraftRecipe(6067, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 0),new CraftRequirement(14, -1, 0) }),   // ForbiddenBathtub
        new CraftRecipe(6068, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 15),new CraftRequirement(225, -1, 5) }),   // ForbiddenBed
        new CraftRecipe(6069, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 20),new CraftRequirement(149, -1, 10) }),   // ForbiddenBookcase
        new CraftRecipe(6077, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 0),new CraftRequirement(16, -1, 0) }),   // ForbiddenDresser
        new CraftRecipe(6070, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 5),new CraftRequirement(8, -1, 3) }),   // ForbiddenCandelabra
        new CraftRecipe(6071, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 4),new CraftRequirement(8, -1, 1) }),   // ForbiddenCandle
        new CraftRecipe(6072, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 0),new CraftRequirement(4, -1, 0) }),   // ForbiddenChair
        new CraftRecipe(6073, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // ForbiddenChandelier
        new CraftRecipe(6074, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 8),new CraftRequirement(22, 28, 2) }),   // ForbiddenChest
        new CraftRecipe(6075, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // ForbiddenClock
        new CraftRecipe(6076, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 0),new CraftRequirement(6, -1, 0) }),   // ForbiddenDoor
        new CraftRecipe(6078, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(6088, -1, 3) }),   // ForbiddenLamp
        new CraftRecipe(6079, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 6),new CraftRequirement(8, -1, 1) }),   // ForbiddenLantern
        new CraftRecipe(6080, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // ForbiddenPiano
        new CraftRecipe(6082, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 6),new CraftRequirement(206, -1, 1) }),   // ForbiddenSink
        new CraftRecipe(6083, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 5),new CraftRequirement(225, -1, 2) }),   // ForbiddenSofa
        new CraftRecipe(6084, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 0),new CraftRequirement(8, -1, 0) }),   // ForbiddenTable
        new CraftRecipe(6086, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 0),new CraftRequirement(10, -1, 0) }),   // ForbiddenWorkbench
        new CraftRecipe(6085, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6088, -1, 0),new CraftRequirement(6, -1, 0) }),   // ForbiddenToilet
        new CraftRecipe(6090, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 0),new CraftRequirement(14, -1, 0) }),   // WaterBathtub
        new CraftRecipe(6091, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 15),new CraftRequirement(225, -1, 5) }),   // WaterBed
        new CraftRecipe(6092, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 20),new CraftRequirement(149, -1, 10) }),   // WaterBookcase
        new CraftRecipe(6099, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 0),new CraftRequirement(16, -1, 0) }),   // WaterDresser
        new CraftRecipe(6093, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 5),new CraftRequirement(8, -1, 3) }),   // WaterCandelabra
        new CraftRecipe(6094, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 4),new CraftRequirement(8, -1, 1) }),   // WaterFurnitureCandle
        new CraftRecipe(6095, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 0),new CraftRequirement(4, -1, 0) }),   // WaterChair
        new CraftRecipe(6096, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // WaterChandelier
        new CraftRecipe(1298, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 8),new CraftRequirement(22, 28, 2) }),   // WaterChest
        new CraftRecipe(6097, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // WaterClock
        new CraftRecipe(6098, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 0),new CraftRequirement(6, -1, 0) }),   // WaterDoor
        new CraftRecipe(6100, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(6109, -1, 3) }),   // WaterLamp
        new CraftRecipe(6101, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 6),new CraftRequirement(8, -1, 1) }),   // WaterLantern
        new CraftRecipe(6102, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // WaterPiano
        new CraftRecipe(6104, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 6),new CraftRequirement(206, -1, 1) }),   // WaterSink
        new CraftRecipe(6105, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 5),new CraftRequirement(225, -1, 2) }),   // WaterSofa
        new CraftRecipe(6106, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 0),new CraftRequirement(8, -1, 0) }),   // WaterTable
        new CraftRecipe(6108, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 0),new CraftRequirement(10, -1, 0) }),   // WaterWorkbench
        new CraftRecipe(6107, 1, 106, CraftEnvironment.None, new[] { new CraftRequirement(6109, -1, 0),new CraftRequirement(6, -1, 0) }),   // WaterToilet
        new CraftRecipe(6111, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 0),new CraftRequirement(14, -1, 0) }),   // BoulderBathtub
        new CraftRecipe(6112, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 15),new CraftRequirement(225, -1, 5) }),   // BoulderBed
        new CraftRecipe(6113, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 20),new CraftRequirement(149, -1, 10) }),   // BoulderBookcase
        new CraftRecipe(6121, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 0),new CraftRequirement(16, -1, 0) }),   // BoulderDresser
        new CraftRecipe(6114, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 5),new CraftRequirement(8, -1, 3) }),   // BoulderCandelabra
        new CraftRecipe(6115, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 4),new CraftRequirement(8, -1, 1) }),   // BoulderCandle
        new CraftRecipe(6116, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 0),new CraftRequirement(4, -1, 0) }),   // BoulderChair
        new CraftRecipe(6117, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 4),new CraftRequirement(8, -1, 4),new CraftRequirement(85, -1, 1) }),   // BoulderChandelier
        new CraftRecipe(6118, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 8),new CraftRequirement(22, 28, 2) }),   // BoulderChest
        new CraftRecipe(6119, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 10),new CraftRequirement(22, 28, 3),new CraftRequirement(170, -1, 6) }),   // BoulderClock
        new CraftRecipe(6120, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 0),new CraftRequirement(6, -1, 0) }),   // BoulderDoor
        new CraftRecipe(6122, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(8, -1, 1),new CraftRequirement(6132, -1, 3) }),   // BoulderLamp
        new CraftRecipe(6123, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 6),new CraftRequirement(8, -1, 1) }),   // BoulderLantern
        new CraftRecipe(6124, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 15),new CraftRequirement(154, -1, 4),new CraftRequirement(149, -1, 1) }),   // BoulderPiano
        new CraftRecipe(6126, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 6),new CraftRequirement(206, -1, 1) }),   // BoulderSink
        new CraftRecipe(6127, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 5),new CraftRequirement(225, -1, 2) }),   // BoulderSofa
        new CraftRecipe(6128, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 0),new CraftRequirement(8, -1, 0) }),   // BoulderTable
        new CraftRecipe(6130, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 0),new CraftRequirement(10, -1, 0) }),   // BoulderWorkbench
        new CraftRecipe(6129, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(6132, -1, 0),new CraftRequirement(6, -1, 0) }),   // BoulderToilet
        new CraftRecipe(9, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(94, -1, 2) }),   // Wood
        new CraftRecipe(620, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(632, -1, 2) }),   // RichMahogany
        new CraftRecipe(619, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(631, -1, 2) }),   // Ebonwood
        new CraftRecipe(911, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(913, -1, 2) }),   // Shadewood
        new CraftRecipe(621, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(633, -1, 2) }),   // Pearlwood
        new CraftRecipe(170, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(1702, -1, 2) }),   // Glass
        new CraftRecipe(1725, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(1796, -1, 2) }),   // Pumpkin
        new CraftRecipe(1729, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(1818, -1, 2) }),   // SpookyWood
        new CraftRecipe(154, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(634, -1, 2) }),   // Bone
        new CraftRecipe(192, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(1457, -1, 2) }),   // ObsidianBrick
        new CraftRecipe(3087, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3146, -1, 2) }),   // GraniteBlock
        new CraftRecipe(4139, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3945, -1, 2) }),   // SpiderBlock
        new CraftRecipe(3955, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(3957, -1, 2) }),   // LesionBlock
        new CraftRecipe(134, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(1384, -1, 2) }),   // BlueBrick
        new CraftRecipe(137, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(1386, -1, 2) }),   // GreenBrick
        new CraftRecipe(139, 1, -1, CraftEnvironment.None, new[] { new CraftRequirement(1385, -1, 2) }),   // PinkBrick
        new CraftRecipe(9, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(1389, -1, 2) }),   // Wood
        new CraftRecipe(145, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(1388, -1, 2) }),   // CopperBrick
        new CraftRecipe(9, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(1418, -1, 2) }),   // Wood
        new CraftRecipe(717, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(1387, -1, 2) }),   // TinBrick
        new CraftRecipe(2693, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2169, -1, 4) }),   // WaterfallBlock
        new CraftRecipe(2694, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(2170, -1, 4) }),   // LavafallBlock
        new CraftRecipe(3754, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3752, -1, 4) }),   // SandFallBlock
        new CraftRecipe(3755, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3753, -1, 4) }),   // SnowFallBlock
        new CraftRecipe(3272, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3340, -1, 4) }),   // HardenedSand
        new CraftRecipe(3274, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3341, -1, 4) }),   // CorruptHardenedSand
        new CraftRecipe(3275, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3342, -1, 4) }),   // CrimsonHardenedSand
        new CraftRecipe(3338, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3343, -1, 4) }),   // HallowHardenedSand
        new CraftRecipe(3276, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3344, -1, 4) }),   // CorruptSandstone
        new CraftRecipe(3277, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3345, -1, 4) }),   // CrimsonSandstone
        new CraftRecipe(3339, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3346, -1, 4) }),   // HallowSandstone
        new CraftRecipe(3347, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3348, -1, 4) }),   // DesertFossil
        new CraftRecipe(662, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(663, -1, 4) }),   // RainbowBrick
        new CraftRecipe(751, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(752, -1, 4) }),   // Cloud
        new CraftRecipe(170, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(392, -1, 4) }),   // Glass
        new CraftRecipe(134, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(135, -1, 4) }),   // BlueBrick
        new CraftRecipe(134, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1379, -1, 4) }),   // BlueBrick
        new CraftRecipe(134, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1378, -1, 4) }),   // BlueBrick
        new CraftRecipe(137, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(138, -1, 4) }),   // GreenBrick
        new CraftRecipe(137, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1383, -1, 4) }),   // GreenBrick
        new CraftRecipe(137, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1382, -1, 4) }),   // GreenBrick
        new CraftRecipe(139, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(140, -1, 4) }),   // PinkBrick
        new CraftRecipe(139, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1381, -1, 4) }),   // PinkBrick
        new CraftRecipe(139, 1, 283, CraftEnvironment.None, new[] { new CraftRequirement(1380, -1, 4) }),   // PinkBrick
        new CraftRecipe(1101, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1102, -1, 4) }),   // LihzahrdBrick
        new CraftRecipe(129, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(130, -1, 4) }),   // GrayBrick
        new CraftRecipe(131, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(132, -1, 4) }),   // RedBrick
        new CraftRecipe(145, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(146, -1, 4) }),   // CopperBrick
        new CraftRecipe(3951, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3952, -1, 4) }),   // IronBrick
        new CraftRecipe(3953, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3954, -1, 4) }),   // LeadBrick
        new CraftRecipe(143, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(144, -1, 4) }),   // SilverBrick
        new CraftRecipe(141, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(142, -1, 4) }),   // GoldBrick
        new CraftRecipe(717, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(720, -1, 4) }),   // TinBrick
        new CraftRecipe(718, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(721, -1, 4) }),   // TungstenBrick
        new CraftRecipe(719, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(722, -1, 4) }),   // PlatinumBrick
        new CraftRecipe(214, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3067, -1, 4) }),   // HellstoneBrick
        new CraftRecipe(192, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(330, -1, 4) }),   // ObsidianBrick
        new CraftRecipe(577, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(606, -1, 4) }),   // DemoniteBrick
        new CraftRecipe(594, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(595, -1, 4) }),   // SnowBrick
        new CraftRecipe(883, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(884, -1, 4) }),   // IceBrick
        new CraftRecipe(586, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(587, -1, 4) }),   // CandyCaneBlock
        new CraftRecipe(591, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(592, -1, 4) }),   // GreenCandyCaneBlock
        new CraftRecipe(607, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(608, -1, 4) }),   // SandstoneBrick
        new CraftRecipe(3271, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3273, -1, 4) }),   // Sandstone
        new CraftRecipe(412, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(417, -1, 4) }),   // PearlstoneBrick
        new CraftRecipe(609, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(610, -1, 4) }),   // EbonstoneBrick
        new CraftRecipe(413, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(418, -1, 4) }),   // IridescentBrick
        new CraftRecipe(414, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(419, -1, 4) }),   // MudstoneBlock
        new CraftRecipe(611, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(615, -1, 4) }),   // RedStucco
        new CraftRecipe(612, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(616, -1, 4) }),   // YellowStucco
        new CraftRecipe(613, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(617, -1, 4) }),   // GreenStucco
        new CraftRecipe(614, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(618, -1, 4) }),   // GrayStucco
        new CraftRecipe(3100, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3101, -1, 4) }),   // MeteoriteBrick
        new CraftRecipe(415, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(420, -1, 4) }),   // CobaltBrick
        new CraftRecipe(416, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(421, -1, 4) }),   // MythrilBrick
        new CraftRecipe(604, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(605, -1, 4) }),   // AdamantiteBeam
        new CraftRecipe(1589, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1590, -1, 4) }),   // PalladiumColumn
        new CraftRecipe(1591, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1592, -1, 4) }),   // BubblegumBlock
        new CraftRecipe(1593, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1594, -1, 4) }),   // TitanstoneBlock
        new CraftRecipe(3461, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3472, -1, 4) }),   // LunarBrick
        new CraftRecipe(3234, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3238, -1, 4) }),   // CrystalBlock
        new CraftRecipe(2, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(30, -1, 4) }),   // DirtBlock
        new CraftRecipe(3, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(26, -1, 4) }),   // StoneBlock
        new CraftRecipe(9, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(1723, -1, 4) }),   // Wood
        new CraftRecipe(9, 1, 304, CraftEnvironment.None, new[] { new CraftRequirement(3584, -1, 4) }),   // Wood
        new CraftRecipe(9, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(93, -1, 4) }),   // Wood
        new CraftRecipe(620, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(623, -1, 4) }),   // RichMahogany
        new CraftRecipe(619, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(622, -1, 4) }),   // Ebonwood
        new CraftRecipe(911, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(927, -1, 4) }),   // Shadewood
        new CraftRecipe(621, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(624, -1, 4) }),   // Pearlwood
        new CraftRecipe(183, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(764, -1, 4) }),   // GlowingMushroom
        new CraftRecipe(1725, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1726, -1, 4) }),   // Pumpkin
        new CraftRecipe(1727, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1728, -1, 4) }),   // Hay
        new CraftRecipe(1729, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1730, -1, 4) }),   // SpookyWood
        new CraftRecipe(1344, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3751, -1, 4) }),   // Cog
        new CraftRecipe(3066, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3083, -1, 4) }),   // MarbleBlock
        new CraftRecipe(3081, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3082, -1, 4) }),   // Marble
        new CraftRecipe(3087, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3089, -1, 4) }),   // GraniteBlock
        new CraftRecipe(3086, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3088, -1, 4) }),   // Granite
        new CraftRecipe(9, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1447, -1, 4) }),   // Wood
        new CraftRecipe(704, 1, 16, CraftEnvironment.None, new[] { new CraftRequirement(1448, -1, 4) }),   // LeadBar
        new CraftRecipe(824, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(825, -1, 4) }),   // SunplateBlock
        new CraftRecipe(276, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(750, -1, 4) }),   // Cactus
        new CraftRecipe(763, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(770, -1, 4) }),   // FleshBlock
        new CraftRecipe(762, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(769, -1, 4) }),   // SlimeBlock
        new CraftRecipe(1124, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(1126, -1, 4) }),   // Hive
        new CraftRecipe(154, 1, 300, CraftEnvironment.None, new[] { new CraftRequirement(768, -1, 4) }),   // Bone
        new CraftRecipe(3955, 1, 18, CraftEnvironment.None, new[] { new CraftRequirement(3956, -1, 4) }),   // LesionBlock
    };
}
