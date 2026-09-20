// TerraAuth — 宝袋（BossBag）战利品数据表
//
// 来源：原版 1.4.5.8 `Player.OpenBossBag(int type)`（逐条核对开袋分支与随机上限）
//   的 `switch (type)` 各 case，逐条抄录 `QuickSpawnItem(...)` 的调用（物品 + 数量区间）。
//
// 用途：SSC 下开袋的战利品**由客户端掷骰**（原版就是这样：`OpenBossBag` 里用 `Main.rand`），
//   服务端不再自行随机（两边各掷一次必然冲突，旧实现的「服务端随机 + 客户端回显」互相覆盖，
//   真机表现为「战利品出现约 1 秒后全部消失」）。服务端改为**按本表校验客户端上报的战利品**：
//   净增量必须是该袋战利品池内的物品、且不超过单次开袋的上限（见 CraftingConservation 的宝袋规则）。
//
// 已建模：仅眼魔宝袋 3319（对应后台 W-2 之外的宝袋条目，其余 20 个袋先「失败关闭」：
//   袋的净减少得不到战利品解释 → 回滚，袋保留在背包里，不会被静默销毁）。
//
// 口径说明：
//   - 单次上限取**腐化 / 猩红两个世界变体的并集**（880/56 与 2171/59 各取上限），
//     故猩红世界的客户端理论上可上报 56（腐化矿）—— 唯一后果是同一件战利品换了变体，
//     不构成凭空造物，故不做世界变体区分（校验函数保持纯函数，无需世界上下文）。
//   - 可选条目（2112 1/7、1299 1/30）上限为 1：概率无法在服务端复核，只约束件数。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>宝袋战利品条目：物品 ID + 单次开袋可获得的最大堆叠数（0 = 不出现）。</summary>
public readonly record struct BossBagLootEntry(int ItemId, int MaxStack);

/// <summary>
/// 宝袋（BossBag）识别与战利品池。数据源见文件头。
/// </summary>
public static class BossBagLootTable
{
    /// <summary>
    /// 原版 <c>ItemID.Sets.BossBag</c>（共 21 个袋 ID）：
    /// <c>Factory.CreateBoolSet(3318 … 5111)</c>）：21 个宝袋 ID。
    /// </summary>
    public static readonly IReadOnlySet<int> Bags = new HashSet<int>
    {
        3318, 3319, 3320, 3321, 3322, 3323, 3324, 3325, 3326, 3327, 3328, 3329,
        3330, 3331, 3332, 3860, 3861, 3862, 4782, 4957, 5111,
    };

    /// <summary>已建模战利品池的袋 ID → 条目数（表头口径，改动时同步）。</summary>
    public static readonly IReadOnlyDictionary<int, int> ModeledCounts = new Dictionary<int, int>
    {
        [3319] = 8,
    };

    /// <summary>眼魔宝袋 3319 战利品池（`Player.OpenBossBag` case 3319）。</summary>
    private static readonly BossBagLootEntry[] EyeOfCthulhu =
    {
        new(2112, 1),      // 1/7 可选
        new(1299, 1),      // 1/30 可选
        new(880, 90),      // 猩红矿 30..90
        new(56, 90),       // 腐化矿 30..90（另一世界变体）
        new(2171, 3),      // 猩红种子 1..3
        new(59, 3),        // 腐化种子 1..3
        new(47, 50),       // 20..50
        new(3097, 1),      // 1
    };

    private static readonly Dictionary<int, BossBagLootEntry[]> Pools = new()
    {
        [3319] = EyeOfCthulhu,
    };

    /// <summary>是否宝袋（原版 <c>ItemID.Sets.BossBag</c>）。</summary>
    public static bool IsBag(int itemId) => Bags.Contains(itemId);

    /// <summary>
    /// 开袋附带的**金币**：原版 <c>Player.OpenBossBag</c> 结尾把 Boss 的 <c>NPC.value</c>（铜币）
    /// 乘以随机系数后按 1 000 000 / 10 000 / 100 / 1 换算成铂金 / 金币 / 银币 / 铜币
    /// （<c>QuickSpawnItem(74/73/72/71, n)</c>）。故开袋**不只是装备，还给钱**——
    /// 早期实现漏了这段，真机表现为「开袋后金币出现又消失」（被守恒判据当凭空造物回滚）。
    /// 键 = 宝袋 ID，值 = 该 Boss 的 <c>NPC.value</c>（铜币）。
    /// </summary>
    public static readonly IReadOnlyDictionary<int, long> CoinValueByBag = new Dictionary<int, long>
    {
        [3319] = 30000,   // 克苏鲁之眼：NPC 4，NPC.cs 中 value = 30000f（3 金币）
    };

    /// <summary>是否钱币（铜 / 银 / 金 / 铂金）。</summary>
    public static bool IsCoin(int itemId) => itemId is 71 or 72 or 73 or 74;

    /// <summary>钱币件数折算成铜币总量。</summary>
    public static long CoinCopper(int itemId, int count) => itemId switch
    {
        71 => count,
        72 => count * 100L,
        73 => count * 10000L,
        74 => count * 1000000L,
        _ => 0,
    };

    /// <summary>
    /// 开袋金币是否落在允许区间：基础值 × [0.8, 5]。
    /// <para>
    /// 下界 0.8 = 原版唯一那次 <c>(1 ± 20%)</c>；上界**按原版系数链算出来是 1.2 × 1.10 × 1.20 × 1.30 × 1.40
    /// ≈ 2.883×**（四层可选加成依次可能触发），但真机实测同一袋（3319，基础 30000 铜）客户端给出
    /// 12 金 30 银 61 铜 = **4.1×** —— 客户端侧的掷骰链与我们对原版公式的理解并不完全一致。
    /// 金币是**客户端掷骰**的结果，服务端只能设上界防止凭空造钱：取 5× 既容得下实测值，
    /// 又挡住离谱数值（掉一次袋凭空多出几十倍基础值）。**不要按 2.883 收窄回去**，否则真机金币又被吞。
    /// </para>
    /// </summary>
    public static bool CoinRewardAllowed(int bagItemId, long copper)
        => CoinValueByBag.TryGetValue(bagItemId, out var value)
           && copper >= (long)(value * 0.8)
           && copper <= (long)(value * 5) + 1;

    /// <summary>该袋是否已建模战利品池；未建模的袋一律失败关闭（净减少得不到解释 → 回滚）。</summary>
    public static bool TryGetLoot(int bagItemId, out BossBagLootEntry[] loot)
        => Pools.TryGetValue(bagItemId, out loot!);

    /// <summary>给定物品是否为该袋战利品池成员；返回其单次上限。</summary>
    public static bool TryGetLootLimit(int bagItemId, int lootItemId, out int maxStack)
    {
        maxStack = 0;
        if (!Pools.TryGetValue(bagItemId, out var loot)) return false;
        foreach (var entry in loot)
        {
            if (entry.ItemId != lootItemId) continue;
            maxStack = entry.MaxStack;
            return true;
        }
        return false;
    }
}
