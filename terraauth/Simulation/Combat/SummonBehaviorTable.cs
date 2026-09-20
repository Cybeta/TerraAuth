// TerraAuth — 召唤本体「行为参数」表（数据表；阶段 1：只落数据，不改行为）
//
// 作用：给每个**会发射派生弹幕**的本体记录两项参数 —— **索敌射程**与**攻击间隔**。
// 这是 backlog W-2 里 `ServerAi`（服务端索敌与节奏）与 `ServerShots`（服务端生成派生弹幕）的前置数据。
// 覆盖范围与 `SummonShotTable.Of` 的 32 个本体**逐一对应**（不发射的本体不需要"攻击间隔"，
// 它们的接触/突进节奏见 `NoDerivedShots` 注释与 backlog）。
//
// 来源：Terraria 1.4.5.8 原版 `Projectile` 行为逐条人工核对（生成脚本无法完成，
// 因为这些值往往由**方法入口的局部变量 + type 分支/switch 覆写**共同决定，且同一变量常被复用）。
//
// 三条必须知道的口径差异（**横向比较前务必先看这里**）：
//   1. **射程的度量不统一**，原版各 AI 各写一套，服务端不得混用：
//      · `Euclidean`  —— `Vector2.Distance` 欧氏距离（`AI_123` 炮台 / `AI_130` 火焰塔 / `AI_134` 弩车）；
//      · `Manhattan`  —— `|dx| + |dy|` 曼哈顿距离（`AI_053` 系哨兵：308 / 377 / 966 / 1025）；
//      · `OwnerRect`  —— 以 **owner 为中心**的矩形相交（`AI_067_TigerSpecialAttack` 的 1600×800）；
//      · 少量本体用 `Collision.CanHit`、少量用 `CanHitLine`，表中以注释标注，不进结构化字段。
//   2. **间隔大多是"阈值 + 随机累加"而非固定帧数**：原版把 `ai[1]` 每帧加一个随机量，
//      超过阈值后归零。例如 Hornet 373 每帧 `+= Main.rand.Next(1, 4)`、阈值 90 → 均值 ≈45、
//      实际区间 30~89。本表记录**均值**并注明；服务端若要严格贴原版应复刻同样的随机累加，而不是用定值。
//   3. **部分间隔取决于玩家装备**（哨兵的护甲套装）：本表记录**无套装时的默认值**，注释给出各档。
//
// 统计：**32 条**（与 `SummonShotTable.Of` 的键集合完全相同）。
//
// 边界（抽不到的不猜）：
//   · 移动 / 回位 / 贴地或飞行 / 越界传送等**尚未抽取**（属 W-2 下一步），本表不含。
//   · 首射前摇单独记在 `FirstShotDelayTicks`：哨兵由 `ai[0] = 120` 初始化，
//     `AI_053` 系（308/377/966）因此首射比后续晚 120 tick；其余为 0。
//   · 本表是**只读数据**，没有任何消费者；射程 / 间隔要等 `ServerAi` / `ServerShots` 才有用武之地
//     （`SummonProjectileTable` 的身份集合已另行按 `SummonEntityTable` / `SummonShotTable` 重写）。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>索敌射程的度量口径（原版各 AI 不统一，服务端不得混用）。</summary>
public enum SummonRangeMetric
{
    /// <summary>`Vector2.Distance` 欧氏距离。</summary>
    Euclidean = 0,

    /// <summary>`|dx| + |dy|` 曼哈顿距离（`AI_053_HandleSentryNPCTargeting`）。</summary>
    Manhattan = 1,

    /// <summary>以 owner 为中心的矩形相交（`TargetingRange` 记宽度，注释给高）。</summary>
    OwnerRect = 2,
}

/// <summary>本体的索敌 / 攻击节奏参数（原版 1.4.5.8 基准值）。</summary>
/// <param name="TargetingRange">
/// 索敌射程（px）；`OwnerRect` 时记矩形宽度。部分本体的射程随仆从位/装备变化（见注释）。
/// </param>
/// <param name="RangeMetric">射程的度量口径。</param>
/// <param name="AttackIntervalTicks">
/// 两次攻击的间隔（ticks）。阈值 + 随机累加的本体记**均值**（注释给随机累加规则与阈值）；
/// 依赖玩家护甲套装的哨兵记**无套装默认值**（注释给各档）。
/// </param>
/// <param name="FirstShotDelayTicks">生成后的首射前摇（`AI_053` 系为 120，其余 0）。</param>
public sealed record SummonBehaviorInfo(
    float TargetingRange,
    SummonRangeMetric RangeMetric,
    int AttackIntervalTicks,
    int FirstShotDelayTicks = 0);

/// <summary>
/// 会发射派生弹幕的 32 个本体 → 索敌射程 + 攻击间隔（键与 <see cref="SummonShotTable.Of"/> 相同）。
/// </summary>
public static class SummonBehaviorTable
{
    /// <summary>本体弹幕类型 → 行为参数。</summary>
    public static readonly IReadOnlyDictionary<int, SummonBehaviorInfo> Of =
        new Dictionary<int, SummonBehaviorInfo>
        {
            // ==== 仆从（17）====
            // AI_026：射程 = 800 + num134，num134 = 40 × minionPos（minionPos = player.numMinions，运行时值）；
            // 冷却 num135 = 30；视线用 Collision.CanHit（不是 CanHitLine）
            [191] = new(800f, SummonRangeMetric.Euclidean, 30),
            [192] = new(800f, SummonRangeMetric.Euclidean, 30),
            [193] = new(800f, SummonRangeMetric.Euclidean, 30),
            [194] = new(800f, SummonRangeMetric.Euclidean, 30),

            // AI_062：索敌半径 num12 被无条件覆写为 2000（入口处的 400/300 是死赋值）；
            // 冷却为阈值 + 随机累加：honey(373)/imp(375) 阈值 90（373 每帧 +1~3 → ≈45，蜜蜂 buff 时阈值 70 → ≈35）；
            // 375 每帧 +1、1/3 概率再 +1 → ≈68；407 阈值 50、每帧 +1、2/3 概率再 +1 → ≈30；
            // 423 阈值 45（同上累加）→ ≈27 且**开火另需目标 ≤ 400**；613 阈值 60 → ≈36 且**开火另需目标 ≤ 500**
            [373] = new(2000f, SummonRangeMetric.Euclidean, 45),
            [375] = new(2000f, SummonRangeMetric.Euclidean, 68),
            [407] = new(2000f, SummonRangeMetric.Euclidean, 30),
            [423] = new(2000f, SummonRangeMetric.Euclidean, 27),
            [613] = new(2000f, SummonRangeMetric.Euclidean, 36),

            // aiStyle 66 内联块：索敌半径 num723 = 2000，视线用 Collision.CanHitLine；
            // 冷却为阈值 90、每帧随机 +1~3（无独立开火距离上限，锁敌且视线通即可射）
            [387] = new(2000f, SummonRangeMetric.Euclidean, 45),

            // AI_067_TigerSpecialAttack：以 owner 为中心的 1600×800 矩形；冷却 localAI[0] 按档位
            // （833 = 360、834 = 300、835 = 240），空挥（矩形内无目标）只等 10 tick 重试
            [833] = new(1600f, SummonRangeMetric.OwnerRect, 360),
            [834] = new(1600f, SummonRangeMetric.OwnerRect, 300),
            [835] = new(1600f, SummonRangeMetric.OwnerRect, 240),

            // AI_067（flag6 = type == 1022）：索敌 800（相对本体或 owner 取近者，无视线要求）；
            // 爆炸需目标进入 60px；节奏由 ai[2] 驱动 —— 蓄力 15 tick 起跳、ai[2] = 30 为空中窗口、
            // 命中后 ai[2] = -120 并每帧 +1 恢复 → 两次爆炸最小间隔 ≈ 120 + 15
            [1022] = new(800f, SummonRangeMetric.Euclidean, 135),

            // AI_026（flag8）：射程同为 800 + 40 × minionPos；1113 额外 +360 → 1160 + 40 × minionPos；
            // 冷却 num135 = 42（1094）/ 30（1113）；1113 每周期三连（num159 = 3）
            [1094] = new(800f, SummonRangeMetric.Euclidean, 42),
            [1113] = new(1160f, SummonRangeMetric.Euclidean, 30),

            // AI_206：Minion_FindTargetInRange(800) 且需 CanHitLine；ai[0] 装填 150、降到 120 时开火
            // （即 30 tick 前摇），发射后重置为 rand(105,115) - 30 ∈ [75,84] → 周期 ≈ 105~114
            [1119] = new(800f, SummonRangeMetric.Euclidean, 110),

            // ==== 哨兵（15）====
            // AI_053 系：射程走 AI_053_HandleSentryNPCTargeting 的默认 maxDistance = 1000（**曼哈顿**距离），
            // 视线为 Collision.CanHit；冷却 ai[0] = 60（308/377）/ 90（966），首射前摇 120
            [308] = new(1000f, SummonRangeMetric.Manhattan, 60, FirstShotDelayTicks: 120),
            [377] = new(1000f, SummonRangeMetric.Manhattan, 60, FirstShotDelayTicks: 120),
            [966] = new(1000f, SummonRangeMetric.Manhattan, 90, FirstShotDelayTicks: 120),

            // AI_123_FloatingTurrets：射程 num = 1000（**欧氏**）；节奏 = 攻击计数阈值 + 20 tick 开火后冷却，
            // 641 阈值 10（→ ≈30）单发，643 阈值 5（→ ≈25）每周期三连（±45° 散布）
            [641] = new(1000f, SummonRangeMetric.Euclidean, 30),
            [643] = new(1000f, SummonRangeMetric.Euclidean, 25),

            // AI_130_FlameBurstTower：射程 num = 900（**欧氏**；学徒 T2 加成时 ×1.5 = 1350）；
            // 节奏 = 待机冷却 num8 + 爆发动画 num6 × num7，三档分别为 80+6×4、70+8×4、60+8×4
            [663] = new(900f, SummonRangeMetric.Euclidean, 104),
            [665] = new(900f, SummonRangeMetric.Euclidean, 102),
            [667] = new(900f, SummonRangeMetric.Euclidean, 92),

            // AI_134_Ballista：射程 shot_range = 900（**欧氏**）；节奏 = ballistraShotDelay + 25 tick 开火动画。
            // 三档（677/678/679）共用同一套数值；冷却由玩家装备决定：无套装 160（侍从 T3 → 100、弩车恐慌 → 60、
            // 两者兼具 → 30）。T2 只把弹速 16 → 21，不改进隔
            [677] = new(900f, SummonRangeMetric.Euclidean, 185),
            [678] = new(900f, SummonRangeMetric.Euclidean, 185),
            [679] = new(900f, SummonRangeMetric.Euclidean, 185),

            // AI_138_ExplosiveTrap：射程是**矩形相交**而非距离 —— 144×144、中心在 base.Center + (0, -48)
            // （哨兵背包挂载时改为 102×102 并向外 Inflate 20）；
            // 冷却 = GetExplosiveTrapCooldown：无套装 90（女猎手 T2 → 60、T3 → 30）；未命中时每 3 tick 扫一次
            [691] = new(144f, SummonRangeMetric.OwnerRect, 90),
            [692] = new(144f, SummonRangeMetric.OwnerRect, 90),
            [693] = new(144f, SummonRangeMetric.OwnerRect, 90),

            // AI_197_CeilingAndHoverTurret：射程 num8 = 1000 + num3 × 16 = 1000 + 15×16 = 1240，
            // 复用 AI_053 的**曼哈顿**索敌；冷却 localAI[0] 从 0 累加到 num4 = 50 后开火（有目标才归零）
            [1025] = new(1240f, SummonRangeMetric.Manhattan, 50),
        };
}
