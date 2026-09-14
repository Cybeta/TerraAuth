// TerraAuth — 服务端权威伤害结算：武器 → 弹药类型表（阶段 F 远程校验用）
// 数据源：Terraria 1.4.5.8 原版 Item.SetDefaults1-5 的 useAmmo 字段（3-tab 基础赋值；
//          变体 switch 经 SetDefaults1(base) 继承，如金属弓继承 IronBow 的 Arrow）。
// 值 = 弹药「类型」物品 ID（AmmoID：Arrow=40 / Bullet=97 / Rocket=771 / Dart=283 / Gel=23 /
//          FallenStar=75 / Flare=931 / Snowball=949 / StyngerBolt=1261 / CandyCorn=1783 /
//          JackOLantern=1785 / Stake=1836 / NailFriendly=3108 / Acorn=27 / Sand=169 / Ale=353）。
// 未收录 = 无弹药合并（投掷武器 / 鱼叉 / 食人鱼枪 / Toxikarp / 喷漆枪 / 木桶发射器，
//          以及 ItemID.Sets.gunProj 四件 3475/3540/3854/3930——原版 ItemCheck_Shoot 对枪械
//          弹种会重置 Damage = 武器伤害，弹药伤害不合并）→ 弹幕伤害 = 武器伤害，可直接校验。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>
/// 远程武器 → 弹药类型（原版 <c>Item.useAmmo</c>）。仅收录 ItemDamageTable 中的远程武器；
/// 未收录项按「无弹药合并」处理（<see cref="CombatResolver.RangedDamageBound"/> 取武器伤害为界）。
/// </summary>
public static class WeaponAmmoTypeOf
{
    public static readonly IReadOnlyDictionary<int, int> Of = new Dictionary<int, int>
    {
        // ---- 弓 / 连弩（弹药：箭 40）----
        [39]   = 40,    // WoodenBow
        [99]   = 40,    // IronBow
        [3504] = 40,    // 铜弓 Copper Bow
        [3510] = 40,    // 银弓 Silver Bow
        [3516] = 40,    // 金弓 Gold Bow
        [3480] = 40,    // 铂金弓 Platinum Bow
        [3486] = 40,    // 钨弓 Tungsten Bow
        [3492] = 40,    // 铅弓 Lead Bow
        [3498] = 40,    // 锡弓 Tin Bow
        [44]   = 40,    // DemonBow
        [120]  = 40,    // MoltenFury
        [655]  = 40,    // EbonwoodBow
        [658]  = 40,    // RichMahoganyBow
        [661]  = 40,    // PearlwoodBow
        [682]  = 40,    // Marrow
        [725]  = 40,    // IceBow
        [796]  = 40,    // TendonBow
        [923]  = 40,    // ShadewoodBow
        [435]  = 40,    // CobaltRepeater
        [436]  = 40,    // MythrilRepeater
        [481]  = 40,    // AdamantiteRepeater
        [578]  = 40,    // HallowedRepeater
        [1187] = 40,    // PalladiumRepeater
        [1194] = 40,    // OrichalcumRepeater
        [1201] = 40,    // TitaniumRepeater
        [1229] = 40,    // ChlorophyteShotbow
        [3019] = 40,    // HellwingBow
        [3029] = 40,    // DaedalusStormbow
        [3052] = 40,    // ShadowFlameBow
        [3859] = 40,    // DD2BetsyBow
        [4953] = 40,    // FairyQueenRangedItem
        [5282] = 40,    // AshWoodBow
        // ---- 枪（弹药：子弹 97）----
        [95]   = 97,    // FlintlockPistol
        [96]   = 97,    // Musket
        [98]   = 97,    // Minishark
        [164]  = 97,    // Handgun
        [219]  = 97,    // PhoenixBlaster
        [434]  = 97,    // ClockworkAssaultRifle
        [533]  = 97,    // Megashark
        [534]  = 97,    // Shotgun
        [679]  = 97,    // TacticalShotgun
        [800]  = 97,    // TheUndertaker
        [964]  = 97,    // Boomstick
        [1254] = 97,    // SniperRifle
        [1255] = 97,    // VenusMagnum
        [1265] = 97,    // Uzi
        [1553] = 97,    // SDMG
        [1870] = 97,    // RedRyder
        [1929] = 97,    // ChainGun
        [3788] = 97,    // OnyxBlaster
        [4703] = 97,    // Quad-Barrel Shotgun
        [5117] = 97,    // PewMaticHorn
        // ---- 火箭发射器（弹药：火箭 771）----
        [758]  = 771,   // GrenadeLauncher
        [759]  = 771,   // RocketLauncher
        [760]  = 771,   // ProximityMineLauncher
        [1946] = 771,   // SnowmanCannon
        [3546] = 771,   // FireworksLauncher
        // ---- 吹管 / 吹箭筒 / 飞镖枪（弹药：飞镖 283）----
        [281]  = 283,   // Blowpipe
        [986]  = 283,   // Blowgun
        [3007] = 283,   // DartPistol
        [3008] = 283,   // DartRifle
        // ---- 特殊弹药 ----
        [197]  = 75,    // StarCannon（陨星 FallenStar，基础伤害 0 → 界=武器伤害）
        [266]  = 169,   // Sandgun（沙块，基础伤害 0）
        [506]  = 23,    // Flamethrower（凝胶，基础伤害 0）
        [1910] = 23,    // ElfMelter（凝胶，基础伤害 0）
        [930]  = 931,   // FlareGun（照明弹 1）
        [1319] = 949,   // SnowballCannon（雪球 8）
        [1258] = 1261,  // Stynger（毒刺矢 17）
        [1782] = 1783,  // CandyCornRifle（糖果玉米 9）
        [1784] = 1785,  // JackOLanternLauncher（爆炸南瓜 60）
        [1835] = 1836,  // StakeLauncher（木桩 25）
        [3107] = 3108,  // NailGun（铁钉 30）
        [3821] = 353,   // AleThrowingGlove（麦酒，基础伤害 0）
        [5629] = 27,    // AcornSlingshot（橡实，基础伤害 0）
    };
}
