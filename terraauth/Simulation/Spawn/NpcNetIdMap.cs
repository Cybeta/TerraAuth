// TerraAuth — 负向 netID → 基础 NPC 类型映射
//
// 权威来源：原版 Terraria.ID.NPCID.NetIdMap（1.4.5.8 / Protocol 326）
//   FromNetId(id) = id < 0 ? NetIdMap[-id - 1] : id
//
// 原版用「负 netID」表达同一 `type` 的体型 / 配色变体（如 -3 = 绿史莱姆，type 仍为 1）。
// 服务端刷怪会挑中这些负值，因此：
//   - 战斗属性（伤害 / 防御 / 生命）必须按 FromNetId 解析后的基础类型取；
//   - 包 23 下发的仍是负 netID，客户端据此选贴图。

namespace TerraAuth.Simulation;

/// <summary>原版 <c>NPCID.FromNetId</c> 的等价实现。</summary>
public static class NpcNetIdMap
{
    /// <summary>原版 <c>NPCID.NetIdMap</c>（65 项；索引 = <c>-netId - 1</c>）。</summary>
    private static readonly int[] Map =
    {
        81, 81, 1, 1, 1, 1, 1, 1, 1, 1,
        6, 6, 31, 31, 77, 42, 42, 176, 176, 176,
        176, 173, 173, 183, 183, 3, 3, 132, 132, 186,
        186, 187, 187, 188, 188, 189, 189, 190, 191, 192,
        193, 194, 2, 200, 200, 21, 21, 201, 201, 202,
        202, 203, 203, 223, 223, 231, 231, 232, 232, 233,
        233, 234, 234, 235, 235,
    };

    /// <summary>负向 netID 解析为基础 <c>type</c>；正值原样返回。</summary>
    public static int FromNetId(int netId)
    {
        if (netId >= 0) return netId;

        int index = -netId - 1;
        // 越界（协议外取值）时以绝对值兜底，避免越界读；原版此处直接索引，我们保留边界防御。
        return index < Map.Length ? Map[index] : -netId;
    }
}
