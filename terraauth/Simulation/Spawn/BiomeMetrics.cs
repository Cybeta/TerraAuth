// TerraAuth — 服务端场景度量（原版 Terraria.SceneMetrics 的「刷怪相关」子集）
//
// 目的：为刷怪规则提供与原版一致的**生物群系 / 深度带**判定。
// 原版这些标志由客户端 <c>SceneMetrics</c> 每帧扫描本地玩家周围图格后写入 <c>Player.Zone*</c>，
// 而 `NPC.Spawner` 读的正是这些 <c>Player.Zone*</c>（<c>NPC.cs</c> 的 <c>SetSpawnFlags</c>）。
//
// 权威来源：原版 Terraria.SceneMetrics（1.4.5.8 / Protocol 326）
//   - 扫描窗口 ZoneScanSize = 169 × 124 图格（= 1920/16 + 25×2 − 1 与 1200/16 + 25×2 − 1，以玩家中心图格居中）
//   - 阈值：SceneMetrics 静态构造（CorruptionTileThreshold 等）
//   - 图格计数表与相互抵消规则：SceneMetrics.AggregateTileCounts
//   - 深度带：SceneMetrics.CalculateZones
//
// 与原版的差异（刻意保留，均在此注明）：
//   - 只实现刷怪用得到的标志；营火 / 水蜡烛 / 和平蜡烛 / 旗帜 / 矿石探测等纯表现与增益统计不实现；
//   - 世界种子变体（remixWorld / drunkWorld / dualDungeons / infectedSeed / Skyblock / 十周年）不建模，
//     其中的 infectedSeed 会把向日葵计数权重从 −10 变 −30，这里固定取 −10；
//   - <c>OceanSandTileCount</c> 用于把海洋沙从沙漠计数里剔除，这里按原版口径实现。

using System;

namespace TerraAuth.Simulation;

/// <summary>深度带（原版 <c>SceneMetrics</c> 的 Zone*Height 系列，互斥）。</summary>
public enum DepthBand
{
    /// <summary>天空层：<c>y ≤ worldSurface × 0.35</c>。</summary>
    Sky = 0,

    /// <summary>地表层：<c>worldSurface × 0.35 &lt; y ≤ worldSurface</c>。</summary>
    Overworld = 1,

    /// <summary>泥土层：<c>worldSurface &lt; y ≤ rockLayer</c>。</summary>
    Dirt = 2,

    /// <summary>岩层：<c>rockLayer &lt; y ≤ underworldLayer</c>。</summary>
    Rock = 3,

    /// <summary>地狱层：<c>y &gt; underworldLayer</c>。</summary>
    Underworld = 4,
}

/// <summary>玩家中心扫描得到的生物群系 / 深度标志（对应原版 <c>Player.Zone*</c>）。</summary>
public readonly record struct SceneZones(
    bool Corrupt,
    bool Crimson,
    bool Hallow,
    bool Jungle,
    bool Snow,
    bool Desert,
    bool Glowshroom,
    bool Meteor,
    bool Dungeon,
    bool Graveyard,
    bool UndergroundDesert,
    bool BehindBackwall,
    DepthBand Depth)
{
    /// <summary>玩家是否位于地表以下（原版 <c>SceneMetrics.BelowSurface</c>）。</summary>
    public bool BelowSurface => Depth >= DepthBand.Dirt;
}

/// <summary>
/// 可复用的场景度量扫描器。图格计数缓冲在实例内复用，避免每次扫描分配。
/// 非线程安全：由仿真线程独占使用。
/// </summary>
public sealed class BiomeScanner
{
    /// <summary>原版 <c>SceneMetrics.ZoneScanSize.X</c>。</summary>
    public const int ScanWidth = 169;
    /// <summary>原版 <c>SceneMetrics.ZoneScanSize.Y</c>。</summary>
    public const int ScanHeight = 124;

    // ---- 原版 SceneMetrics 静态构造的阈值 ----
    public const int CorruptionTileThreshold = 300;
    public const int CrimsonTileThreshold = 300;
    public const int HallowTileThreshold = 125;
    public const int JungleTileThreshold = 140;
    public const int SnowTileNormalThreshold = 1500;
    public const int MushroomTileThreshold = 100;
    public const int MeteorTileThreshold = 75;
    public const int DungeonTileThreshold = 250;
    public const int GraveyardTileThreshold = 28;
    public const int DesertTileNormalThreshold = 1500;

    /// <summary>地狱层高度（图格）：与原版 <c>Main.UnderworldLayer</c> 同为「世界底部 200 格」。</summary>
    public const int UnderworldHeight = 200;

    /// <summary>计数表覆盖的最大图格 ID（本文件用到的最大 ID 为 662）。</summary>
    private const int MaxCountedTileId = 700;

    /// <summary>原版 <c>WorldGen.beachDistance</c>：海滩 / 海洋判定用的横向边界。</summary>
    public const int BeachDistance = 380;

    private readonly int[] _counts = new int[MaxCountedTileId + 1];

    /// <summary>地狱层起始图格 Y（原版 <c>Main.UnderworldLayer</c>）。</summary>
    public static int UnderworldLayerOf(WorldState world) => world.MaxTilesY - UnderworldHeight;

    /// <summary>海平面图格 Y（原版 <c>WorldGen.oceanLevel</c> = (地表 + 岩层) / 2 + 40）。</summary>
    public static double OceanLevelOf(WorldState world) => (world.WorldSurface + world.RockLayer) / 2.0 + 40.0;

    /// <summary>原版 <c>WorldGen.oceanDepths</c>：是否位于海面之下的世界两端。</summary>
    public static bool IsOceanDepth(WorldState world, int tileX, int tileY)
        => tileY <= OceanLevelOf(world)
           && (tileX < BeachDistance || tileX > world.MaxTilesX - BeachDistance);

    /// <summary>原版 <c>TileID.Sets.isDesertBiomeSand</c>。</summary>
    public static bool IsDesertBiomeSand(ushort type)
        => type is 53 or 397 or 396 or 400 or 403 or 401;

    /// <summary>原版 <c>Main.tileSand</c>（刷怪旗标 <c>isOcean</c> 用到的一部分沙类图格）。</summary>
    public static bool IsSand(ushort type)
        => type is 53 or 112 or 116 or 234 or 397 or 398 or 399 or 400 or 401 or 402 or 403 or 396;

    /// <summary>原版 <c>Main.wallDungeon</c>（地牢墙，用于 <c>ZoneDungeon</c>）。</summary>
    public static bool IsDungeonWall(ushort wall)
        => wall is 7 or 8 or 9 or 94 or 95 or 96 or 97 or 98 or 99;

    /// <summary>原版 <c>WallID.Sets.AllowsUndergroundDesertEnemiesToSpawn</c>（地下沙漠墙）。</summary>
    public static bool AllowsUndergroundDesertEnemies(ushort wall)
        => wall is 187 or 220 or 222 or 221 or 216 or 217 or 219 or 218 or 223;

    /// <summary>
    /// 以 <paramref name="centerTileX"/> / <paramref name="centerTileY"/> 为中心扫描并聚合生物群系标志。
    /// 对应原版 <c>SceneMetrics.ScanTiles</c> + <c>AggregateTileCounts</c> + <c>CalculateZones</c>。
    /// </summary>
    public SceneZones Scan(WorldState world, int centerTileX, int centerTileY)
    {
        Array.Clear(_counts);

        int halfW = ScanWidth / 2;   // 84
        int halfH = ScanHeight / 2;  // 62
        int x0 = Math.Max(0, centerTileX - halfW);
        int x1 = Math.Min(world.MaxTilesX - 1, centerTileX + halfW);
        int y0 = Math.Max(0, centerTileY - halfH);
        int y1 = Math.Min(world.MaxTilesY - 1, centerTileY + halfH);

        int oceanSand = 0;
        if (x1 >= x0 && y1 >= y0)
        {
            // 区块分区读锁：仿真线程的图格写入 / 包 10 编码并发时保持一致性。
            using (world.Sections.EnterRead(x0, y0, x1, y1))
            {
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        ref var tile = ref world.Tiles[x, y];
                        if (!tile.Active) continue;

                        if (tile.Type <= MaxCountedTileId) _counts[tile.Type]++;
                        if (IsDesertBiomeSand(tile.Type) && IsOceanDepth(world, x, y)) oceanSand++;
                    }
                }
            }
        }

        // ---- 原版 AggregateTileCounts：按图格 ID 聚合 ----
        const int sunflowerWeight = -10; // 原版 infectedSeed 时为 -30
        int holy = C(109) + C(492) + C(110) + C(113) + C(117) + C(116) + C(164) + C(403) + C(402);
        int snow = C(147) + C(148) + C(161) + C(162) + C(164) + C(163) + C(200);
        int jungle = C(60) + C(61) + C(62) + C(74) + C(226) + C(225);
        int evil = C(23) + C(661) + C(24) + C(25) + C(32) + C(112) + C(163) + C(400) + C(398)
                   + C(27) * sunflowerWeight;
        int blood = C(199) + C(662) + C(201) + C(203) + C(200) + C(401) + C(399) + C(234) + C(352)
                    + C(27) * sunflowerWeight;
        int mushroom = C(70) + C(71) + C(72) + C(528);
        int meteor = C(37);
        int dungeon = C(41) + C(43) + C(44) + C(481) + C(482) + C(483);
        int sand = C(53) + C(112) + C(116) + C(234) + C(397) + C(398) + C(402) + C(399)
                   + C(396) + C(400) + C(403) + C(401);
        int graveyard = C(85) - C(27) / 2;

        if (graveyard < 0) graveyard = 0;
        if (holy < 0) holy = 0;
        if (evil < 0) evil = 0;
        if (blood < 0) blood = 0;

        // 神圣 / 腐化 / 猩红三者互相抵消（原版同一段逻辑，避免神圣与邪恶图格重叠计数）
        int holyRaw = holy;
        holy -= evil;
        holy -= blood;
        evil -= holyRaw;
        blood -= holyRaw;
        if (holy < 0) holy = 0;
        if (evil < 0) evil = 0;
        if (blood < 0) blood = 0;

        int desertSand = Math.Max(0, sand - oceanSand);

        // ---- CalculateZones ----
        int underworldLayer = UnderworldLayerOf(world);
        DepthBand depth =
            centerTileY > underworldLayer ? DepthBand.Underworld
            : centerTileY > world.RockLayer ? DepthBand.Rock
            : centerTileY > world.WorldSurface ? DepthBand.Dirt
            : centerTileY > world.WorldSurface * 0.35 ? DepthBand.Overworld
            : DepthBand.Sky;

        ref var center = ref world.Tiles[
            Math.Clamp(centerTileX, 0, world.MaxTilesX - 1),
            Math.Clamp(centerTileY, 0, world.MaxTilesY - 1)];
        ushort centerWall = center.Wall;

        bool zoneDungeon = dungeon >= DungeonTileThreshold
                           && centerTileY > world.WorldSurface
                           && IsDungeonWall(centerWall);

        bool zoneDesert = desertSand >= DesertTileNormalThreshold;
        bool belowSurface = centerTileY > world.WorldSurface;

        return new SceneZones(
            Corrupt: evil >= CorruptionTileThreshold,
            Crimson: blood >= CrimsonTileThreshold,
            Hallow: holy >= HallowTileThreshold,
            Jungle: jungle >= JungleTileThreshold && depth != DepthBand.Underworld,
            Snow: snow >= SnowTileNormalThreshold,
            Desert: zoneDesert,
            Glowshroom: mushroom >= MushroomTileThreshold,
            Meteor: meteor >= MeteorTileThreshold,
            Dungeon: zoneDungeon,
            Graveyard: graveyard >= GraveyardTileThreshold,
            UndergroundDesert: zoneDesert && belowSurface && AllowsUndergroundDesertEnemies(centerWall),
            BehindBackwall: centerWall > 0,
            Depth: depth);

        int C(int tileId) => _counts[tileId];
    }
}
