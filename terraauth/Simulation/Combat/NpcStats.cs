// TerraAuth — 服务端权威伤害结算：NPC 基础属性表
// 数据源：Terraria 1.4.5.8 原版 NPC.SetDefaults 数据（经典难度基准值；专家/大师倍率见 CombatResolver）

namespace TerraAuth.Simulation;

/// <summary>NPC 基础战斗属性（经典难度基准）。</summary>
public readonly record struct NpcStats(int Damage, int Defense, int LifeMax);

/// <summary>
/// 服务端权威战斗结算用 NPC 属性表：type → (伤害 / 防御 / 最大生命)。
/// 先覆盖服务端实际会刷的类型；未收录类型由调用方兜底。
/// </summary>
public static class NpcStatsTable
{
    public static readonly IReadOnlyDictionary<int, NpcStats> Of = new Dictionary<int, NpcStats>
    {
        [1] = new NpcStats(7, 2, 25),       // Blue Slime
        [2] = new NpcStats(18, 2, 60),      // Green Slime
        [3] = new NpcStats(14, 6, 45),      // Pinky
        [4] = new NpcStats(15, 12, 2800),   // Eye of Cthulhu
        [26] = new NpcStats(12, 4, 60),     // Goblin Peon
        [35] = new NpcStats(32, 10, 4400),  // Skeletron
        [50] = new NpcStats(40, 10, 2000),  // King Slime
    };
}
