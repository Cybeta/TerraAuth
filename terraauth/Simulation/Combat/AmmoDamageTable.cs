// TerraAuth — 服务端权威伤害结算：弹药攻击力表（数据表补齐）
// 数据源：Terraria 1.4.5.8 原版 Item.SetDefaults1-5 的 ammo + damage 字段（ID 以 ItemID.cs 为准）。
// 用途：远程武器命中 = 武器伤害 + 弹药伤害（原版 PickAmmo 合并），本表为后续远程武器上界校验
//       （当前远程武器因弹药合并复杂性失败放行）提供权威弹药数据。
// 收录范围：ammo = true 且 damage > 0 的弹药（箭/子弹/火箭/镖/钉/雪球/种子/照明弹等）。
// 未收录：damage = 0 的弹药（凝胶/沙块/溶液/陨星/橡实）与 notAmmo = true 的抛掷物（硬币/炸弹类）
//       —— 不参与伤害合并，收录无意义；未知弹药 → 无加成，绝不误拒。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>弹药 ID → 攻击力（原版 <c>Item.damage</c>）。</summary>
public static class AmmoDamageTable
{
    public static readonly IReadOnlyDictionary<int, int> Of = new Dictionary<int, int>
    {
        // ---- 箭（弓 / 连弩用）----
        [40]  = 5,    // 木箭 Wooden Arrow
        [41]  = 7,    // 烈焰箭 Flaming Arrow
        [47]  = 12,   // 邪箭 Unholy Arrow
        [51]  = 10,   // 小丑之箭 Jester's Arrow
        [265] = 13,   // 狱炎箭 Hellfire Arrow
        [516] = 13,   // 圣箭 Holy Arrow
        [545] = 17,   // 诅咒箭 Cursed Arrow
        [988] = 9,    // 霜冻箭 Frostburn Arrow
        [1235] = 16,  // 叶绿箭 Chlorophyte Arrow
        [1334] = 16,  // 灵液箭 Ichor Arrow
        [1341] = 19,  // 毒液箭 Venom Arrow
        [3003] = 8,   // 骨箭 Bone Arrow
        [3103] = 5,   // 无尽箭袋 Endless Quiver
        [3568] = 15,  // 月亮领主箭 Moon Lord Arrow
        [5348] = 12,  // 微光箭 Shimmer Arrow
        // ---- 子弹（枪 / 发射器用）----
        [97]  = 7,    // 火枪子弹 Musket Ball
        [234] = 8,    // 陨星弹 Meteor Shot
        [278] = 9,    // 银子弹 Silver Bullet
        [515] = 9,    // 水晶子弹 Crystal Bullet
        [546] = 15,   // 诅咒弹 Cursed Bullet
        [1179] = 9,   // 叶绿弹 Chlorophyte Bullet
        [1302] = 11,  // 高速子弹 High Velocity Bullet
        [1335] = 13,  // 灵液弹 Ichor Bullet
        [1342] = 15,  // 毒液弹 Venom Bullet
        [1349] = 10,  // 派对弹 Party Bullet
        [1350] = 10,  // 纳米弹 Nano Bullet
        [1351] = 10,  // 爆破弹 Exploding Bullet
        [1352] = 10,  // 金子弹 Golden Bullet
        [3104] = 7,   // 无尽火枪袋 Endless Musket Pouch
        [3567] = 20,  // 月亮领主弹 Moon Lord Bullet
        [4915] = 9,   // 钨子弹 Tungsten Bullet
        // ---- 火箭（火箭发射器 / 庆祝系列用）----
        [771] = 40,   // 火箭 I Rocket I
        [772] = 40,   // 火箭 II Rocket II
        [773] = 65,   // 火箭 III Rocket III
        [774] = 65,   // 火箭 IV Rocket IV
        [1785] = 60,  // 南瓜炸弹 Explosive Jack O'Lantern
        [4445] = 50,  // 集束火箭 I Cluster Rocket I
        [4446] = 50,  // 集束火箭 II Cluster Rocket II
        [4447] = 40,  // 湿火箭 Wet Rocket
        [4448] = 40,  // 熔岩火箭 Lava Rocket
        [4449] = 40,  // 蜂蜜火箭 Honey Rocket
        [4457] = 75,  // 迷你核弹 I Mini Nuke I
        [4458] = 75,  // 迷你核弹 II Mini Nuke II
        [4459] = 40,  // 干火箭 Dry Rocket
        // ---- 镖 / 钉 / 其他----
        [1310] = 10,  // 毒镖 Poison Dart
        [3009] = 14,  // 水晶镖 Crystal Dart
        [3010] = 9,   // 诅咒镖 Cursed Dart
        [3011] = 10,  // 灵液镖 Ichor Dart
        [1261] = 17,  // 毒刺矢 Stynger Bolt（毒刺发射器用）
        [3108] = 30,  // 铁钉 Nail（射钉枪用）
        [949] = 8,    // 雪球 Snowball（雪球炮用）
        [283] = 4,    // 种子 Seed（吹箭筒 / 吹管用）
        [1783] = 9,   // 糖果玉米 Candy Corn（糖果玉米步枪用）
        [1836] = 25,  // 木桩 Stake（木桩发射器用）
        // ---- 照明弹（信号枪用）----
        [931]  = 1,   // 照明弹 Flare
        [1614] = 1,   // 蓝照明弹 Blue Flare
        [5377] = 1,   // 勘探照明弹 Spelunker Flare
        [5378] = 1,   // 诅咒照明弹 Cursed Flare
        [5379] = 1,   // 彩虹照明弹 Rainbow Flare
        [5380] = 1,   // 微光照明弹 Shimmer Flare
    };
}
