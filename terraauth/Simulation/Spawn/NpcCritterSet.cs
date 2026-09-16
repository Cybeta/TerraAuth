// TerraAuth — 小动物（critter）集合与行为分派
//
// 权威来源：原版 <c>NPCID.Sets.CountsAsCritter</c>（Terraria 1.4.5.8，97 个类型）。
// 小动物在原版里的语义：不作为敌对目标（武器追踪 / 弹幕自瞄会排除它们）、可被虫网捕捉、
// 且各自有独立行为（鸟在空中游走、兔子贴地蹦跳、松鼠受惊逃走…）。
//
// 服务端的处理策略：
//   - **按原版各自的 aiStyle 逐类移植**（见 <see cref="AiStyleOf"/> 与 `WorldSimulator.Ai0XX*` 实现）。
//     这些 aiStyle 里混着「敌对追击」实现（1 = 史莱姆扑向玩家、3 = 战士冲向玩家），
//     故小动物不走通用 aiStyle 分派，而是走同名的 Ai0XX 方法里针对小动物的那条分支
//     （例如蚂蚱 377/446 复用 <c>Ai001Slimes</c> 的蚂蚱分支：`flag3` 时背向玩家跳跃）。
//   - 战斗属性（伤害 / 生命）仍取原版 <c>NPC.SetDefaults</c> 基准（见 NpcStatsTable），不另行改动。
//   - 飞行 / 贴地由各 AI 自行设定 <c>NoGravity</c> / <c>NoTileCollide</c>（原版 SetDefaults 初值 + AI 内改写）。

using System.Collections.Frozen;
using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>原版 <c>NPCID.Sets.CountsAsCritter</c> 与服务端的飞行 / 贴地分派。</summary>
public static class NpcCritterSet
{
    /// <summary>原版小动物类型（97 项，逐项照抄 <c>NPCID.Sets.CountsAsCritter</c>）。</summary>
    private static readonly FrozenSet<int> Types = new HashSet<int>
    {
        46, 303, 337, 540, 443, 74, 297, 298, 442, 611, 689, 377, 446, 612, 613, 356, 444,
        595, 596, 597, 598, 599, 600, 601, 604, 605, 357, 448, 374, 484, 355, 358, 606, 359,
        360, 485, 486, 487, 148, 149, 55, 230, 592, 593, 299, 538, 539, 300, 447, 361, 445,
        362, 363, 364, 365, 367, 366, 583, 584, 585, 602, 603, 607, 608, 609, 610, 616, 617,
        625, 626, 627, 615, 639, 640, 641, 642, 643, 644, 645, 646, 647, 648, 649, 650, 651,
        652, 653, 654, 655, 661, 669, 671, 672, 673, 674, 675, 677, 687, 688,
    }.ToFrozenSet();

    /// <summary>是否为小动物。</summary>
    public static bool Is(int type) => Types.Contains(type);

    /// <summary>
    /// 是否为飞行型小动物。按原版 <c>NPC.SetDefaults</c> 的 aiStyle 逐类核对：
    /// 24 = 鸟/金鸟，64 = 萤火虫/荧光虫，65 = 蝴蝶/金蝴蝶，112 = 仙灵，114 = 蜻蜓/金蜻蜓。
    /// </summary>
    public static bool IsFlyer(int type) => type is
        74 or 297 or 298 or 442 or 611 or 689 or >= 671 and <= 675 or   // Bird / GoldBird / Seagull（aiStyle 24）
        355 or 358 or 654 or 677 or                                      // Firefly / LightningBug（aiStyle 64）
        356 or 444 or 653 or 661 or                                      // Butterfly / GoldButterfly（aiStyle 65）
        583 or 584 or 585 or                                             // FairyCritter（aiStyle 112）
        >= 595 and <= 601 or                                             // Dragonfly / GoldDragonfly（aiStyle 114）
        669;                                                             // Ladybug（aiStyle 115，noGravity=true）

    /// <summary>
    /// 小动物 type → 原版 <c>NPC.aiStyle</c>（逐项核对 <c>NPC.SetDefaults</c>）。
    /// 服务端 AI 分派按此分发到对应 Ai0XX 移植实现；未列出的类型不会是 critter。
    /// </summary>
    public static int AiStyleOf(int type) => type switch
    {
        // aiStyle 7：城镇行走小动物（兔 / 企鹅 / 松鼠 / 青蛙 / 乌龟 / 海鸥 / 鸭子 616/617/625 / 兔 46 系列）
        46 or 148 or 149 or 230 or 299 or 300 or 303 or 337 or 361 or 362 or 364 or 366 or 367
            or 443 or 445 or 447 or 538 or 539 or 540 or 593 or 602 or 608 or 610 or 616 or 617
            or 625 or 687 or (>= 639 and <= 652) => 7,

        // aiStyle 1：史莱姆家族；377/446 蚂蚱走 AI_001 的蚂蚱分支
        377 or 446 => 1,

        // aiStyle 16：鱼（55/592/607/615 走通用湿/干分支；688 受击跳跃分支）
        55 or 592 or 607 or 615 or 688 => 16,

        // aiStyle 24：鸟 / 金鸟 / 海鸥（611/689 初始即飞；671-675 降速参数不同）
        74 or 297 or 298 or 442 or 611 or 689 or (>= 671 and <= 675) => 24,

        // aiStyle 64：萤火虫 / 荧光虫（677 noTileCollide=true 的边界转向分支）
        355 or 358 or 654 or 677 => 64,

        // aiStyle 65：蝴蝶 / 金蝴蝶
        356 or 444 or 653 or 661 => 65,

        // aiStyle 66：蚯蚓 / 金蚯蚓 / 岩浆蚯蚓（374 受惊变形分支只计数）
        357 or 448 or 374 or 484 or 485 or 486 or 487 or 606 => 66,

        // aiStyle 67：蜗牛 / 荧光蜗牛（爬墙状态机）
        359 or 360 or 655 => 67,

        // aiStyle 68：鸭 / 金鸭（地面走 + 遇水游）
        363 or 365 or 603 or 609 => 68,

        // aiStyle 112：仙灵（state 0 悬停 / state 1 逃离）
        583 or 584 or 585 => 112,

        // aiStyle 114：蜻蜓 / 金蜻蜓
        >= 595 and <= 601 => 114,

        // aiStyle 115：瓢虫 / 金瓢虫（飞行 + 落地）
        604 or 605 or 669 => 115,

        // aiStyle 116：水黾（水面行走）
        612 or 613 => 116,

        // aiStyle 118：海马（水中悬浮）
        626 or 627 => 118,

        _ => 0,
    };
}
