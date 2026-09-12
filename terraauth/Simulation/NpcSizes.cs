// TerraAuth — 实体碰撞盒尺寸表（按原版 width/height 核对）
//
// 原版约定：实体 <c>position</c> = **碰撞盒左上角**，脚底 = <c>position.Y + height</c>。
// 服务端同步（包 23）下发的 position 必须是这一口径 —— 否则客户端会按**自己的**尺寸解释该坐标：
// 贴图会陷地 / 悬空，且客户端自身 AI 的图格碰撞会与服务端下发的位置互相纠正，表现为持续抖动。
// （原版客户端收到包 23 后即 `npc.position = 收到的位置 − 同步锚点`，随后按 `velocity` / `ai[]` 自行推进。）
//
// 数值来源：
//   - NPC：原版 `NPC.SetDefaults` 的 `width` / `height`（逐类型核对）；
//   - 玩家：原版 `Player.cs` 的 `width = 20` / `height = 42`。

namespace TerraAuth.Simulation;

/// <summary>实体碰撞盒尺寸（像素）。</summary>
public static class NpcSizes
{
    /// <summary>玩家碰撞盒宽度（原版 <c>Player.cs</c>）。</summary>
    public const int PlayerWidth = 20;

    /// <summary>玩家碰撞盒高度（原版 <c>Player.cs</c>）；脚底 = <c>position.Y + 42</c>。</summary>
    public const int PlayerHeight = 42;

    /// <summary>未登记类型的兜底尺寸（保守取 32×32，避免未知 NPC 行为突变）。</summary>
    public const int FallbackWidth = 32;
    public const int FallbackHeight = 32;

    /// <summary>按原版 <c>NPC.type</c> 取碰撞盒尺寸。</summary>
    public static (int Width, int Height) Of(int npcType) => npcType switch
    {
        1 => (24, 18),      // Blue Slime
        4 => (100, 110),    // Eye of Cthulhu
        5 => (20, 20),      // Servant of Cthulhu
        22 => (18, 40),     // Guide（城镇 NPC 通用 18×40）
        26 => (18, 38),     // Goblin Peon
        35 => (80, 102),    // Skeletron
        50 => (98, 92),     // King Slime
        126 => (100, 110),  // Spazmatism
        210 => (12, 12),    // Hornet
        211 => (8, 8),      // Hornet（小）
        222 => (66, 66),    // Queen Bee
        _ => (FallbackWidth, FallbackHeight),
    };
}
