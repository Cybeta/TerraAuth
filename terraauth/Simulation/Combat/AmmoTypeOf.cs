// TerraAuth — 服务端权威伤害结算：弹药物品 → 弹药类型表（阶段 F 远程校验用）
// 数据源：Terraria 1.4.5.8 原版 Item.SetDefaults1-5 的 ammo 字段（3-tab 基础赋值）。
// 值 = 弹药「类型」物品 ID（与 WeaponAmmoTypeOf 的武器 useAmmo 对拍）。
// 用途：CombatResolver.RangedDamageBound 扫描玩家物品栏，取与武器同类型的最高弹药基础伤害
//       （AmmoDamageTable 收录全部 dmg>0 弹药；未收录的弹药原版基础伤害为 0，按 0 计不误拒）。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>弹药物品 ID → 弹药类型（原版 <c>Item.ammo</c>）。未收录项视为非弹药。</summary>
public static class AmmoTypeOf
{
    public static readonly IReadOnlyDictionary<int, int> Of = new Dictionary<int, int>
    {
        // ---- 箭（40）----
        [40]   = 40,    // WoodenArrow
        [41]   = 40,    // FlamingArrow
        [47]   = 40,    // UnholyArrow
        [51]   = 40,    // JestersArrow
        [265]  = 40,    // HellfireArrow
        [516]  = 40,    // HolyArrow
        [545]  = 40,    // CursedArrow
        [988]  = 40,    // FrostburnArrow
        [1235] = 40,    // ChlorophyteArrow
        [1334] = 40,    // IchorArrow
        [1341] = 40,    // VenomArrow
        [3003] = 40,    // BoneArrow
        [3103] = 40,    // EndlessQuiver
        [3568] = 40,    // MoonLordArrow
        [5348] = 40,    // ShimmerArrow
        // ---- 子弹（97）----
        [97]   = 97,    // MusketBall
        [234]  = 97,    // MeteorShot
        [278]  = 97,    // SilverBullet
        [515]  = 97,    // CrystalBullet
        [546]  = 97,    // CursedBullet
        [1179] = 97,    // ChlorophyteBullet
        [1302] = 97,    // HighVelocityBullet
        [1335] = 97,    // IchorBullet
        [1342] = 97,    // VenomBullet
        [1349] = 97,    // PartyBullet
        [1350] = 97,    // NanoBullet
        [1351] = 97,    // ExplodingBullet
        [1352] = 97,    // GoldenBullet
        [3104] = 97,    // EndlessMusketPouch
        [3567] = 97,    // MoonLordBullet
        [4915] = 97,    // TungstenBullet
        // ---- 火箭（771）----
        [771]  = 771,   // RocketI
        [772]  = 771,   // RocketII
        [773]  = 771,   // RocketIII
        [774]  = 771,   // RocketIV
        [4445] = 771,   // ClusterRocketI
        [4446] = 771,   // ClusterRocketII
        [4447] = 771,   // WetRocket
        [4448] = 771,   // LavaRocket
        [4449] = 771,   // HoneyRocket
        [4457] = 771,   // MiniNukeI
        [4458] = 771,   // MiniNukeII
        [4459] = 771,   // DryRocket
        // ---- 飞镖（283）----
        [283]  = 283,   // Seed
        [1310] = 283,   // PoisonDart
        [3009] = 283,   // CrystalDart
        [3010] = 283,   // CursedDart
        [3011] = 283,   // IchorDart
        // ---- 特殊 ----
        [23]   = 23,    // Gel
        [27]   = 27,    // Acorn
        [71]   = 71,    // CopperCoin
        [72]   = 71,    // SilverCoin
        [73]   = 71,    // GoldCoin
        [74]   = 71,    // PlatinumCoin
        [75]   = 75,    // FallenStar
        [169]  = 169,   // Sand
        [370]  = 169,   // Ebonsand
        [408]  = 169,   // Crimsand
        [1246] = 169,   // Pearlsand
        [353]  = 353,   // Ale
        [780]  = 780,   // Solution
        [781]  = 780,   // (Solution 变体)
        [782]  = 780,   // (Solution 变体)
        [783]  = 780,   // (Solution 变体)
        [784]  = 780,   // (Solution 变体)
        [931]  = 931,   // Flare
        [1614] = 931,   // BlueFlare
        [5377] = 931,   // SpelunkerFlare
        [5378] = 931,   // CursedFlare
        [5379] = 931,   // RainbowFlare
        [5380] = 931,   // ShimmerFlare
        [949]  = 949,   // Snowball
        [1261] = 1261,  // StyngerBolt
        [1783] = 1783,  // CandyCorn
        [1785] = 1785,  // ExplosiveJackOLantern
        [1836] = 1836,  // Stake
        [3108] = 3108,  // Nail
    };
}
