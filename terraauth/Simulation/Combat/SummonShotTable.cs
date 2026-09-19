// TerraAuth — 召唤本体「派生弹幕」表（数据表；阶段 1：只落数据，不改行为）
//
// 作用：把「本体」（`Projectile.SetDefaults` 里 `minion = true` / `sentry = true` 的弹幕，
// 见 Simulation/Combat/SummonEntityTable.cs 的 62 条）与「本体 AI 自己发射的派生弹幕」分开。
// 这是 backlog W-2 里 `ServerShots`（服务端生成本体派生弹幕）的前置数据。
//
// 来源与口径（Terraria 1.4.5.8 反编译源码，逐条人工读 AI；脚本无法可靠完成，原因见下）：
//   · 只有**本体所走的那条 AI 分支内、且 type 条件对本本体成立**的 `Projectile.NewProjectile`
//     才算派生。原版把几十个 type 塞进同一个巨型 AI（`AI_026` / `AI_062` / `AI_067` / `AI_133` 内联块 …），
//     发射点的 Type 往往在**函数入口按 type 赋给局部变量**（如 `int num48 = 0; if (type == 373) num48 = 374;`），
//     所以「在生成点附近找数字字面量」必然误判——必须回溯到该局部变量的赋值链。
//   · `NewProjectile` 有多个重载，Projectile Type 参数分别落在第 3 / 4 / 5 个位置
//     （Vector2 重载：position, velocity, **type**, …；float 重载：X, Y, SpeedX, SpeedY, **type**, …），
//     按位置硬编码取参也会取错。
//   · 名称以 `ProjectileID.cs` 为准（逐条核对，不按印象写）。
//
// 统计：**32 个本体有派生弹幕 / 共 34 条「本体 → 派生」关系 / 25 个不重复的派生弹幕类型**；
//       其余 **30 个本体**已完整检查其 AI 路径，确认无攻击派生弹幕（见 `NoDerivedShots`）。
//       32 + 30 = 62，与本体表总数一致——即**全部本体都已定稿，不存在"未解析"**。
//
// 典型陷阱（本轮据此纠正了多处误判）：
//   · `AI_026`（Pygmy / Spider / Foxsparks / 大量宠物共用，7600+ 行）：发射点的类型来自 `num161`，
//     默认 195（PygmySpear），仅 `flag8`（`type == 1094 || type == 1113`，Foxsparks）时改写为 1097 ——
//     所以蜘蛛 390/391/392 与宠物都**不会**发射，只有 Pygmy 191-194 会。
//   · `AI_062`（Hornet / Imp / Tempest / UFO / StardustCell / 以及 Abigail 963 共用）：
//     `num48` 只在 `type == 373 / 375 / 407 / 423 / 613` 时被赋值；963 保持 0，
//     共享块里那次 `NewProjectile(..., num48, ...)` 对 963 是「type 0」的无效调用 —— 故 963 无有效派生。
//   · `AI_067`（海盗 / 吸血鬼蛙 / Flinx / 蘑菇小子 共用）：唯一发射点被 `flag6`（`type == 1022`）包裹，
//     而老虎 833/834/835 的攻击派生走的是**另一个**专用方法 `AI_067_TigerSpecialAttack`（`→ 818`）。
//   · aiStyle 66 是**内联块**（无 `AI_066` 方法）：387 → 389 被 `if (type == 387)` 限定，388 / 533 不发射。
//   · aiStyle 130 / 134 / 138 的 DD2 哨兵：派生类型由**方法入口的默认值 + `switch (type)` 覆写**决定
//     （如 FlameBurst `int num2 = 664; case 665: num2 = 666; case 667: num2 = 668;`），
//     不得按 ID 相邻（665→665）推断。
//
// 边界（抽不到的不猜）：
//   · 「媒介 / 召唤生成」**不算**攻击派生弹幕，故不入本表，仅在 `NoDerivedShots` 里标注来源：
//     - 970 AbigailCounter（媒介）→ 963 AbigailMinion：由 `Player.UpdateAbigailStatus` 生成；
//     - 831 StormTigerGem（媒介）→ 833/834/835 StormTigerTier1/2/3：由 `Player.UpdateStormTigerStatus`
//       按 `ownedProjectileCounts[831]` 的档位（>0 / >3 / >6）生成；
//     - 3531 StardustDragonStaff（物品）→ 625/626/627/628：由玩家召唤逻辑一次性串出整条龙（含追加节段）；
//       625-628 的 AI（`AI_121`）**只做节段重连**，本身不发射任何弹幕。
//   · 本表只列「会发射什么」；**索敌射程 / 攻击间隔 / 冷却 / 触发前置条件**尚未抽取（属 backlog W-2 的下一步），
//     未定稿前不得据此实现 `ServerShots`。
//   · 本表已接入运行时：`SummonProjectileTable.Shots` 由本表算出（25 条），与 `Bodies` 并集成身份集合 `Of`，
//     从而修掉「派生弹幕不在身份集合里 → 包 28 被拒 → 伤害丢失」的缺陷。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>本体发射的一种派生弹幕。</summary>
/// <param name="ProjectileType">派生弹幕的 <c>Projectile.type</c>。</param>
/// <param name="Name">`ProjectileID` 名称（便于核对）。</param>
public sealed record SummonShotInfo(int ProjectileType, string Name);

/// <summary>
/// 召唤 / 哨兵**本体**发射的派生弹幕（键为**本体**弹幕类型）。
/// 32 个本体共 34 条关系；与 <see cref="SummonEntityTable"/> 的 62 条本体互补：
/// 32（有派生）+ 30（无派生，见 <see cref="NoDerivedShots"/>）= 62。
/// </summary>
public static class SummonShotTable
{
    /// <summary>本体弹幕类型 → 该本体 AI 可发射的派生弹幕。</summary>
    public static readonly IReadOnlyDictionary<int, IReadOnlyList<SummonShotInfo>> Of =
        new Dictionary<int, IReadOnlyList<SummonShotInfo>>
        {
            // ---- 仆从本体（17 个）----
            // AI_026：冷却 + 有可追击目标 + 本机 owner 时发射；速度 11f 归一化（Projectile.cs L82700-82763）
            [191] = new[] { new SummonShotInfo(195, "PygmySpear") },
            [192] = new[] { new SummonShotInfo(195, "PygmySpear") },
            [193] = new[] { new SummonShotInfo(195, "PygmySpear") },
            [194] = new[] { new SummonShotInfo(195, "PygmySpear") },

            // AI_062：num48 按 type 赋值（L87579-87603），共享发射块在 L87638/87656/87690
            [373] = new[] { new SummonShotInfo(374, "HornetStinger") },
            [375] = new[] { new SummonShotInfo(376, "ImpFireball") },
            [407] = new[] { new SummonShotInfo(408, "MiniSharkron") },
            [423] = new[] { new SummonShotInfo(433, "UFOLaser") },
            [613] = new[] { new SummonShotInfo(614, "StardustCellMinionShot") },

            // aiStyle 66 内联块：唯一发射点被 `if (type == 387)` 限定（L39872-39884）
            [387] = new[] { new SummonShotInfo(389, "MiniRetinaLaser") },

            // AI_067_TigerSpecialAttack：近距有目标 + 本机 + 冷却（冷却 833/基础 360、834 300、835 240）
            [833] = new[] { new SummonShotInfo(818, "WhiteTigerPounce") },
            [834] = new[] { new SummonShotInfo(818, "WhiteTigerPounce") },
            [835] = new[] { new SummonShotInfo(818, "WhiteTigerPounce") },

            // AI_067：target 命中后生成本体爆炸；仅 owner 本机（L66573-66585）
            [1022] = new[] { new SummonShotInfo(1044, "DeadCellsMushroomBoiMinionExplosion") },

            // AI_026（flag8 = Foxsparks）：普通攻击 → 1097；引导武器（AI_0>=1000）→ 1106
            [1094] = new[]
            {
                new SummonShotInfo(1097, "PalworldMinionFoxsparksFireball"),
                new SummonShotInfo(1106, "PalworldMinionFoxsparksFlames"),
            },
            [1113] = new[]
            {
                new SummonShotInfo(1097, "PalworldMinionFoxsparksFireball"),
                new SummonShotInfo(1106, "PalworldMinionFoxsparksFlames"),
            },

            // AI_206：Minion_FindTargetInRange(800) 命中且有视线时发射（L48536）
            [1119] = new[] { new SummonShotInfo(1120, "ForbiddenMinionShot") },

            // ---- 哨兵本体（15 个）----
            // AI_053_FrostHydra / SpiderHiver：num15 按 type 覆写（308→309、377→378、966→967）
            [308] = new[] { new SummonShotInfo(309, "FrostBlastFriendly") },
            [377] = new[] { new SummonShotInfo(378, "SpiderEgg") },
            [966] = new[] { new SummonShotInfo(967, "HoundiusShootiusFireball") },

            // 月球炮台 / 彩虹水晶：目标距离 + CanHitLine + 攻击计数阈值（641 = 10、643 = 5，643 每次生成 3 发）
            [641] = new[] { new SummonShotInfo(642, "MoonlordTurretLaser") },
            [643] = new[] { new SummonShotInfo(644, "RainbowCrystalExplosion") },

            // AI_130_FlameBurstTower：入口 `num2 = 664`，case 665/667 覆写为 666/668（L91001/91067/91120）
            [663] = new[] { new SummonShotInfo(664, "DD2FlameBurstTowerT1Shot") },
            [665] = new[] { new SummonShotInfo(666, "DD2FlameBurstTowerT2Shot") },
            [667] = new[] { new SummonShotInfo(668, "DD2FlameBurstTowerT3Shot") },

            // AI_134_Ballista：三档共用 `int num = 680;`（L91519，L91640 发射），仅速度/延迟不同
            [677] = new[] { new SummonShotInfo(680, "DD2BallistraProj") },
            [678] = new[] { new SummonShotInfo(680, "DD2BallistraProj") },
            [679] = new[] { new SummonShotInfo(680, "DD2BallistraProj") },

            // AI_138_ExplosiveTrap：入口 `num = 694`，case 692/693 覆写为 695/696（L92718-92729）
            [691] = new[] { new SummonShotInfo(694, "DD2ExplosiveTrapT1Explosion") },
            [692] = new[] { new SummonShotInfo(695, "DD2ExplosiveTrapT2Explosion") },
            [693] = new[] { new SummonShotInfo(696, "DD2ExplosiveTrapT3Explosion") },

            // AI_197_CeilingAndHoverTurret：50 tick + 有视线目标时发射（L52290 类型、L52426 发射）
            [1025] = new[] { new SummonShotInfo(1026, "DeadCellsBarnacleShot") },
        };

    /// <summary>
    /// 已**完整检查 AI 路径**确认无攻击派生弹幕的本体（30 条，纯接触伤害 / 媒介 / 共享块无效调用）。
    /// 与 <see cref="Of"/> 互补：Of.Count + NoDerivedShots.Count = <see cref="SummonEntityTable"/>.Of.Count = 62。
    /// 显式列出（而非"Of 里没有就是没派生"）是为了让后续改动必须显式表态，避免新类型静默漏判。
    /// </summary>
    public static readonly IReadOnlySet<int> NoDerivedShots = new HashSet<int>
    {
        // 纯接触伤害（AI 整段含全部子函数均无 NewProjectile）
        266,   // BabySlime —— AI_026 的发射点只对 Pygmy(191-194)/Foxsparks(1094/1113) 可达
        317,   // Raven —— aiStyle 54 内联块
        388,   // Spazmamini —— aiStyle 66 内联块；389 仅 387 可达（388 走拦截/冲刺）
        390,   // VenomSpider —— AI_026 同 266
        391,   // JumperSpider
        392,   // DangerousSpider
        393,   // OneEyedPirate —— AI_067；1044 仅 1022 可达
        394,   // SoulscourgePirate
        395,   // PirateCaptain
        533,   // DeadlySphere —— aiStyle 66 内联块；389 仅 387 可达
        623,   // StardustGuardian —— AI_120_StardustGuardian
        625,   // StardustDragon1 —— AI_121 只做节段重连，节段由物品 3531 生成
        626,   // StardustDragon2
        627,   // StardustDragon3
        628,   // StardustDragon4
        755,   // BatOfLight —— AI_156
        758,   // VampireFrog —— AI_067；1044 仅 1022 可达
        759,   // BabyBird —— AI_158
        864,   // Smolstar —— AI_169
        946,   // EmpressBlade —— AI_156
        951,   // FlinxMinion —— AI_067 同 758
        963,   // AbigailMinion —— AI_062 里 num48 保持 0（无效 type 0），无有效派生
        1093,  // PalworldMinionCattiva —— AI_067；走 flag8 分支，无发射点
        1112,  // PalworldMinionTrustyCattiva —— 同 1093
        1118,  // ClayPotMinion —— AI_067 走 flag9 分支，无发射点

        // 媒介 / 召唤生成：本体本身不发射，实体由玩家逻辑生成
        831,   // StormTigerGem（媒介）→ 由 Player.UpdateStormTigerStatus 生成 833/834/835
        970,   // AbigailCounter（媒介）→ 由 Player.UpdateAbigailStatus 生成 963

        // 哨兵：AI 整段无发射点
        688,   // DD2LightningAuraT1 —— AI_137 只有范围接触判定
        689,   // DD2LightningAuraT2
        690,   // DD2LightningAuraT3
    };
}
