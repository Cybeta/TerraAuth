// TerraAuth — 服务端权威弹幕类型映射。
// 仅收录已核对原版 Item.shoot 的武器与弹药。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>武器或弹药物品 ID 可产生的基础弹幕类型。</summary>
public static class WeaponProjectileTypeOf
{
    /// <summary>不使用弹药的武器 ID → 固定弹幕类型。</summary>
    public static readonly IReadOnlyDictionary<int, int> Direct = new Dictionary<int, int>
    {
        [757] = 985, // TerraBlade
        [756] = 130, // TrueExcalibur
    };

    /// <summary>弹药物品 ID → PickAmmo 后的弹幕类型。</summary>
    public static readonly IReadOnlyDictionary<int, int> Ammo = new Dictionary<int, int>
    {
        [40] = 1,     // WoodenArrow
        [41] = 2,     // FlamingArrow
        [47] = 4,     // UnholyArrow
        [51] = 5,     // JestersArrow
        [265] = 41,   // HellfireArrow
        [516] = 91,   // HolyArrow
        [545] = 103,  // CursedArrow
        [988] = 172,  // FrostburnArrow
        [1235] = 225, // ChlorophyteArrow
        [1334] = 278, // IchorArrow
        [1341] = 282, // VenomArrow
        [97] = 14,    // MusketBall
        [234] = 75,   // MeteorShot
        [278] = 15,   // SilverBullet
        [515] = 27,   // CrystalBullet
        [546] = 104,  // CursedBullet
        [1179] = 207, // ChlorophyteBullet
        [1302] = 242, // HighVelocityBullet
        [1335] = 279, // IchorBullet
        [1342] = 283, // VenomBullet
        [1349] = 284, // PartyBullet
        [1350] = 285, // NanoBullet
        [1351] = 286, // ExplodingBullet
        [1352] = 287, // GoldenBullet
        [283] = 51,   // Seed
        [1310] = 267, // PoisonDart
    };
}
