// TerraAuth — 合成站 / 环境校验（SSC 合成守恒的「配方前置条件」）
//
// 背景：守恒判据只回答「净增量能不能由某条配方解释」，但原版合成还有**前置条件**——
// 必须站在合成站附近（工作台 / 砧 / 熔炉 / 锯木机 …）、部分配方还要紧邻水 / 蜂蜜 / 岩浆。
// 不校验这些条件，客户端只要有材料就能在**任意位置**合成高阶物品。
//
// 本文件镜像原版三段逻辑：
//   1. <c>Player.AdjTiles()</c>：把玩家**可达区域**内的图格（含 <c>Recipe.TileCountsAs</c> 等价图格）
//      收进 <c>adjTile</c>，并按 <c>tile.liquid &gt; 200</c> 与 <c>LiquidType</c> 置水 / 蜂蜜 / 岩浆标志；
//      区域来自 <c>TileReachCheckSettings.Simple</c> = X ±5 / Y ±3 图格（原版 <c>DefaultTileRangeX=5</c> /
//      <c>DefaultTileRangeY=3</c>，上限 20），水平 / 垂直以玩家碰撞盒（20×42）为基准。
//   2. <c>SceneMetrics.ScanTiles()</c>：以玩家所在图格为中心、按**固定尺寸** <c>ZoneScanSize</c> 统计图格
//      （原版 <c>AssumedConstantScreenSize</c> = 1920×1200 + <c>ZoneScanPadding</c> 25 → 169×124 图格），
//      据此得到 <c>ZoneSnow</c>（雪原图格 ≥ 1500）与 <c>ZoneGraveyard</c>（墓碑数 − 向日葵数/2 ≥ 28）。
//      **该区域是固定常数，不随客户端分辨率变化**（分辨率只影响 <c>VisualScanArea</c> 的屏上图格统计，
//      与这两个群系判定无关），故服务端可确定性复现。
//   3. <c>Recipe.PlayerMeetsEnvironmentConditions(player)</c>：<c>requiredTile</c> 须在 <c>adjTile</c> 内，
//      <c>needWater / needHoney / needLava</c> 与 <c>needSnowBiome / needGraveyardBiome</c> 取 1 / 2 的采集结果，
//      <c>needMechdusa / needTorchGodsFavor</c> 分别取世界特性与玩家解锁状态（见各自字段注释）。

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

    /// <summary>身处雪原生物群系（原版 <c>player.ZoneSnow</c>：场景扫描区内雪原图格 ≥ 1500）。</summary>
    public bool SnowBiome;

    /// <summary>身处墓地生物群系（原版 <c>player.ZoneGraveyard</c>：场景扫描区内墓碑数 − 向日葵数/2 ≥ 28）。</summary>
    public bool GraveyardBiome;

    /// <summary>世界具备「机械三王」特性（原版 <c>SpecialSeedFeatures.Mechdusa</c> = remixWorld ∧ getGoodWorld）。</summary>
    public bool Mechdusa;

    /// <summary>玩家已解锁火把神恩（原版 <c>player.unlockedBiomeTorches</c>，由包 4 上报）。</summary>
    public bool TorchGodsFavor;

    /// <summary>是否满足配方的合成站与环境前置条件（原版 <c>PlayerMeetsEnvironmentConditions</c>）。</summary>
    public bool Satisfies(int requiredTile, CraftEnvironment environment)
    {
        if (requiredTile >= 0 && !AdjacentTiles.Contains(requiredTile)) return false;
        if ((environment & CraftEnvironment.Water) != 0 && !Water) return false;
        if ((environment & CraftEnvironment.Honey) != 0 && !Honey) return false;
        if ((environment & CraftEnvironment.Lava) != 0 && !Lava) return false;
        if ((environment & CraftEnvironment.SnowBiome) != 0 && !SnowBiome) return false;
        if ((environment & CraftEnvironment.GraveyardBiome) != 0 && !GraveyardBiome) return false;
        if ((environment & CraftEnvironment.Mechdusa) != 0 && !Mechdusa) return false;
        if ((environment & CraftEnvironment.TorchGodsFavor) != 0 && !TorchGodsFavor) return false;
        return true;
    }
}

/// <summary>采集玩家当前的合成环境快照（镜像原版 <c>Player.AdjTiles()</c> + <c>SceneMetrics</c> 群系计数）。</summary>
public static class CraftingEnvironmentSampler
{
    /// <summary>原版 <c>Player.DefaultTileRangeX</c>：水平可达 ±5 图格。</summary>
    public const int TileRangeX = 5;

    /// <summary>原版 <c>Player.DefaultTileRangeY</c>：垂直可达 ±3 图格。</summary>
    public const int TileRangeY = 3;

    /// <summary>原版 <c>SceneMetrics.ZoneScanSize.X</c>：场景度量扫描区宽 = 1920/16 + 25×2 − 1 = 169 图格。</summary>
    public const int ZoneScanWidth = 169;

    /// <summary>原版 <c>SceneMetrics.ZoneScanSize.Y</c>：场景度量扫描区高 = 1200/16 + 25×2 − 1 = 124 图格。</summary>
    public const int ZoneScanHeight = 124;

    /// <summary>原版液体判定阈值：<c>tile.liquid &gt; 200</c>（满格 255）。</summary>
    private const int LiquidThreshold = 200;

    /// <summary>原版 <c>SceneMetrics.SnowTileNormalThreshold</c>：雪原图格数阈值（天空岛种子为 300，本服务端不产该种子）。</summary>
    private const int SnowTileThreshold = 1500;

    /// <summary>原版 <c>SceneMetrics.GraveyardTileThreshold</c>：墓地计数阈值。</summary>
    private const int GraveyardTileThreshold = 28;

    /// <summary>原版墓碑图格（<c>_tileCounts[85]</c>）。</summary>
    private const int GravestoneTile = 85;

    /// <summary>原版向日葵图格：每 2 个抵消 1 个墓碑（<c>GraveyardTileCount -= _tileCounts[27] / 2</c>）。</summary>
    private const int SunflowerTile = 27;

    /// <summary>原版 <c>SceneMetrics.SnowTileCount</c> 计入的雪原图格（147 / 148 / 161 / 162 / 163 / 164 / 200）。</summary>
    private static readonly HashSet<int> SnowTiles = new() { 147, 148, 161, 162, 163, 164, 200 };

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
    /// 重取玩家合成环境（等价原版每 tick 的 <c>AdjTiles()</c> 与 <c>SceneMetrics.Scan</c>）。
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
        environment.SnowBiome = false;
        environment.GraveyardBiome = false;
        environment.Mechdusa = world.MechdusaFeature;
        environment.TorchGodsFavor = player.UnlockedBiomeTorches;

        var tiles = world.Tiles;

        // 可达区域（原版 Player.AdjTiles）：以玩家碰撞盒（20×42）为基准的 X ±5 / Y ±3 图格。
        int left = (int)(player.Position.X / 16f) - TileRangeX;
        int right = (int)Math.Ceiling((player.Position.X + NpcSizes.PlayerWidth) / 16f) - 1 + TileRangeX;
        int top = (int)(player.Position.Y / 16f) - TileRangeY;
        int bottom = (int)Math.Ceiling((player.Position.Y + NpcSizes.PlayerHeight) / 16f) - 1 + TileRangeY;

        // 场景度量扫描区（原版 SceneMetrics.ScanTiles）：以玩家中心所在图格为中心的固定 169×124 图格。
        int centerX = ((int)(player.Position.X + NpcSizes.PlayerWidth / 2f)) >> 4;
        int centerY = ((int)(player.Position.Y + NpcSizes.PlayerHeight / 2f)) >> 4;
        int zoneLeft = centerX - ZoneScanWidth / 2;
        int zoneRight = zoneLeft + ZoneScanWidth - 1;
        int zoneTop = centerY - ZoneScanHeight / 2;
        int zoneBottom = zoneTop + ZoneScanHeight - 1;

        if (left < 0) left = 0;
        if (top < 0) top = 0;
        if (right > tiles.Width - 1) right = tiles.Width - 1;
        if (bottom > tiles.Height - 1) bottom = tiles.Height - 1;
        if (zoneLeft < 0) zoneLeft = 0;
        if (zoneTop < 0) zoneTop = 0;
        if (zoneRight > tiles.Width - 1) zoneRight = tiles.Width - 1;
        if (zoneBottom > tiles.Height - 1) zoneBottom = tiles.Height - 1;

        // 可达区域恒被场景度量扫描区包含（±5/±3 远小于 ±84/±62，且两者同中心、同样夹取到世界边界），
        // 故一次读锁覆盖并集即可；界外（世界为空 / 玩家在界外）时保持空快照。
        if (zoneLeft > zoneRight || zoneTop > zoneBottom) return;

        int snowTileCount = 0;
        int graveyardTileCount = 0;
        int sunflowerCount = 0;

        using (world.Sections.EnterRead(zoneLeft, zoneTop, zoneRight, zoneBottom))
        {
            for (int x = zoneLeft; x <= zoneRight; x++)
            for (int y = zoneTop; y <= zoneBottom; y++)
            {
                var tile = tiles[x, y];
                if (tile.Active)
                {
                    if (SnowTiles.Contains(tile.Type)) snowTileCount++;
                    else if (tile.Type == GravestoneTile) graveyardTileCount++;
                    else if (tile.Type == SunflowerTile) sunflowerCount++;
                }

                if (x < left || x > right || y < top || y > bottom) continue;

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

        environment.SnowBiome = snowTileCount >= SnowTileThreshold;
        environment.GraveyardBiome = graveyardTileCount - sunflowerCount / 2 >= GraveyardTileThreshold;
    }

    private static void AddTileType(HashSet<int> tiles, int tileType)
    {
        if (!tiles.Add(tileType)) return;
        if (CountsAs.TryGetValue(tileType, out var equivalents))
            foreach (int equivalent in equivalents) tiles.Add(equivalent);
    }
}
