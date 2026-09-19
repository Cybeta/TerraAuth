// TerraAuth — 召唤 / 哨兵弹幕类型表（阶段 G）
// 服务端权威地识别「召唤物 / 哨兵炮台」发射的弹幕，用于两处：
//   1) 生命周期：召唤物由召唤 AI 驱动（跟随 / 驻守），位置与朝向目前仍由客户端包 27 上报，
//      服务端不做直线积分，也不按默认 300 tick 超时销毁（否则合法召唤物命中会失去弹幕基准），
//      存活由客户端包 29 + 召唤 Buff / 召唤武器清理驱动。
//   2) 伤害校验：召唤物命中（包 28）的上界 = ceil(最高召唤弹幕伤害 × 1.15) × (crit ? 2 : 1)，
//      （原版 Projectile.Damage 的 DamageVar ±15% × 暴击倍率；弹幕伤害由客户端包 27 创建时上报，
//       已含 minionDamage 修饰，攻击时不再变化）。见 CombatResolver.SummonDamageBound。
// 数据源：Terraria 1.4.5.8 原版 Projectile.SetDefaults 中赋 `minion = true` / `sentry = true` 的弹幕类型
//         （ID 以 ProjectileID.cs 为准，与 ItemDamageTable 同源）。
// 说明：表允许「宽于实际」——误收录只抬高合法上界（不误拒），漏收录则找不到基准（失败放行），
//       两个方向都保证绝不误拒合法命中。
//
// **已知口径问题（待 backlog W-2 第二步修正，本文件暂不改行为）**：本表把「本体」与「本体发射的派生弹幕」
// 混在一起，且 `SentryTypes` 有多处误标。经原版源码逐条核对（见 Simulation/Combat/SummonEntityTable.cs）：
//   · 真本体共 **62 条**（46 条由物品 `Item.shoot` 直生 + 16 条本体变体），其中本表**漏了 19 条**：
//     Hornet 373 / FlyingImp 375 / SpiderHiver 377 / Retanimini 387 / VenomSpider 390（+变体 391/392）、
//     OneEyedPirate 393（+394/395）/ Tempest 407 / UFOMinion 423、以及 11 个 DD2 哨兵
//     （663/665/667 FlameBurst、677/678/679 Ballistra、688/689/690 LightningAura、691/692/693 ExplosiveTrap）；
//   · `SentryTypes` 误标：831 / 946 / 951 / 970 是**仆从**（StormTigerGem / EmpressBlade / FlinxMinion / AbigailCounter），
//     真正的哨兵是 308 / 377 / 641 / 643 / 663-693（DD2 四系三档）/ 966 / 1025；
//   · 192-194 / 626-628 / 833-835 / 963 / 623 / 388 是**本体变体**（仍是本体，不是派生弹幕）。
// 逐条对照表见 `Simulation/Combat/SummonEntityTable.cs` 文件头。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>
/// 原版 1.4.5.8 全部召唤物 / 哨兵弹幕类型（<c>minion = true</c> / <c>sentry = true</c>）。
/// 用于识别「该玩家拥有的召唤弹幕」并据此计算包 28 的合法伤害上界。
/// </summary>
public enum SummonKind
{
    None = 0,
    Minion = 1,
    Sentry = 2,
}

public static class SummonProjectileTable
{
    /// <summary>召唤 / 哨兵弹幕类型集合（可安全包含全部 minion/sentry 弹幕）。</summary>
    public static readonly HashSet<int> Of = new()
    {
        191, 192, 193, 194,  // 星尘细胞法杖（StardustCell）系列
        266,                 // 蜘蛛法杖（SpiderMinion）
        308,                 // 小恶魔法杖（ImpFireball）
        317,                 // 蜂巢法杖（HornetStinger）
        373, 375, 377,       // 海盗法杖（Pirate）系列
        387, 388, 389,       // 乌鸦法杖 / 矮人法杖（Raven / Pygmy）系列
        407, 533,            // 蝙蝠法杖（VampireBat）
        423,                 // 双子魔眼法杖（TwinEyeMinion）
        613,                 // 提基法杖（TikiSpirit）
        623,                 // 荆棘弹幕（ThornBall）
        625, 626, 627, 628,  // 骷髅法杖（SkeletonMinion / SkeletonBone）系列
        641, 643,            // 外星法杖（XenoMinion）系列
        667,                 // 酒馆老板哨兵（DD2）弹幕
        676, 687,            // 酒馆老板哨兵（DD2）弹幕
        755,                 // 致命球法杖（DeadlySphere）
        758, 759,            // 星尘守卫（StardustGuardian）系列
        831, 833, 834, 835,  // 酒馆老板（DD2）哨兵系列
        864,                 // 阿比盖尔之花（AbigailMinion）
        946, 951,            // 酒馆老板（DD2）哨兵
        963, 966, 970,       // 阿比盖尔 / 酒馆老板哨兵
        1022, 1025,          // 泰拉棱镜（Terraprisma）
        1093, 1094,          // 沙漠精灵法杖（DesertDjinnCurse）系列
        1112, 1113,          // 1.4.5 新增召唤弹幕
        1118, 1119,          // 1.4.5 新增召唤弹幕
    };

    /// <summary>
    /// 召唤武器（物品 ID）→ 其召唤 Buff（原版 <c>Item.SetDefaults</c> 的 <c>buffType</c> 字段，逐条核对原版实现）。
    /// 用于「移除召唤武器即销毁」（<see cref="WorldState.DestroySummonsOnWeaponRemoval"/>）时
    /// 同步移除客户端对应召唤 Buff：原版仆从由该 Buff 驱动存活，**只销毁服务端弹幕（包 29）不够**——
    /// 客户端 Buff 未移除时仆从不消失，仍持续发射弹幕并造成伤害（用户实测：必须手动点掉 Buff 提示）。
    /// 覆盖全部召唤武器（含 1.4.5+/交叉内容）。下列**哨兵炮台**在 <c>Item.SetDefaults</c> 里只设
    /// <c>sentry = true</c>、无 <c>buffType</c>，即无玩家召唤 Buff——无需清，故刻意不入表：
    /// 1572 FrostHydra / 3569 MoonlordTurret / 3571 RainbowCrystal / 3834 DD2Trap /
    /// 5119 HoundiusShootius / 5463 DeadCellsBarnacle。
    /// </summary>
    public static readonly IReadOnlyDictionary<int, int> SummonWeaponBuff = new Dictionary<int, int>
    {
        [1157] = 49,   // Pygmy Staff → Pygmies                (Item.cs:14284)
        [1309] = 64,   // Slime Staff → BabySlime              (Item.cs:16240)
        [1802] = 83,   // Raven Staff → Ravens                 (Item.cs:20046)
        [2364] = 125,  // Hornet Staff → Hornet                (Item.cs:23862)
        [2365] = 126,  // Imp Staff → FlyingImp                (Item.cs:23881)
        [2535] = 134,  // Optic Staff → Retanimini/Spazmamini  (Item.cs:24694)
        [2551] = 133,  // Spider Staff → VenomSpider           (Item.cs:24960)
        [2584] = 135,  // Pirate Staff → OneEyedPirate         (Item.cs:25393)
        [2621] = 139,  // Tempest Staff → Tempest              (Item.cs:25623)
        [2749] = 140,  // Xeno Staff → UFOMinion               (Item.cs:26320)
        [3249] = 161,  // Deadly Sphere Staff → DeadlySphere  (Item.cs:29965)
        [3474] = 182,  // Stardust Cell Staff → StardustMinion(Item.cs:31197)
        [3531] = 188,  // Stardust Dragon Staff → Dragon      (Item.cs:31766)
        [4269] = 213,  // Sanguine Staff → Sanguine Bat        (Item.cs:36769)
        [4273] = 214,  // Vampire Frog Staff → VampireFrog    (Item.cs:36840)
        [4281] = 216,  // Finch Staff → BabyBird               (Item.cs:36898)
        [4607] = 263,  // Desert Tiger Staff → StormTiger     (Item.cs:38190)
        [4758] = 271,  // Blade Staff → Smolstar               (Item.cs:39167)
        [5005] = 322,  // Empress' Blade → EmpressBlade       (Item.cs:40337)
        [5069] = 325,  // Flinx Staff → FlinxMinion            (Item.cs:40690)
        [5114] = 335,  // Abigail's Flower → AbigailMinion    (Item.cs:41025)
        [5456] = 355,  // Dead Cells Mushroom Boi             (Item.cs:43062)
        [5663] = 385,  // Palworld Cattiva                     (Item.cs:44532)
        [5664] = 386,  // Palworld Foxsparks                   (Item.cs:44551)
        [6148] = 389,  // Palworld Trusty Cattiva              (Item.cs:47405)
        [6149] = 390,  // Palworld Trusty Foxsparks            (Item.cs:47425)
        [6161] = 393,  // Clay Pot Minion                      (Item.cs:47548)
        [6164] = 394,  // Forbidden Minion                     (Item.cs:47567)
    };

    private static readonly HashSet<int> SentryTypes = new()
    {
        667, 676, 687,
        831, 833, 834, 835,
        946, 951, 966, 970,
    };

    private static readonly HashSet<int> SummonBuffSet = new(SummonWeaponBuff.Values);

    /// <summary>返回服务端用于生命周期和命中归因的召唤类别。</summary>
    public static SummonKind KindOf(int projectileType)
    {
        if (!Of.Contains(projectileType))
            return SummonKind.None;

        return SentryTypes.Contains(projectileType)
            ? SummonKind.Sentry
            : SummonKind.Minion;
    }

    /// <summary>判断 Buff 是否为召唤 Buff（供 <see cref="WorldState.KillSummonedProjectiles"/> 移除）。</summary>
    public static bool IsSummonBuff(int buffId) => SummonBuffSet.Contains(buffId);
}
