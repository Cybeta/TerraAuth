// TerraAuth — 面向玩家提示的物品中文显示名

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>物品 ID → 玩家聊天提示用的中文显示名。</summary>
public static class ItemDisplayNameTable
{
    private static readonly Dictionary<int, string> Of = new()
    {
        [2] = "泥土块",
        [3] = "石块",
        [5] = "蘑菇",
        [9] = "木材",
        [11] = "铁矿石",
        [12] = "铜矿石",
        [13] = "金矿石",
        [14] = "银矿石",
        [23] = "凝胶",
        [27] = "橡实",
        [29] = "生命水晶",
        [56] = "魔矿",
        [58] = "心",
        [169] = "沙块",
        [183] = "蘑菇草",
        [307] = "太阳花种子",
        [308] = "月光草种子",
        [309] = "闪耀根种子",
        [310] = "死亡草种子",
        [311] = "水草种子",
        [312] = "火焰花种子",
        [313] = "太阳花",
        [314] = "月光草",
        [315] = "闪耀根",
        [316] = "死亡草",
        [317] = "水草",
        [318] = "火焰花",
        [757] = "泰拉刃",
        [122] = "熔岩镐",
        [2357] = "寒颤棘种子",
        [2358] = "寒颤棘",
        [3319] = "可疑眼球宝藏袋",
    };

    /// <summary>取玩家显示名；未收录时回退原版标识名。</summary>
    public static string NameOf(int itemId)
        => Of.TryGetValue(itemId, out var name) ? name : ItemNameTable.NameOf(itemId);
}
