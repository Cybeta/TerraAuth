// TerraAuth — 合成站 / 环境校验（SSC 合成守恒的「配方前置条件」）
//
// 背景：守恒判据只回答「净增量能不能由某条配方解释」，但原版合成还有**前置条件**——
// 必须站在合成站附近（工作台 / 砧 / 熔炉 / 锯木机 …）、部分配方还要紧邻水 / 蜂蜜 / 岩浆。
// 不校验这些条件，客户端只要有材料就能在**任意位置**合成高阶物品。
//
// 本文件镜像原版两段逻辑：
//   1. <c>Player.AdjTiles()</c>：把玩家**可达区域**内的图格（含 <c>Recipe.TileCountsAs</c> 等价图格）
//      收进 <c>adjTile</c>，并按 <c>tile.liquid &gt; 200</c> 与 <c>LiquidType</c> 置水 / 蜂蜜 / 岩浆标志；
//      区域来自 <c>TileReachCheckSettings.Simple</c> = X ±5 / Y ±3 图格（原版 <c>DefaultTileRangeX=5</c> /
//      <c>DefaultTileRangeY=3</c>，上限 20），水平 / 垂直以玩家碰撞盒（20×42）为基准。
//   2. <c>Recipe.PlayerMeetsEnvironmentConditions(player)</c>：<c>requiredTile</c> 须在 <c>adjTile</c> 内，
//      <c>needWater / needHoney / needLava</c> 须分别满足对应标志。
//
// **未校验**（依赖客户端分辨率的场景度量或玩家解锁状态，服务端不可确定性复现，见 RecipeTable 文件头）：
// needSnowBiome / needGraveyardBiome / needMechdusa / needTorchGodsFavor —— 这些配方仍只受材料与站位约束。

using System;
using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>
/// 玩家合成环境快照（语义对应原版 <c>Player.adjTile</c> + <c>adjWaterSource / adjHoney / adjLava</c>）。
/// 由 <see cref="CraftingEnvironmentSampler.Refresh"/> 在事务暂存时重取，提交时用于校验配方前置条件。
/// </summary>
public sealed class CraftingEnvironment
{
    /// <summary>可达区域内的图格类型（含等价图格展开，见 <see cref="RecipeTable.TileCountsAs"/>）。</summary>
    public readonly HashSet<int> AdjacentTiles = new();

    /// <summary>紧邻水源（原版 <c>adjWaterSource</c>）。</summary>
    public bool Water;

    /// <summary>紧邻蜂蜜（原版 <c>adjHoney</c>）。</summary>
    public bool Honey;

    /// <summary>紧邻岩浆（原版 <c>adjLava</c>）。</summary>
    public bool Lava;

    /// <summary>是否满足配方的合成站与液体前置条件（原版 <c>PlayerMeetsEnvironmentConditions</c>）。</summary>
    public bool Satisfies(int requiredTile, CraftEnvironment environment)
    {
        if (requiredTile >= 0 && !AdjacentTiles.Contains(requiredTile)) return false;
        if ((environment & CraftEnvironment.Water) != 0 && !Water) return false;
        if ((environment & CraftEnvironment.Honey) != 0 && !Honey) return false;
        if ((environment & CraftEnvironment.Lava) != 0 && !Lava) return false;
        return true;
    }
}

/// <summary>采集玩家当前的合成环境快照（镜像原版 <c>Player.AdjTiles()</c>）。</summary>
public static class CraftingEnvironmentSampler
{
    /// <summary>原版 <c>Player.DefaultTileRangeX</c>：水平可达 ±5 图格。</summary>
    public const int TileRangeX = 5;

    /// <summary>原版 <c>Player.DefaultTileRangeY</c>：垂直可达 ±3 图格。</summary>
    public const int TileRangeY = 3;

    /// <summary>原版液体判定阈值：<c>tile.liquid &gt; 200</c>（满格 255）。</summary>
    private const int LiquidThreshold = 200;

    /// <summary>算作水源的图格（原版 <c>TileID.Sets.CountsAsWaterForCrafting</c>，如水槽 / 喷泉）。</summary>
    private static readonly HashSet<int> WaterForCraftingTiles = new(RecipeTable.WaterForCraftingTiles);

    /// <summary>图格 → 等价图格（原版 <c>Recipe.TileCountsAs</c>，含多跳展开）。</summary>
    private static readonly Dictionary<int, int[]> CountsAs = BuildCountsAs();

    private static Dictionary<int, int[]> BuildCountsAs()
    {
        var direct = new Dictionary<int, List<int>>();
        foreach (var (tile, equivalent) in RecipeTable.TileCountsAs)
        {
            if (!direct.TryGetValue(tile, out var list))
            {
                list = new List<int>();
                direct[tile] = list;
            }
            list.Add(equivalent);
        }

        var closure = new Dictionary<int, int[]>(direct.Count);
        foreach (var tile in direct.Keys)
        {
            var set = new HashSet<int>();
            CollectEquivalents(tile, direct, set);
            set.Remove(tile);
            closure[tile] = new List<int>(set).ToArray();
        }
        return closure;
    }

    private static void CollectEquivalents(int tile, Dictionary<int, List<int>> direct, HashSet<int> set)
    {
        if (!set.Add(tile)) return;
        if (!direct.TryGetValue(tile, out var equivalents)) return;
        foreach (int equivalent in equivalents) CollectEquivalents(equivalent, direct, set);
    }

    /// <summary>
    /// 重取玩家合成环境（等价原版每 tick 的 <c>AdjTiles()</c>）。
    /// **锁序**：内部只取区块读锁（<see cref="WorldState.Sections"/>），调用方**不得**已持有
    /// <see cref="WorldState.PlayersLock"/> —— 必须在进 PlayersLock **之前**调用（全局锁序 SectionLocks → PlayersLock）。
    /// </summary>
    public static void Refresh(WorldState world, PlayerRuntime player)
    {
        var environment = player.CraftingEnvironment;
        environment.AdjacentTiles.Clear();
        environment.Water = false;
        environment.Honey = false;
        environment.Lava = false;

        var tiles = world.Tiles;
        int left = (int)(player.Position.X / 16f) - TileRangeX;
        int right = (int)Math.Ceiling((player.Position.X + NpcSizes.PlayerWidth) / 16f) - 1 + TileRangeX;
        int top = (int)(player.Position.Y / 16f) - TileRangeY;
        int bottom = (int)Math.Ceiling((player.Position.Y + NpcSizes.PlayerHeight) / 16f) - 1 + TileRangeY;

        if (left < 0) left = 0;
        if (top < 0) top = 0;
        if (right > tiles.Width - 1) right = tiles.Width - 1;
        if (bottom > tiles.Height - 1) bottom = tiles.Height - 1;
        if (left > right || top > bottom) return;   // 世界为空 / 玩家在界外：保持空快照

        using (world.Sections.EnterRead(left, top, right, bottom))
        {
            for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                var tile = tiles[x, y];
                if (tile.Active)
                {
                    AddTileType(environment.AdjacentTiles, tile.Type);
                    if (WaterForCraftingTiles.Contains(tile.Type)) environment.Water = true;
                }

                if (tile.Liquid <= LiquidThreshold) continue;
                switch (tile.LiquidType)
                {
                    case 0: environment.Water = true; break;
                    case 1: environment.Lava = true; break;
                    case 2: environment.Honey = true; break;
                }
            }
        }
    }

    private static void AddTileType(HashSet<int> tiles, int tileType)
    {
        if (!tiles.Add(tileType)) return;
        if (CountsAs.TryGetValue(tileType, out var equivalents))
            foreach (int equivalent in equivalents) tiles.Add(equivalent);
    }
}
