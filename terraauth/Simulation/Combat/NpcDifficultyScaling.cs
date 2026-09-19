// TerraAuth — NPC 难度缩放的「非曲线」部分（原版 NPC.ScaleStats 链）
//
// 原版 `NPC.ScaleStats(activePlayersCount, strengthOverride)`（NPC.cs L18316）的顺序：
//   1) `difficulty >= Expert && Main.hardMode` → `ScaleStats_ForExpertHardmode()`  → 本文件 NpcHardmodeScaling
//   2) `ScaleStats_ByDifficulty()`                                              → 已落：CombatResolver.ScaleNpcLifeMax / ScaleNpcDamage
//   3) `difficulty >= Expert` → `ScaleStats_ByPlayerCount(activePlayersCount)`  → 本文件 NpcPlayerCountScaling
//   4) 弹幕类以外 `lifeMax < 6` → 6；`life = lifeMax`
//
// **为什么必须与客户端一致**：包 23 不下发 NPC 的 `lifeMax`，客户端收到后自行 `SetDefaults` +
// 同一条 `ScaleStats` 链重算上限（难度取自包内 difficulty 段、人数取自包内玩家数段）。
// 服务端与客户端任何一处的条件表不一致 → 血条上限发散（满血怪显示半格 / 掉不动）。

using System;
using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>
/// 原版 <c>NPC.ScaleStats_ForExpertHardmode()</c>（NPC.cs L18683）：专家及以上且**困难模式**下，
/// 把强度不足的普通怪补到最低强度线（`damage + defense + lifeMax/4` 低于 80，击败世纪之花后 100）。
/// </summary>
public static class NpcHardmodeScaling
{
    /// <summary>原版 <c>NPCID.Sets.ProjectileNPC</c>（NPCID.cs L11064）：投射物型 NPC 只放大伤害。</summary>
    private static readonly HashSet<int> ProjectileNpcTypes = new()
    {
        25, 30, 665, 33, 112, 666, 261, 265, 371, 516, 519,
    };

    /// <summary>原版 <c>NPCID.Sets.DontDoHardmodeScaling</c>（NPCID.cs L10732）：这些类型不参与上表补强。</summary>
    private static readonly HashSet<int> DontDoHardmodeScalingTypes = new()
    {
        5, 13, 14, 15, 267, 113, 114, 115, 116, 117, 118, 119, 658, 659, 660, 400, 522,
    };

    /// <summary>是否为原版「投射物型 NPC」（只放大伤害，不动防御 / 生命上限）。</summary>
    public static bool IsProjectileNpc(int type) => ProjectileNpcTypes.Contains(type);

    /// <summary>
    /// 按困难模式补强 <paramref name="npc"/>（就地修改 damage / defense / lifeMax）。
    /// <para>
    /// 注意原版是 **整数除法**：<c>float factor = 目标强度 / 当前强度</c> 里两个操作数都是 int，
    /// 结果先截断再参与乘算（例：强度 15 → factor = 5 而不是 5.33）。此处照抄该口径。
    /// </para>
    /// 未建模：`getGoodWorld` 特殊种子分支（本服务端不产该种子）、`value`（金币数值缩放，本服务端未建模金币掉落）。
    /// </summary>
    public static void Apply(WorldNpc npc, bool downedPlantBoss)
    {
        if (npc.IsBoss || npc.LifeMax >= 1000) return;                    // 原版：boss || lifeMax >= 1000 → 不补强
        if (DontDoHardmodeScalingTypes.Contains(npc.Type)) return;

        int power = npc.Damage + npc.Defense + npc.LifeMax / 4;
        if (power == 0) power = 1;                                        // 原版：全 0 时按 1 处理，避免除零
        int target = downedPlantBoss ? 100 : 80;
        if (power >= target) return;

        int factor = target / power;                                      // 原版整数除法（截断）
        npc.Damage = (int)(npc.Damage * factor * 0.9f);
        if (ProjectileNpcTypes.Contains(npc.Type)) return;                 // 投射物型：只放大伤害
        npc.Defense = (int)(npc.Defense * factor);
        npc.LifeMax = (int)(npc.LifeMax * factor * 1.1f);
    }
}

/// <summary>
/// 原版 <c>NPC.ScaleStats_ByPlayerCount(numPlayers)</c>（NPC.cs L18733）：专家及以上按**在线人数**
/// 放大 NPC 生命上限（伤害不随人数变化；另有 3 个类型的击退抗性削弱，本服务端未模拟击退，故不落）。
/// </summary>
public static class NpcPlayerCountScaling
{
    /// <summary>原版 <c>NPC.GetStatScalingFactors</c>（NPC.cs L18895）：balance 从 1 起，每多一名玩家
    /// 加上当前 boost，且 boost 按 <c>+ (1 - boost) / 3</c> 递增（收敛到 1）；balance &gt; 8 后增速放缓，上限 1000。</summary>
    public static void GetStatScalingFactors(int numPlayers, out double balance, out double boost)
    {
        balance = 1.0;
        boost = 0.35;
        for (int i = 1; i < numPlayers; i++)
        {
            balance += boost;
            boost += (1.0 - boost) / 3.0;
        }

        if (balance > 8.0) balance = (balance * 2.0 + 8.0) / 3.0;
        if (balance > 1000.0) balance = 1000.0;
    }

    /// <summary>原版 <c>NPCID.Sets.BelongsToInvasionOldOnesArmy</c>（NPCID.cs L11048）：撒旦军队单位按 0.857 插值。</summary>
    private static readonly HashSet<int> OldOnesArmyTypes = new()
    {
        552, 553, 554, 561, 562, 563, 555, 556, 557, 558, 559, 560, 576, 577, 568, 569,
        566, 567, 570, 571, 572, 573, 548, 549, 564, 565, 574, 575, 551, 578,
    };

    /// <summary>原版 <c>NPC.GetNPCInvasionGroup</c>（NPC.cs L92100）中返回 **-1** 的类型（火星暴乱）。</summary>
    private static readonly HashSet<int> InvasionGroupMinusOneTypes = new()
    {
        338, 339, 340, 341, 342, 343, 344, 345, 346, 347, 348, 349, 350, 351, 352,
    };

    /// <summary>原版 <c>NPC.GetNPCInvasionGroup</c> 中返回 **-2** 的类型（旧日军团 1 / 2 级）。</summary>
    private static readonly HashSet<int> InvasionGroupMinusTwoTypes = new()
    {
        305, 306, 307, 308, 309, 310, 311, 312, 313, 314, 315, 325, 326, 327, 329, 330,
    };

    /// <summary>原版该分支里被显式排除的 6 个类型（<c>case 315/325/327/344/345/346: break;</c>）。</summary>
    private static readonly HashSet<int> InvasionScalingExceptions = new() { 315, 325, 327, 344, 345, 346 };

    /// <summary>
    /// 按在线人数缩放 <paramref name="npc"/> 的生命上限，并把「按几人缩放」记进实体
    /// （<see cref="WorldNpc.StatsScaledForPlayers"/>，供包 23 的玩家数段同步给客户端）。
    /// <para>
    /// 未在类型表中的 NPC 其倍率为 1（生命不变），但**人数仍要记录并下发** —— 原版同样如此，
    /// 客户端拿到同一人数后按同一张表算出的倍率也是 1，两边一致。
    /// </para>
    /// </summary>
    public static void Apply(WorldNpc npc, int numPlayers)
    {
        if (numPlayers < 1) numPlayers = 1;
        npc.StatsScaledForPlayers = numPlayers;
        if (numPlayers == 1) return;   // balance = 1 → 所有分支都是恒等

        GetStatScalingFactors(numPlayers, out double balance, out _);
        int type = npc.Type;
        double num = 1.0;

        // ---- 显式类型表（逐条镜像原版，顺序无关，均为连乘）----
        if (type == 4) num *= balance;                                  // 克苏鲁之眼
        if (type is >= 13 and <= 15) num *= balance;                    // 世界吞噬者分段
        if (type is 266 or 267) num *= balance;                         // 克苏鲁之脑 / 飞眼
        if (type == 50) num *= balance;                                 // 史莱姆王
        if (type == 471) num *= Lerp(1.0, balance, 2.0 / 3.0);
        if (type == 472) num *= Lerp(1.0, balance, 0.5);
        if (type == 222) num *= balance;                                // 蜂后
        if (type is 35 or 36) num *= balance;                           // 骷髅王头 / 手
        if (type == 668) num *= balance;                                // 独眼巨鹿
        if (type is 113 or 114) num *= balance;                         // 血肉墙
        else if (type is 115 or 116) num *= balance;
        if (type == 657) num *= balance;                                // 史莱姆皇后
        if (type is >= 658 and <= 660) num *= balance;
        if (type is >= 134 and <= 136) num *= balance;                  // 毁灭者
        else if (type == 139) num *= Lerp(1.0, balance, 2.0 / 3.0);
        if (type is >= 127 and <= 131) num *= balance;                  // 机械骷髅王
        if (type is >= 125 and <= 126) num *= balance;                  // 双子魔眼
        if (type == 262) num *= balance;                                // 世纪之花
        else if (type == 264) num *= balance;
        if (type == 636) num *= balance;                                // 光之女皇
        if (type is >= 245 and <= 249) num *= balance;                  // 石巨人
        if (type == 370) num *= balance;                                // 猪龙鱼公爵
        if (type == 439 || type == 440 || type is >= 454 and <= 459 || type == 523) num *= balance;   // 拜月教 / 月亮事件
        if (type is 396 or 397 or 398) num *= balance;                   // 月亮领主
        if (type == 551) num *= balance;
        else if (OldOnesArmyTypes.Contains(type)) num *= Lerp(1.0, balance, 0.8571428656578064);

        // ---- 入侵怪通用分支：group == -1 / -2 时按 1 + (人数-1)×0.2 ----
        if ((InvasionGroupMinusOneTypes.Contains(type) || InvasionGroupMinusTwoTypes.Contains(type))
            && !InvasionScalingExceptions.Contains(type))
            num *= 1.0 + (numPlayers - 1) * 0.2;

        npc.LifeMax = (int)Math.Round(npc.LifeMax * num);
    }

    /// <summary>原版 <c>Utils.Lerp(double, double, double)</c>：线性插值。</summary>
    private static double Lerp(double value1, double value2, double amount)
        => value1 + (value2 - value1) * amount;
}
