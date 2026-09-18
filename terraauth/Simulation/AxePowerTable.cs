// TerraAuth — 物品 ID → 斧力（原版 Item.axe 的已用子集）

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>用于树木额外木材判定的手持物品斧力表。</summary>
public static class AxePowerTable
{
    private static readonly Dictionary<int, int> Of = new()
    {
        [10] = 9,
        [45] = 15,
        [204] = 20,
        [217] = 30,
        [3482] = 12,
        [3488] = 11,
        [3494] = 10,
        [3500] = 7,
        [3506] = 8,
        [3512] = 11,
        [3518] = 12,
    };

    /// <summary>取物品斧力；未收录物品视为 0。</summary>
    public static int AxePowerOf(int itemId) => Of.TryGetValue(itemId, out var axePower) ? axePower : 0;
}
