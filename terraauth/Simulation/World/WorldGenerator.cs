// TerraAuth — Phase 6: 程序化世界生成（分层地形）
// 目的：解除进服阻塞——默认 new WorldState() 是 1×1 世界，NetworkHost 的
//   maxSectionsX = MaxTilesX/200 = 0 → 区块列表为空 → 客户端收不到任何 TileSection（包 10）而掉线。
//
// 生成内容（层高比例 / 图格 ID / 摆放约定均按原版核对）：
//   1) 地表起伏（多倍频值噪声）→ 草皮 / 泥土 / 岩层 / 地狱层
//   2) 泥土层内的黏土与沙斑；地下背景墙
//   3) 洞穴（脊状噪声挖隧道 + 深层空腔）
//   4) 矿脉（按深度分带：铜/铁 → 铁/银 → 银/金，随机游走成脉）
//   5) 两端海滩 + 海水（海平面按原版「(地表+岩层)/2 + 40」）
//   6) 地狱层（灰烬 + 狱石）与岩浆
//   7) 地下宝箱（2×2 摆放 + 原版帧约定 + 战利品）
//
// 说明：这是「原版风格的分层生成」，**不是原版地形生成器的逐段移植**；
//   不含生物群系（雪原 / 沙漠 / 丛林 / 腐化）、树木、生命水晶、地牢 / 神庙等结构体。

using System;

namespace TerraAuth.Simulation;

/// <summary>世界尺寸档（与原版三档一致）。</summary>
public enum WorldSize
{
    Small,
    Medium,
    Large,
}

/// <summary>确定性程序化世界生成。</summary>
public static class WorldGenerator
{
    /// <summary>原版小世界尺寸（由其宽高像素 / 16 + 1 得出区块数）。</summary>
    public const int SmallWorldWidth = 4200;
    public const int SmallWorldHeight = 1200;

    /// <summary>原版世界生成器版本（1.4.5.8）。</summary>
    public const ulong GeneratorVersion = 1400159338497UL;

    /// <summary>尺寸档 → 图格宽高（与原版小 / 中 / 大世界一致）。</summary>
    public static (int Width, int Height) Dimensions(WorldSize size) => size switch
    {
        WorldSize.Medium => (6400, 1800),
        WorldSize.Large => (8400, 2400),
        _ => (SmallWorldWidth, SmallWorldHeight),
    };

    // ---- 图格 ID（与原版图格 ID 一致）----
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort Iron = 6;
    private const ushort Copper = 7;
    private const ushort Gold = 8;
    private const ushort Silver = 9;
    private const ushort ChestTile = 21;
    private const ushort Heart = 12;             // TileID.Heart（生命水晶）
    private const ushort ClayBlock = 40;
    private const ushort Sand = 53;
    private const ushort Ash = 57;
    private const ushort Hellstone = 58;

    // ---- 生物群系图格 ID（原版 TileID，逐项核对）----
    private const ushort CorruptGrass = 23;      // TileID.CorruptGrass
    private const ushort Ebonstone = 25;         // TileID.Ebonstone
    private const ushort CrimsonGrass = 199;     // TileID.CrimsonGrass
    private const ushort Crimstone = 203;        // TileID.Crimstone
    private const ushort Mud = 59;               // TileID.Mud
    private const ushort JungleGrass = 60;       // TileID.JungleGrass
    private const ushort MushroomGrass = 70;     // TileID.MushroomGrass
    private const ushort MushroomPlants = 71;    // TileID.MushroomPlants
    private const ushort HallowedGrass = 109;    // TileID.HallowedGrass
    private const ushort Ebonsand = 112;         // TileID.Ebonsand
    private const ushort Pearlsand = 116;        // TileID.Pearlsand
    private const ushort Pearlstone = 117;       // TileID.Pearlstone
    private const ushort SnowBlock = 147;        // TileID.SnowBlock
    private const ushort IceBlock = 161;         // TileID.IceBlock
    private const ushort CorruptIce = 163;       // TileID.CorruptIce
    private const ushort HallowedIce = 164;      // TileID.HallowedIce
    private const ushort FleshIce = 200;         // TileID.FleshIce
    private const ushort Crimsand = 234;         // TileID.Crimsand
    private const ushort Meteorite = 37;         // TileID.Meteorite
    private const ushort BlueDungeonBrick = 41;  // TileID.BlueDungeonBrick
    private const ushort Sandstone = 396;        // TileID.Sandstone
    private const ushort HardenedSand = 397;     // TileID.HardenedSand
    private const ushort CorruptHardenedSand = 398;  // TileID.CorruptHardenedSand
    private const ushort CrimsonHardenedSand = 399;  // TileID.CrimsonHardenedSand
    private const ushort CorruptSandstone = 400;     // TileID.CorruptSandstone
    private const ushort CrimsonSandstone = 401;     // TileID.CrimsonSandstone
    private const ushort HallowHardenedSand = 402;   // TileID.HallowHardenedSand
    private const ushort HallowSandstone = 403;      // TileID.HallowSandstone

    // ---- 墙壁 ID ----
    private const ushort StoneWall = 1;
    private const ushort DirtWall = 2;
    private const ushort BlueDungeonUnsafeWall = 7;   // WallID.BlueDungeonUnsafe（Main.wallDungeon 成员）
    private const ushort CorruptionUnsafeWall = 188;  // WallID.CorruptionUnsafe1（原版 initializeHardMode 用的那一档）
    private const ushort CrimsonUnsafeWall = 192;     // WallID.CrimsonUnsafe1
    private const ushort HallowUnsafeWall = 200;      // WallID.HallowUnsafe1
    private const ushort SandstoneWall = 187;         // WallID.Sandstone（WallID.Sets.Conversion.Sandstone 成员）
    private const ushort HardenedSandWall = 216;      // WallID.HardenedSand（同上）

    // ---- 物品 ID（与原版物品 ID 一致；仅用于宝箱战利品）----
    private const short ItemTorch = 8;
    private const short ItemWood = 9;
    private const short ItemLesserHealingPotion = 28;
    private const short ItemShuriken = 42;
    private const short ItemCopperCoin = 71;
    private const short ItemSilverCoin = 72;
    private const short ItemGoldCoin = 73;

    /// <summary>向导 NPC 的类型 ID（原版常量 22）。</summary>
    private const short GuideNpcType = 22;

    // ---- 层高比例（原版地表 / 岩层公式）----
    private const double SurfaceBaseRatio = 0.30;   // 地表基准 = 世界高度 × 0.30
    private const double SurfaceJitter = 0.10;      // 再乘 0.90~1.10
    private const double RockLayerExtraRatio = 0.20; // 岩层 = (地表 + 高度 × 0.20) × 0.90~1.10
    private const double SurfaceMinRatio = 0.17;    // 地表允许上限 / 下限（× 世界高度）
    private const double SurfaceMinRatioSmallBonus = 0.02;
    private const double SurfaceMaxRatio = 0.26;
    private const double SurfaceAmplitudeRatio = 0.05; // 起伏振幅（× 世界高度）

    private const int UnderworldHeight = 200;       // 地狱层高度（底部图格数）
    private const int SpawnFlatRadius = 10;         // 出生点压平半宽
    private const int SpawnSafeRadius = 16;         // 出生点保护半径（不挖洞 / 不放宝箱）

    /// <summary>生成与原版小世界同尺寸的确定性世界（等价于 <c>Generate(WorldSize.Small, ...)</c>）。</summary>
    public static WorldState GenerateSmall(string worldName = "TerraAuth", int seed = 20260909)
        => Generate(WorldSize.Small, worldName, seed);

    /// <summary>按尺寸档生成确定性世界。</summary>
    public static WorldState Generate(WorldSize size, string worldName = "TerraAuth", int seed = 20260909)
    {
        var (maxTilesX, maxTilesY) = Dimensions(size);
        var rng = new XoshiroRng((ulong)seed);

        var world = new WorldState
        {
            MaxTilesX = maxTilesX,
            MaxTilesY = maxTilesY,
            Tiles = new TileMap(maxTilesX, maxTilesY),
            WorldName = worldName,
            WorldId = 1,
            UniqueId = CreateDeterministicGuid(seed),
            Seed = seed.ToString(),
            WorldGeneratorVersion = GeneratorVersion,
            GameMode = 0,
            DayTime = true,
            Time = 27000.0, // 正午
        };

        // ---- 1. 层高（原版公式）----
        double surfaceBase = maxTilesY * SurfaceBaseRatio * (1.0 - SurfaceJitter + rng.NextDouble() * SurfaceJitter * 2);
        double rockBase = (surfaceBase + maxTilesY * RockLayerExtraRatio)
                          * (1.0 - SurfaceJitter + rng.NextDouble() * SurfaceJitter * 2);
        double surfaceMin = maxTilesY * (SurfaceMinRatio + (size == WorldSize.Small ? SurfaceMinRatioSmallBonus : 0.0));
        double surfaceMax = maxTilesY * SurfaceMaxRatio;
        double surfaceAmplitude = maxTilesY * SurfaceAmplitudeRatio;

        // ---- 2. 逐列地表行：多倍频值噪声 ----
        var groundY = new int[maxTilesX];
        int groundMax = 0;
        for (int x = 0; x < maxTilesX; x++)
        {
            double n = Fbm(x, 0, octaves: 4, freq: 0.0055, seed);      // [0,1]
            double h = surfaceBase + (n - 0.5) * 2.0 * surfaceAmplitude;
            h = Math.Clamp(h, surfaceMin, surfaceMax);
            int y = (int)Math.Round(h);
            groundY[x] = y;
            if (y > groundMax) groundMax = y;
        }

        // 世界层位：地表取最高列 + 25，岩层对齐到 6 的倍数（与原版一致）
        int worldSurface = groundMax + 25;
        int rockLayerRaw = (int)rockBase;
        int rockLayer = worldSurface + ((rockLayerRaw - worldSurface) / 6) * 6;
        if (rockLayer <= worldSurface + 30) rockLayer = worldSurface + 30;
        int hellStart = Math.Max(worldSurface + 1, maxTilesY - UnderworldHeight);

        // 海滩 / 海平面（原版：海平面 = (地表 + 岩层) / 2 + 40）
        int oceanWidth = Math.Max(60, maxTilesX / 40);
        int oceanLevel = (int)((worldSurface + rockLayer) / 2.0 + 40);

        // 出生点：世界中央，压平一段地表
        int spawnX = maxTilesX / 2;
        int spawnGroundY = groundY[spawnX];
        for (int x = spawnX - SpawnFlatRadius; x <= spawnX + SpawnFlatRadius; x++)
            groundY[x] = spawnGroundY;

        // ---- 3. 填充地层 ----
        for (int x = 0; x < maxTilesX; x++)
        {
            bool beach = x < oceanWidth || x >= maxTilesX - oceanWidth;
            int gy = groundY[x];
            if (beach)
            {
                // 海滩：越靠外越深，形成缓坡；水面之上留空，水下灌水
                int distToEdge = Math.Min(x, maxTilesX - 1 - x);
                gy = oceanLevel + Math.Max(1, (oceanWidth - distToEdge) / 2);
                if (gy >= maxTilesY - 1) gy = maxTilesY - 2;
                groundY[x] = gy;
            }

            for (int y = gy; y < maxTilesY; y++)
            {
                ref var tile = ref world.Tiles[x, y];
                tile.Active = true;

                if (y >= hellStart)
                {
                    // 地狱层：灰烬为主，夹狱石
                    tile.Type = Hash01(x, y, seed + 5) < 0.07 ? Hellstone : Ash;
                    continue;
                }

                if (y >= rockLayer)
                {
                    tile.Type = Stone;
                    tile.Wall = StoneWall;
                    continue;
                }

                if (y == gy)
                {
                    tile.Type = beach ? Sand : Grass;
                    continue;
                }

                // 泥土层：局部黏土 / 沙斑
                double p = ValueNoise2D(x * 0.05, y * 0.05, seed + 11);
                tile.Type = p < 0.12 ? ClayBlock : (beach ? Sand : Dirt);
                tile.Wall = DirtWall;
            }

            // 海水：海平面到沙面之间灌满水
            if (beach)
            {
                for (int y = oceanLevel; y < gy; y++)
                {
                    ref var water = ref world.Tiles[x, y];
                    water.Active = false;
                    water.Wall = 0;
                    water.Liquid = 255;
                    water.LiquidType = 0;
                }
            }
        }

        // ---- 4. 洞穴 ----
        for (int x = 0; x < maxTilesX; x++)
        {
            // 出生点保护：附近不挖洞（避免出生即坠落 / 无法站立）
            if (Math.Abs(x - spawnX) <= SpawnSafeRadius) continue;
            if (x < oceanWidth || x >= maxTilesX - oceanWidth) continue; // 海滩下不挖

            int gy = groundY[x];
            for (int y = gy + 4; y < hellStart; y++)
            {
                // 脊状噪声 → 隧道（单倍频即可，逐格成本敏感）；深层再叠加空腔
                double ridge = Math.Abs(ValueNoise2D(x * 0.032, y * 0.032, seed + 21) - 0.5);
                bool tunnel = ridge < 0.035;
                bool cavern = y > rockLayer + 80
                              && Fbm(x, y, octaves: 2, freq: 0.05, seed: seed + 31) > 0.72;
                if (!tunnel && !cavern) continue;

                ref var tile = ref world.Tiles[x, y];
                if (!tile.Active) continue;
                tile.Active = false;
                if (y >= hellStart - 30)
                {
                    // 靠近地狱：洞穴底部积岩浆
                    tile.Liquid = 255;
                    tile.LiquidType = 1;
                }
            }
        }

        // ---- 5. 矿脉 ----
        PlaceOreVeins(world, rng, worldSurface, rockLayer, hellStart);

        // ---- 5b. 生物群系（腐化/猩红、雪原、沙漠、丛林、发光蘑菇、地牢）----
        PlaceBiomes(world, rng, seed, groundY, rockLayer, hellStart, spawnX, oceanWidth);

        // ---- 5c. 地表树木（原版 WorldGen.GrowTree 的树干部分）----
        PlaceTrees(world, rng, spawnX);

        // ---- 6. 地表宝箱（地下洞穴内）----
        PlaceChests(world, rng, rockLayer, hellStart);

        // ---- 6b. 地下生命水晶（原版 AddLifeCrystal：2×2 摆放）----
        PlaceLifeCrystals(world, rng, worldSurface, rockLayer, hellStart);

        world.SpawnTileX = spawnX;
        world.SpawnTileY = spawnGroundY;
        world.WorldSurface = worldSurface;
        world.RockLayer = rockLayer;

        // ---- 7. 出生点旁放置一名城镇 NPC（向导），使 NPC 同步（包 23）具备可观测对象 ----
        int guideTileX = spawnX + 2;
        var (guideW, guideH) = NpcSizes.Of(GuideNpcType);
        world.Npcs.Add(new WorldNpc
        {
            Type = GuideNpcType,
            NetId = GuideNpcType,
            AiStyle = 7,            // 原版 aiStyle 7（TownEntities）
            GivenName = "Guide",
            // 原版约定：X/Y = 碰撞盒左上角，脚底 = Y + height（向导 18×40）→ 脚底正好贴地表上沿
            X = (guideTileX + 0.5f) * 16f - guideW / 2f,
            Y = spawnGroundY * 16f - guideH,
            IsTownNpc = true,
            HomeTileX = guideTileX,
            HomeTileY = spawnGroundY,
            Life = 250,
            LifeMax = 250,
        });

        return world;
    }

    // ========================================================================
    // 地表树木（原版 WorldGen.GrowTree 的树干部分）
    // ========================================================================
    //
    // 口径说明：原版的「树」在图格数据里**只有树干**（TileID.Trees = 5）。
    // 树冠（枝叶）不是图格，而是客户端按树干顶帧 + 世界的 `WorldGen.TreeTops` 元数据绘制的
    // （见 WorldGen.TreeGrowFXCheck / GetTreeLeaf）—— 该元数据不在我们的世界 / 协议模型里，
    // 所以这里保持与原版图格完全一致的口径：只写树干帧，树冠交给客户端渲染。
    //
    // 已知差异：<see cref="TileIdSets"/> 的实心集合未收录 5（原版 <c>Main.tileSolid[5] = true</c>），
    // 因此服务端视树干为**非实心**（玩家 / NPC 可穿过）。这是刻意保留的：把 5 加进实心集合会一并改变
    // 液体流动阻塞、掉落物落地与刷怪落点搜索，影响面大于收益。客户端仍会按自己的碰撞把玩家挡住。

    /// <summary>原版 <c>TileID.Trees</c>。</summary>
    private const ushort Trees = 5;

    /// <summary>原版 <c>WorldGen.IsTileTypeFitForTree</c> 中我们世界会出现的可长树地表。</summary>
    private static bool IsTreeGround(ushort type) => type is
        Grass or CorruptGrass or CrimsonGrass or JungleGrass or MushroomGrass or HallowedGrass or SnowBlock;

    /// <summary>
    /// 原版 <c>WorldGen.GrowTree</c> 的树干帧表：
    /// pattern 0..9 是 10 种树干纹理（5/6/7 会在两侧伸出枝条），variant 0..2 是同一图案的三种抖动。
    /// 帧值逐条对照原版 <c>GrowTreeWithSettings</c> 的帧赋值表。
    /// </summary>
    private static (short X, short Y) TreeTrunkFrame(int pattern, int variant) => pattern switch
    {
        1 => variant switch { 0 => ((short)0, (short)66), 1 => ((short)0, (short)88), _ => ((short)0, (short)110) },
        2 => variant switch { 0 => ((short)22, (short)0), 1 => ((short)22, (short)22), _ => ((short)22, (short)44) },
        3 => variant switch { 0 => ((short)44, (short)66), 1 => ((short)44, (short)88), _ => ((short)44, (short)110) },
        4 => variant switch { 0 => ((short)22, (short)66), 1 => ((short)22, (short)88), _ => ((short)22, (short)110) },
        5 => variant switch { 0 => ((short)88, (short)0), 1 => ((short)88, (short)22), _ => ((short)88, (short)44) },
        6 => variant switch { 0 => ((short)66, (short)66), 1 => ((short)66, (short)88), _ => ((short)66, (short)110) },
        7 => variant switch { 0 => ((short)110, (short)66), 1 => ((short)110, (short)88), _ => ((short)110, (short)110) },
        _ => variant switch { 0 => ((short)0, (short)0), 1 => ((short)0, (short)22), _ => ((short)0, (short)44) },
    };

    /// <summary>
    /// 逐列尝试长树（原版在「Placing trees」阶段随机采样落点）。森林密度约每 4 列一株；
    /// 出生点保护半径内的列跳过（与出生点压平一致，避免树挡在出生点）。
    /// </summary>
    private static void PlaceTrees(WorldState world, XoshiroRng rng, int spawnX)
    {
        for (int x = 8; x < world.MaxTilesX - 8; x++)
        {
            if (Math.Abs(x - spawnX) <= SpawnFlatRadius + 2) continue;
            if (rng.NextInt32(4) != 0) continue;

            TryGrowTree(world, rng, x);
        }
    }

    /// <summary>原版 <c>WorldGen.GrowTree</c>：条件校验 + 写入树干与枝条图格。</summary>
    private static bool TryGrowTree(WorldState world, XoshiroRng rng, int x)
    {
        // 该列地表 = 从上往下第一个实心格
        int baseY = -1;
        for (int y = 1; y < world.MaxTilesY - 40; y++)
        {
            if (world.Tiles[x, y].Active)
            {
                baseY = y;
                break;
            }
        }
        if (baseY < 2) return false;

        ref var ground = ref world.Tiles[x, baseY];
        if (!IsTreeGround(ground.Type)) return false;

        // 原版：底座上方三格不能有液体
        for (int d = 1; d <= 3; d++)
            if (world.Tiles[x, baseY - d].Liquid != 0) return false;

        // 原版：左右至少一侧也必须是可长树地表（保证地势平缓，树不会插在悬崖边）
        bool leftFit = world.Tiles[x - 1, baseY].Active && IsTreeGround(world.Tiles[x - 1, baseY].Type);
        bool rightFit = world.Tiles[x + 1, baseY].Active && IsTreeGround(world.Tiles[x + 1, baseY].Type);
        if (!leftFit && !rightFit) return false;

        // 原版 GrowTree：树干 5-16 格；丛林地表额外 +5；上方再留 4 格树冠锚点
        int trunkHeight = rng.NextInt32(12) + 5;
        int totalHeight = trunkHeight + 4 + (ground.Type == JungleGrass ? 5 : 0);
        int topY = baseY - totalHeight;
        if (topY < 1) return false;

        // 原版 EmptyTileCheck(x-2, x+2, topY, baseY-1)：上方 5 格宽 × totalHeight 格高必须为空
        for (int tx = x - 2; tx <= x + 2; tx++)
            for (int ty = topY; ty <= baseY - 1; ty++)
                if (world.Tiles[tx, ty].Active || world.Tiles[tx, ty].Liquid != 0) return false;

        // ---- 树干（原版 31490-31744）----
        bool leftBranchUsed = false;
        bool rightBranchUsed = false;

        for (int y = topY; y < baseY; y++)
        {
            ref var trunk = ref world.Tiles[x, y];
            trunk.Active = true;
            trunk.Type = Trees;

            int variant = rng.NextInt32(3);
            int pattern = rng.NextInt32(10);

            // 原版：最底一格与最顶一格固定用 pattern 0（避免在两端伸出枝条）
            if (y == baseY - 1 || y == topY) pattern = 0;

            // 原版：同一侧不允许连续两格出枝
            while (((pattern is 5 or 7) && leftBranchUsed) || ((pattern is 6 or 7) && rightBranchUsed))
                pattern = rng.NextInt32(10);

            leftBranchUsed = pattern is 5 or 7;
            rightBranchUsed = pattern is 6 or 7;

            (trunk.FrameX, trunk.FrameY) = TreeTrunkFrame(pattern, variant);

            if (pattern is 5 or 7)
            {
                ref var branch = ref world.Tiles[x - 1, y];
                branch.Active = true;
                branch.Type = Trees;
                int v = rng.NextInt32(3);
                if (rng.NextInt32(3) < 2) { branch.FrameX = 44; branch.FrameY = (short)(198 + v * 22); }
                else { branch.FrameX = 66; branch.FrameY = (short)(v * 22); }
            }

            if (pattern is 6 or 7)
            {
                ref var branch = ref world.Tiles[x + 1, y];
                branch.Active = true;
                branch.Type = Trees;
                int v = rng.NextInt32(3);
                if (rng.NextInt32(3) < 2) { branch.FrameX = 66; branch.FrameY = (short)(198 + v * 22); }
                else { branch.FrameX = 88; branch.FrameY = (short)(66 + v * 22); }
            }
        }

        // ---- 底部两侧（原版 31745-31861）----
        bool keepLeft = leftFit && rng.NextInt32(3) != 0;
        bool keepRight = rightFit && rng.NextInt32(3) != 0;

        if (keepLeft)
        {
            ref var tile = ref world.Tiles[x - 1, baseY - 1];
            tile.Active = true;
            tile.Type = Trees;
            tile.FrameX = 44;
            tile.FrameY = (short)(132 + rng.NextInt32(3) * 22);
        }

        if (keepRight)
        {
            ref var tile = ref world.Tiles[x + 1, baseY - 1];
            tile.Active = true;
            tile.Type = Trees;
            tile.FrameX = 22;
            tile.FrameY = (short)(132 + rng.NextInt32(3) * 22);
        }

        {
            ref var tile = ref world.Tiles[x, baseY - 1];
            tile.FrameX = (short)(keepLeft ? (keepRight ? 88 : 0) : 66);
            tile.FrameY = (short)(132 + rng.NextInt32(3) * 22);
        }

        // ---- 树顶锚点（原版 31862-31899：13 分之 1 概率用 frameX 0）----
        {
            ref var tile = ref world.Tiles[x, topY];
            tile.FrameX = (short)(rng.NextInt32(13) != 0 ? 22 : 0);
            tile.FrameY = (short)(198 + rng.NextInt32(3) * 22);
        }

        return true;
    }

    // ========================================================================
    // 矿脉
    // ========================================================================

    /// <summary>矿石带：按深度选矿，随机游走成脉（只替换泥土 / 黏土 / 沙 / 石）。</summary>
    private static void PlaceOreVeins(WorldState world, XoshiroRng rng, int worldSurface, int rockLayer, int hellStart)
    {
        int yMin = worldSurface + 25;
        int yMax = hellStart - 40;
        if (yMax <= yMin) return;

        int veinCount = Math.Max(8, world.MaxTilesX / 8);
        for (int v = 0; v < veinCount; v++)
        {
            int x = 8 + rng.NextInt32(world.MaxTilesX - 16);
            int y = yMin + rng.NextInt32(yMax - yMin);

            double depth = (y - worldSurface) / (double)Math.Max(1, hellStart - worldSurface);
            ushort ore = depth < 0.25
                ? (rng.NextInt32(2) == 0 ? Copper : Iron)
                : depth < 0.60
                    ? (rng.NextInt32(2) == 0 ? Iron : Silver)
                    : (rng.NextInt32(2) == 0 ? Silver : Gold);

            int size = 4 + rng.NextInt32(10);
            for (int s = 0; s < size; s++)
            {
                x += rng.NextInt32(3) - 1;
                y += rng.NextInt32(3) - 1;
                if (x < 1 || x >= world.MaxTilesX - 1 || y < 1 || y >= world.MaxTilesY - 1) break;

                ref var tile = ref world.Tiles[x, y];
                if (!tile.Active) continue;
                if (tile.Type is Dirt or ClayBlock or Sand or Stone)
                    tile.Type = ore;
            }
        }
    }

    // ========================================================================
    // 生命水晶（原版 WorldGen.AddLifeCrystal）
    // ========================================================================

    /// <summary>
    /// 在岩层里撒若干生命水晶（原版地下随处可见 <c>TileID.Heart</c>）。
    /// 数量按世界宽度取（小世界约 14 颗），摆放与判据完全走 <see cref="TryPlaceLifeCrystal"/>。
    /// </summary>
    private static void PlaceLifeCrystals(WorldState world, XoshiroRng rng, int worldSurface, int rockLayer, int hellStart)
    {
        int target = Math.Max(4, world.MaxTilesX / 300);
        int placed = 0;
        int attempts = 0;
        int maxAttempts = target * 60;

        while (placed < target && attempts++ < maxAttempts)
        {
            int x = 3 + rng.NextInt32(world.MaxTilesX - 6);
            int y = Math.Max(worldSurface + 10, rockLayer - 60) + rng.NextInt32(Math.Max(1, hellStart - rockLayer - 20));
            if (y >= world.MaxTilesY - 40) continue;

            if (!TryPlaceLifeCrystal(world, x, y)) continue;
            placed++;
        }
    }

    /// <summary>
    /// 生命水晶摆放（原版 <c>WorldGen.AddLifeCrystal</c>）：自 <paramref name="y"/> 向下找第一块实地，
    /// 在其上方摆 2×2 的生命水晶（图格 12），帧为 (0,0)/(18,0)/(0,18)/(18,18)。
    /// 判据：2×2 上方为空、**脚下两格都是实心**、无岩浆、非地牢墙。
    /// </summary>
    private static bool TryPlaceLifeCrystal(WorldState world, int x, int y)
    {
        int floor = y;
        for (; floor < world.MaxTilesY - 1; floor++)
        {
            ref var t = ref world.Tiles[x, floor];
            if (t.Active && TileIdSets.IsTileSolid(t.Type)) break;
        }
        if (floor >= world.MaxTilesY - 1) return false;

        int bottom = floor - 1;               // 水晶占 bottom-1 .. bottom 两行
        if (x - 1 < 1 || x >= world.MaxTilesX - 1 || bottom - 1 < 1) return false;

        for (int dx = -1; dx <= 0; dx++)
        {
            for (int dy = -1; dy <= 0; dy++)
            {
                ref var cell = ref world.Tiles[x + dx, bottom + dy];
                if (cell.Active) return false;                                  // 需要空位
                if (cell.Liquid > 0 && cell.LiquidType == 1) return false;      // 岩浆处不放
            }
        }

        if (BiomeScanner.IsDungeonWall(world.Tiles[x, bottom].Wall)) return false;

        // 脚下两格都必须是实心（原版要求 tile(i-1,num+1) 与 tile(i,num+1) 皆为实心）[i-1..i]
        for (int dx = -1; dx <= 0; dx++)
        {
            ref var t = ref world.Tiles[x + dx, bottom + 1];
            if (!t.Active || !TileIdSets.IsTileSolid(t.Type)) return false;
            if (t.Slope != 0 || t.HalfBrick) { t.Slope = 0; t.HalfBrick = false; }
        }

        SetLifeCrystal(world, x - 1, bottom - 1, 0, 0);
        SetLifeCrystal(world, x, bottom - 1, 18, 0);
        SetLifeCrystal(world, x - 1, bottom, 0, 18);
        SetLifeCrystal(world, x, bottom, 18, 18);
        return true;
    }

    private static void SetLifeCrystal(WorldState world, int x, int y, short frameX, short frameY)
    {
        ref var tile = ref world.Tiles[x, y];
        tile.Active = true;
        tile.Type = Heart;
        tile.FrameX = frameX;
        tile.FrameY = frameY;
    }

    // ========================================================================
    // 宝箱（2×2 摆放 + 原版帧约定）
    // ========================================================================

    /// <summary>
    /// 在地下洞穴内放置若干宝箱。摆放与 { 原版 } 一致：
    /// 占 2×2 格（左上 (x,y-1)、右上 (x+1,y-1)、左下 (x,y)、右下 (x+1,y)），
    /// 帧为 frameX ∈ {0,18} / frameY ∈ {0,18}，箱子实体登记在左上格；下方两格必须为实心。
    /// </summary>
    private static void PlaceChests(WorldState world, XoshiroRng rng, int rockLayer, int hellStart)
    {
        int target = Math.Max(3, world.MaxTilesX / 700);
        int placed = 0;
        int attempts = 0;
        int maxAttempts = target * 80;

        while (placed < target && attempts++ < maxAttempts)
        {
            int x = 4 + rng.NextInt32(world.MaxTilesX - 6);
            int span = Math.Max(1, hellStart - rockLayer - 6);
            int y = rockLayer + 2 + rng.NextInt32(span);

            if (!TryPlaceChest(world, x, y, rng)) continue;
            placed++;
        }
    }

    private static bool TryPlaceChest(WorldState world, int x, int y, XoshiroRng rng)
    {
        // 需要 2×2 空位，且下方两格实心
        for (int dx = 0; dx <= 1; dx++)
        {
            if (world.Tiles[x + dx, y - 1].Active) return false;
            if (world.Tiles[x + dx, y].Active) return false;
            if (!world.Tiles[x + dx, y + 1].Active) return false;
        }

        // 2×2 图格 + 原版帧（frameX ∈ {0,18}，frameY ∈ {0,18}）
        for (int dx = 0; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 0; dy++)
            {
                ref var tile = ref world.Tiles[x + dx, y + dy];
                tile.Active = true;
                tile.Type = ChestTile;
                tile.FrameX = (short)(dx * 18);
                tile.FrameY = (short)((dy + 1) * 18);
            }
        }

        var chest = new Chest
        {
            X = x,
            Y = y - 1,               // 实体登记在左上格
            Name = "",
            Items = new ChestItem[40],
        };
        FillChestLoot(chest, rng);
        lock (world.ChestsLock)
        {
            chest.Index = world.Chests.Count;
            world.Chests.Add(chest);
        }
        return true;
    }

    /// <summary>随机战利品：硬币 + 火把 / 木材 / 治疗药水 / 手里剑（物品 ID 与原版一致）。</summary>
    private static void FillChestLoot(Chest chest, XoshiroRng rng)
    {
        int slot = 0;
        void Put(short type, short stack)
        {
            if (slot >= chest.Items.Length) return;
            chest.Items[slot++] = new ChestItem { Type = type, Stack = stack, Prefix = 0 };
        }

        Put(ItemCopperCoin, (short)(50 + rng.NextInt32(200)));
        if (rng.NextInt32(2) == 0) Put(ItemSilverCoin, (short)(1 + rng.NextInt32(40)));
        if (rng.NextInt32(4) == 0) Put(ItemGoldCoin, (short)(1 + rng.NextInt32(8)));
        Put(ItemTorch, (short)(5 + rng.NextInt32(20)));
        if (rng.NextInt32(2) == 0) Put(ItemWood, (short)(20 + rng.NextInt32(60)));
        if (rng.NextInt32(3) == 0) Put(ItemLesserHealingPotion, (short)(2 + rng.NextInt32(5)));
        if (rng.NextInt32(3) == 0) Put(ItemShuriken, (short)(20 + rng.NextInt32(60)));
    }

    // ========================================================================
    // 生物群系
    // ========================================================================

    /// <summary>群系锚点距出生点的最小水平距离（图格）：出生点周围保持无群系，与出生保护 / 初始刷怪一致。</summary>
    private const int BiomeMinDistanceFromSpawn = 300;

    /// <summary>群系锚点距世界边界的水平距离下界（图格）：避开海滩与海洋（原版 <c>beachDistance</c> = 380）。</summary>
    private const int BiomeMinDistanceFromEdge = 420;

    /// <summary>是否是可以被群系「转换」的天然图格（泥土 / 草皮 / 黏土 / 石 / 沙）。</summary>
    private static bool IsNaturalTile(ushort type)
        => type is Dirt or Grass or ClayBlock or Stone or Sand;

    /// <summary>群系锚点 X：按世界宽度比例取位，并夹到「距边界 ≥ 420、距出生点 ≥ 300」的安全区间。</summary>
    private static int BiomeAnchor(int width, int percent, int spawnX, int dir)
    {
        int x = width * percent / 100;
        if (Math.Abs(x - spawnX) < BiomeMinDistanceFromSpawn)
            x = spawnX + dir * BiomeMinDistanceFromSpawn;
        return Math.Clamp(x, BiomeMinDistanceFromEdge, width - 1 - BiomeMinDistanceFromEdge);
    }

    /// <summary>群系种类：决定把天然图格映射成哪一套（图格 ID 逐项对照原版 <c>TileID</c>）。</summary>
    private enum BiomeKind
    {
        Corruption,
        Crimson,
        Hallow,
        Snow,
        Desert,
        Jungle,
    }

    /// <summary>
    /// 群系条带：以 <paramref name="centerX"/> 为起点、按 <paramref name="slant"/> 斜向下延伸的有机带。
    /// 对应原版「斜插式群系条带」的形态（世界生成的邪恶 / 丛林条带与困难模式的 <c>GERunner</c> 都是斜的、
    /// 且路径会左右蜿蜒），这里用「斜率 + 一倍频值噪声的横向游走」近似；边缘与下边界同样按噪声起伏。
    /// **只替换天然图格**（泥土 / 草 / 黏土 / 石 / 沙），不动洞穴、矿脉与已放置的结构。
    /// </summary>
    private static void PlaceBiomeBand(WorldState world, int seed, int[] groundY, int centerX, int halfWidth,
        int rockLayer, double slant, BiomeKind kind)
    {
        int top = groundY[Math.Clamp(centerX, 1, world.MaxTilesX - 2)];
        int bottom = Math.Min(world.MaxTilesY - 2, rockLayer + 40);

        for (int y = top - 20; y <= bottom; y++)
        {
            // 中心 = 起点 + 斜率位移 + 横向蜿蜒（原版 GERunner 每步都会随机偏移）
            int wander = (int)((ValueNoise2D(0, y * 0.018, seed + 91) - 0.5) * halfWidth * 1.6);
            int center = centerX + (int)(slant * (y - top)) + wander;
            int half = halfWidth + (int)((ValueNoise2D(y * 0.05, 2, seed + 71) - 0.5) * halfWidth * 0.7);
            int x0 = Math.Max(1, center - half);
            int x1 = Math.Min(world.MaxTilesX - 2, center + half);

            for (int x = x0; x <= x1; x++)
            {
                int colTop = groundY[x];
                if (y < colTop) continue;

                ref var tile = ref world.Tiles[x, y];
                if (!tile.Active || !IsNaturalTile(tile.Type)) continue;
                if (!MapBiomeTile(kind, tile.Type, y, colTop, out ushort mapped, out ushort wall)) continue;

                tile.Type = mapped;
                if (wall != 0) tile.Wall = wall;
            }
        }
    }

    /// <summary>
    /// 天然图格 → 群系图格映射（对照原版 <c>WorldGen.Convert</c> 的转换表）。
    /// 返回 false 表示该图格在本群系里保持不变（如雪原 / 丛林 / 沙漠不改岩层里的石头）。
    /// </summary>
    private static bool MapBiomeTile(BiomeKind kind, ushort src, int y, int colTop,
        out ushort mapped, out ushort wall)
    {
        wall = 0;
        mapped = src;

        switch (kind)
        {
            case BiomeKind.Corruption or BiomeKind.Crimson or BiomeKind.Hallow:
            {
                bool corrupt = kind == BiomeKind.Corruption;
                bool crimson = kind == BiomeKind.Crimson;

                if (src == Sand) mapped = corrupt ? Ebonsand : crimson ? Crimsand : Pearlsand;
                else if (src == HardenedSand) mapped = corrupt ? CorruptHardenedSand : crimson ? CrimsonHardenedSand : HallowHardenedSand;
                else if (src == Sandstone) mapped = corrupt ? CorruptSandstone : crimson ? CrimsonSandstone : HallowSandstone;
                else if (src == IceBlock) mapped = corrupt ? CorruptIce : crimson ? FleshIce : HallowedIce;
                else if (y == colTop) mapped = corrupt ? CorruptGrass : crimson ? CrimsonGrass : HallowedGrass;
                else mapped = corrupt ? Ebonstone : crimson ? Crimstone : Pearlstone;

                wall = corrupt ? CorruptionUnsafeWall : crimson ? CrimsonUnsafeWall : HallowUnsafeWall;
                return true;
            }

            case BiomeKind.Snow:
                if (src == Stone) return false;   // 原版雪原的岩层仍是岩石
                mapped = y <= colTop + 6 ? SnowBlock : IceBlock;
                return true;

            case BiomeKind.Desert:
                if (src == Stone) { mapped = Sandstone; wall = SandstoneWall; }
                else if (src == Sand) return false;
                else if (y == colTop) mapped = Sand;
                else { mapped = HardenedSand; wall = HardenedSandWall; }
                return true;

            case BiomeKind.Jungle:
                if (src == Stone) return false;   // 原版丛林的岩层仍是岩石
                mapped = y <= colTop + 3 ? JungleGrass : Mud;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 生物群系总入口。原版**生成阶段**应有的群系都在这里产出：
    /// 腐化 / 猩红（二选一，同原版）、雪原、沙漠（含地下沙漠的沙岩墙）、丛林、发光蘑菇、地牢。
    /// 神圣与陨石**不在**生成阶段（分别是困难模式转换与砸暗影珠坠落），见 <see cref="ApplyHardmode"/>。
    /// </summary>
    private static void PlaceBiomes(WorldState world, XoshiroRng rng, int seed, int[] groundY,
        int rockLayer, int hellStart, int spawnX, int oceanWidth)
    {
        int width = world.MaxTilesX;
        bool crimson = rng.NextInt32(2) == 0;

        // 锚点全部落在海滩内侧、且与出生点拉开距离；各条带横向互不重叠（斜向带按坡度留余量）
        int dungeonX = BiomeAnchor(width, 12, spawnX, +1);
        int evilX = BiomeAnchor(width, 20, spawnX, +1);
        int snowX = BiomeAnchor(width, 30, spawnX, +1);
        int mushroomX = BiomeAnchor(width, 41, spawnX, +1);
        int desertX = BiomeAnchor(width, 63, spawnX, -1);
        int jungleX = BiomeAnchor(width, 80, spawnX, -1);

        PlaceBiomeBand(world, seed, groundY, evilX, 55, rockLayer, slant: +0.35,
            crimson ? BiomeKind.Crimson : BiomeKind.Corruption);
        PlaceBiomeBand(world, seed, groundY, snowX, 85, rockLayer, slant: -0.25, BiomeKind.Snow);
        PlaceBiomeBand(world, seed, groundY, desertX, 75, rockLayer, slant: +0.30, BiomeKind.Desert);
        PlaceBiomeBand(world, seed, groundY, jungleX, 90, rockLayer, slant: -0.35, BiomeKind.Jungle);
        PlaceGlowingMushroom(world, seed, mushroomX, rockLayer, hellStart, oceanWidth);
        PlaceDungeon(world, dungeonX, rockLayer, oceanWidth);

        // 世界进度：本世界为猩红则置位（影响 Boss 掉落 魔矿/猩红矿 与包 7 的 Crimson 位）
        world.Progress.Crimson = crimson;
    }

    /// <summary>
    /// 困难模式地形转换（原版 <c>WorldGen.StartHardmode</c> → <c>initializeHardMode</c>）：
    /// <list type="number">
    ///   <item>按原版公式选两条带位置：<c>maxTilesX × 0.300~0.399</c> 与其镜像（并列随机换向），
    ///         再按**地牢所在侧**把其中一条挪到 <c>maxTilesX × 0.200~0.299</c> 一档；</item>
    ///   <item>一条转为**神圣**、一条**刷新为邪恶**（原版两次 <c>GERunner</c>：good / evil，方向相反）；</item>
    ///   <item>在地下洞窟里补神圣 / 腐化 / 猩红背景墙（原版同一段后半部分）。</item>
    /// </list>
    /// 说明：条带**形态**按 <c>GERunner</c> 的语义实现（斜向有机带），非逐行移植。
    /// 由服务端在「血肉墙被击杀 → <c>Progress.HardMode</c> 置位」时调用一次（与原版 <c>StartHardmode</c> 的守卫等价）。
    /// </summary>
    public static void ApplyHardmode(WorldState world, IRng rng)
    {
        int width = world.MaxTilesX;
        int rockLayer = (int)world.RockLayer;
        int[] groundY = SurfaceColumns(world);

        // ---- 1) 两条带位置（原版公式）----
        double near = (rng.NextInt32(100) + 300) * 0.001;   // 0.300 ~ 0.399
        double inner = (rng.NextInt32(100) + 200) * 0.001;  // 0.200 ~ 0.299
        int x1 = (int)(width * near);
        int x2 = (int)(width * (1.0 - near));
        int dir = 1;
        if (rng.NextInt32(2) == 0)
        {
            x2 = (int)(width * near);
            x1 = (int)(width * (1.0 - near));
            dir = -1;
        }

        if (world.DungeonX < width / 2)
        {
            if (x2 < x1) x2 = (int)(width * inner);
            else x1 = (int)(width * inner);
        }
        else
        {
            if (x2 > x1) x2 = (int)(width * (1.0 - inner));
            else x1 = (int)(width * (1.0 - inner));
        }

        // ---- 2) 神圣带 + 邪恶带 ----
        PlaceBiomeBand(world, world.WorldId * 31 + 7, groundY, x1, 45, rockLayer,
            slant: 0.30 * dir, BiomeKind.Hallow);
        PlaceBiomeBand(world, world.WorldId * 31 + 11, groundY, x2, 45, rockLayer,
            slant: -0.30 * dir, world.Progress.Crimson ? BiomeKind.Crimson : BiomeKind.Corruption);

        // ---- 3) 地下群系墙 ----
        PlaceBiomeWalls(world, rng, x1, x2);
    }

    /// <summary>每列「自顶向下第一个实心格」的 Y（运行期做地形转换时用；生成期直接用 groundY）。</summary>
    private static int[] SurfaceColumns(WorldState world)
    {
        var surface = new int[world.MaxTilesX];
        for (int x = 0; x < world.MaxTilesX; x++)
        {
            int y = 1;
            for (; y < world.MaxTilesY - 1; y++)
            {
                ref var tile = ref world.Tiles[x, y];
                if (tile.Active && TileIdSets.IsTileSolid(tile.Type)) break;
            }
            surface[x] = y;
        }
        return surface;
    }

    /// <summary>
    /// 地下群系墙（原版 <c>initializeHardMode</c> 后半段：<c>25 × (maxTilesX/4200)</c> 个点，
    /// 从地表的空腔做洪水填充，把群系墙铺到填充区域上）。
    /// 这里同样做「有界洪水填充 + 铺墙」，但墙的种类按该点离哪条带更近来决定（原版是按填充起点处的图格种类）。
    /// </summary>
    private static void PlaceBiomeWalls(WorldState world, IRng rng, int x1, int x2)
    {
        int spots = Math.Max(1, 25 * Math.Max(1, world.MaxTilesX / 4200));
        ushort evilWall = world.Progress.Crimson ? CrimsonUnsafeWall : CorruptionUnsafeWall;

        var queue = new Queue<(int X, int Y)>();
        for (int i = 0; i < spots; i++)
        {
            int x = Math.Clamp(rng.NextInt32(world.MaxTilesX), 20, world.MaxTilesX - 21);
            int minY = Math.Max(1, (int)world.WorldSurface - 100);
            int y = Math.Clamp(minY + rng.NextInt32(190), 1, world.MaxTilesY - 60);

            // 只处理带附近的空腔（原版的选点也要求该处是群系图格）
            if (Math.Abs(x - x1) >= 120 && Math.Abs(x - x2) >= 120) continue;
            if (world.Tiles[x, y].Active) continue;

            ushort wall = Math.Abs(x - x1) <= Math.Abs(x - x2) ? HallowUnsafeWall : evilWall;

            queue.Clear();
            queue.Enqueue((x, y));
            world.Tiles[x, y].Wall = wall;
            int filled = 0;

            while (queue.Count > 0 && filled < 1000)
            {
                var (cx, cy) = queue.Dequeue();
                filled++;
                TryEnqueue(cx - 1, cy);
                TryEnqueue(cx + 1, cy);
                TryEnqueue(cx, cy - 1);
                TryEnqueue(cx, cy + 1);
            }

            void TryEnqueue(int nx, int ny)
            {
                if (nx < 1 || ny < 1 || nx >= world.MaxTilesX - 1 || ny >= world.MaxTilesY - 1) return;
                ref var tile = ref world.Tiles[nx, ny];
                if (tile.Active || tile.Wall == wall) return;   // 只沿空腔走（已是该墙则视为已访问）
                tile.Wall = wall;
                queue.Enqueue((nx, ny));
            }
        }
    }

    // ========================================================================
    // 陨石（原版 WorldGen.meteor）
    // ========================================================================

    /// <summary>陨石落点禁止图格（原版 <c>WorldGen.meteor</c> 的拒绝清单，逐个照抄 ID）。</summary>
    private static bool BlocksMeteor(ushort type)
        => type is 26 or 226 or 470 or 475 or 488 or 597;

    /// <summary>
    /// 陨石坠落（原版 <c>WorldGen.meteor</c> 的语义）：在 (<paramref name="tileX"/>, <paramref name="tileY"/>) 附近
    /// 生成一处陨石坑 —— 多圈同心衰减：内圈填陨石(37) → 上半部掏空成坑 → 稀疏外圈把裸露地面换成陨石。
    /// 原版落点由「砸碎第 3 颗暗影珠 / 猩红之心」后的搜索决定（<c>Main.rand</c> 试 15 个候选点找平地），
    /// 我们的世界尚未生成暗影珠，故此处把落点与生成拆成服务端 API（触发链属独立专项）。
    /// 落点检查（玩家屏幕内 / NPC 附近 / 宝箱 / 地牢砖 / 禁落图格）按原版口径实现。
    /// </summary>
    public static bool TryPlaceMeteor(WorldState world, IRng rng, int tileX, int tileY, bool ignorePlayers = false)
    {
        if (tileX < 50 || tileX > world.MaxTilesX - 50) return false;
        if (tileY < 50 || tileY > world.MaxTilesY - 50) return false;

        const int scan = 35;

        // 玩家屏幕内不落（原版用 NPC.sWidth / safeRangeX 的矩形）
        if (!ignorePlayers)
        {
            foreach (var p in world.Players.Values)
            {
                if (!p.Active) continue;
                float px = p.AimPosition.X + NpcSizes.PlayerWidth / 2f - 1000f;
                float py = p.AimPosition.Y + NpcSizes.PlayerHeight / 2f - 650f;
                if ((tileX - scan) * 16f < px + 2000f && px < (tileX + scan) * 16f
                    && (tileY - scan) * 16f < py + 1300f && py < (tileY + scan) * 16f)
                {
                    return false;
                }
            }
        }

        // NPC 附近不落
        lock (world.NpcsLock)
        {
            foreach (var n in world.Npcs)
            {
                if (!n.Active) continue;
                var (nw, nh) = NpcSizes.Of(n.Type);
                if (n.X < (tileX + scan) * 16f && n.X + nw > (tileX - scan) * 16f
                    && n.Y < (tileY + scan) * 16f && n.Y + nh > (tileY - scan) * 16f)
                {
                    return false;
                }
            }
        }

        // 宝箱 / 地牢砖 / 禁落图格
        for (int x = tileX - scan; x < tileX + scan; x++)
        {
            for (int y = tileY - scan; y < tileY + scan; y++)
            {
                ref var tile = ref world.Tiles[x, y];
                if (!tile.Active) continue;
                if (TileIdSets.IsBasicChest(tile.Type) || TileIdSets.IsDungeonBrick(tile.Type)) return false;
                if (BlocksMeteor(tile.Type)) return false;
            }
        }

        // 1) 内圈填陨石（半径 17~22，圆心略下移；非实心格先清空再置为陨石）
        int radius = 17 + rng.NextInt32(6);
        for (int x = tileX - radius; x < tileX + radius; x++)
        {
            for (int y = tileY - radius; y < tileY + radius; y++)
            {
                if (y <= tileY + rng.NextInt32(5) - 2 - 5) continue;
                double dx = Math.Abs(tileX - x);
                double dy = Math.Abs(tileY - y);
                if (Math.Sqrt(dx * dx + dy * dy) < radius * 0.9 + rng.NextInt32(9) - 4)
                {
                    ref var tile = ref world.Tiles[x, y];
                    if (!TileIdSets.IsTileSolid(tile.Type)) tile.Active = false;
                    tile.Slope = 0;
                    tile.HalfBrick = false;
                    tile.Type = Meteorite;
                }
            }
        }

        // 2) 上半部掏空成坑（半径 8~13）
        radius = 8 + rng.NextInt32(6);
        for (int x = tileX - radius; x < tileX + radius; x++)
        {
            for (int y = tileY - radius; y < tileY + radius; y++)
            {
                if (y <= tileY + rng.NextInt32(5) - 2 - 4) continue;
                double dx = Math.Abs(tileX - x);
                double dy = Math.Abs(tileY - y);
                if (Math.Sqrt(dx * dx + dy * dy) < radius * 0.8 + rng.NextInt32(7) - 3)
                    world.Tiles[x, y].Active = false;
            }
        }

        // 3) 清理与整平（半径 25~34）：清液体、去掉悬空陨石、抹平斜坡
        radius = 25 + rng.NextInt32(10);
        for (int x = tileX - radius; x < tileX + radius; x++)
        {
            for (int y = tileY - radius; y < tileY + radius; y++)
            {
                double dx = Math.Abs(tileX - x);
                double dy = Math.Abs(tileY - y);
                if (Math.Sqrt(dx * dx + dy * dy) < radius * 0.7)
                {
                    ref var tile = ref world.Tiles[x, y];
                    tile.Liquid = 0;
                }

                ref var t = ref world.Tiles[x, y];
                if (t.Type != Meteorite) continue;
                bool supported = TileIdSets.IsTileSolid(world.Tiles[x - 1, y].Type)
                                 || TileIdSets.IsTileSolid(world.Tiles[x + 1, y].Type)
                                 || TileIdSets.IsTileSolid(world.Tiles[x, y - 1].Type)
                                 || TileIdSets.IsTileSolid(world.Tiles[x, y + 1].Type);
                if (!supported) t.Active = false;
            }
        }

        // 4) 稀疏外圈（半径 23~31）：1/10 概率把实心格换成陨石
        radius = 23 + rng.NextInt32(9);
        for (int x = tileX - radius; x < tileX + radius; x++)
        {
            for (int y = tileY - radius; y < tileY + radius; y++)
            {
                if (rng.NextInt32(10) != 0) continue;
                double dx = Math.Abs(tileX - x);
                double dy = Math.Abs(tileY - y);
                if (Math.Sqrt(dx * dx + dy * dy) >= radius * 0.8) continue;
                ref var tile = ref world.Tiles[x, y];
                if (!tile.Active) continue;
                tile.Slope = 0;
                tile.HalfBrick = false;
                tile.Type = Meteorite;
            }
        }

        // 5) 最外圈（半径 30~37）：1/20 概率，位置更高
        radius = 30 + rng.NextInt32(8);
        for (int x = tileX - radius; x < tileX + radius; x++)
        {
            for (int y = tileY - radius; y < tileY + radius; y++)
            {
                if (rng.NextInt32(20) != 0) continue;
                if (y <= tileY + rng.NextInt32(5) - 2) continue;
                double dx = Math.Abs(tileX - x);
                double dy = Math.Abs(tileY - y);
                if (Math.Sqrt(dx * dx + dy * dy) >= radius * 0.9) continue;
                ref var tile = ref world.Tiles[x, y];
                if (!tile.Active) continue;
                tile.Slope = 0;
                tile.HalfBrick = false;
                tile.Type = Meteorite;
            }
        }

        world.ProgressDirty = true;
        return true;
    }

    /// <summary>
    /// 发光蘑菇地（岩层内）：先按噪声掏出不规则洞窟，再把剩余实心换成泥；
    /// 顶面（上方为空）长蘑菇草，其上再点缀蘑菇植物（<c>MushroomPlants</c>）。
    /// 原版蘑菇地判据 = 70 + 71 + 72 + 528 ≥ 100。
    /// </summary>
    private static void PlaceGlowingMushroom(WorldState world, int seed, int centerX,
        int rockLayer, int hellStart, int oceanWidth)
    {
        int cy = Math.Min(rockLayer + 120, hellStart - 120);
        int radius = 34;

        // 1) 掏出洞窟（只挖岩层内的实心格）
        for (int x = centerX - radius; x <= centerX + radius; x++)
        {
            if (x < oceanWidth + 1 || x >= world.MaxTilesX - oceanWidth - 1) continue;
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                if (y <= rockLayer + 10 || y >= hellStart - 10) continue;
                double dx = (x - centerX) / (double)radius;
                double dy = (y - cy) / (double)radius;
                if (dx * dx + dy * dy > 1.0) continue;

                ref var tile = ref world.Tiles[x, y];
                if (!tile.Active) continue;
                // 噪声决定是否挖空：形成不规则洞窟（保留一部分石柱 / 顶板）
                if (ValueNoise2D(x * 0.12, y * 0.12, seed + 131) < 0.45) tile.Active = false;
            }
        }

        // 2) 洞窟内壁 → 泥；顶面 → 蘑菇草；草上点缀蘑菇植物
        for (int x = centerX - radius; x <= centerX + radius; x++)
        {
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                ref var tile = ref world.Tiles[x, y];
                if (!tile.Active) continue;

                double dx = (x - centerX) / (double)radius;
                double dy = (y - cy) / (double)radius;
                if (dx * dx + dy * dy > 1.0) continue;

                tile.Type = Mud;
                if (y - 1 >= 0 && !world.Tiles[x, y - 1].Active)
                {
                    tile.Type = MushroomGrass;
                    // 蘑菇植物：长在蘑菇草上的非实心装饰（上方需为空），密植以保证蘑菇地判据充足
                    if (y - 2 >= 0 && !world.Tiles[x, y - 2].Active)
                    {
                        ref var plant = ref world.Tiles[x, y - 1];
                        plant.Active = true;
                        plant.Type = MushroomPlants;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 地牢（岩层内）：沿水平走廊串起若干房间，房间内填地牢墙、边界铺地牢砖。
    /// 原版地牢判据 = 地牢砖（41/43/44/481/482/483）≥ 250 且玩家中心格的墙属于
    /// <c>Main.wallDungeon</c>（7/8/9/94-99），故墙与砖必须同时产出；并写下 <c>DungeonX/Y</c>（包 7）。
    /// </summary>
    private static void PlaceDungeon(WorldState world, int centerX, int rockLayer, int oceanWidth)
    {
        const int roomWidth = 34;
        const int roomHeight = 22;
        const int rooms = 6;
        const int gap = 6;

        int startX = Math.Clamp(centerX, oceanWidth + roomWidth, world.MaxTilesX - oceanWidth - rooms * (roomWidth + gap));
        int topY = rockLayer + 60;
        int dungeonY = topY + roomHeight / 2;

        for (int r = 0; r < rooms; r++)
        {
            int x0 = startX + r * (roomWidth + gap);
            int y0 = topY;

            for (int x = x0; x < x0 + roomWidth; x++)
            {
                for (int y = y0; y < y0 + roomHeight; y++)
                {
                    if (x < 1 || x >= world.MaxTilesX - 1 || y < 1 || y >= world.MaxTilesY - 1) continue;
                    bool border = x == x0 || x == x0 + roomWidth - 1 || y == y0 || y == y0 + roomHeight - 1;

                    ref var tile = ref world.Tiles[x, y];
                    if (border)
                    {
                        tile.Active = true;
                        tile.Type = BlueDungeonBrick;
                        tile.FrameX = -1;
                        tile.FrameY = -1;
                    }
                    else
                    {
                        tile.Active = false;
                        tile.Liquid = 0;
                    }
                    tile.Wall = BlueDungeonUnsafeWall;
                }
            }

            // 房与房之间打通一条横向走廊（与两侧房间中线对齐）
            if (r + 1 < rooms)
            {
                int y = y0 + roomHeight / 2;
                for (int x = x0 + roomWidth - 1; x <= x0 + roomWidth + gap; x++)
                {
                    if (x < 1 || x >= world.MaxTilesX - 1) continue;
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        if (y + dy < 1 || y + dy >= world.MaxTilesY - 1) continue;
                        ref var tile = ref world.Tiles[x, y + dy];
                        tile.Active = false;
                        tile.Liquid = 0;
                        tile.Wall = BlueDungeonUnsafeWall;
                    }
                }
            }
        }

        world.DungeonX = startX + roomWidth / 2;
        world.DungeonY = dungeonY;
    }

    // ========================================================================
    // 噪声（确定性哈希值噪声，不依赖 System.Random）
    // ========================================================================

    /// <summary>确定性哈希 → [0,1)。</summary>
    private static double Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)seed * 1442695041u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / 4294967296.0;
        }
    }

    /// <summary>二维值噪声（平滑插值），返回 [0,1]。</summary>
    private static double ValueNoise2D(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        double fx = x - x0;
        double fy = y - y0;
        double sx = fx * fx * (3.0 - 2.0 * fx);
        double sy = fy * fy * (3.0 - 2.0 * fy);

        double n00 = Hash01(x0, y0, seed);
        double n10 = Hash01(x0 + 1, y0, seed);
        double n01 = Hash01(x0, y0 + 1, seed);
        double n11 = Hash01(x0 + 1, y0 + 1, seed);

        double a = n00 + (n10 - n00) * sx;
        double b = n01 + (n11 - n01) * sx;
        return a + (b - a) * sy;
    }

    /// <summary>分形叠加（多倍频）噪声，返回 [0,1]。</summary>
    private static double Fbm(double x, double y, int octaves, double freq, int seed)
    {
        double sum = 0, amp = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise2D(x * freq, y * freq, seed + i * 101);
            norm += amp;
            amp *= 0.5;
            freq *= 2.0;
        }
        return sum / norm;
    }

    /// <summary>由种子派生稳定的 UniqueId（避免每次启动世界标识变化）。</summary>
    private static Guid CreateDeterministicGuid(int seed)
    {
        Span<byte> bytes = stackalloc byte[16];
        ulong s = (ulong)seed * 6364136223846793005UL + 1442695040888963407UL;
        for (int i = 0; i < bytes.Length; i++)
        {
            s = s * 6364136223846793005UL + 1442695040888963407UL;
            bytes[i] = (byte)(s >> 33);
        }
        return new Guid(bytes);
    }
}
