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
    private const ushort ClayBlock = 40;
    private const ushort Sand = 53;
    private const ushort Ash = 57;
    private const ushort Hellstone = 58;

    // ---- 墙壁 ID ----
    private const ushort StoneWall = 1;
    private const ushort DirtWall = 2;

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

        // ---- 6. 地表宝箱（地下洞穴内）----
        PlaceChests(world, rng, rockLayer, hellStart);

        world.SpawnTileX = spawnX;
        world.SpawnTileY = spawnGroundY;
        world.WorldSurface = worldSurface;
        world.RockLayer = rockLayer;

        // ---- 7. 出生点旁放置一名城镇 NPC（向导），使 NPC 同步（包 23）具备可观测对象 ----
        int guideTileX = spawnX + 2;
        world.Npcs.Add(new WorldNpc
        {
            Type = GuideNpcType,
            NetId = GuideNpcType,
            GivenName = "Guide",
            X = (guideTileX + 0.5f) * 16f,
            Y = (spawnGroundY - 2) * 16f,
            IsTownNpc = true,
            HomeTileX = guideTileX,
            HomeTileY = spawnGroundY,
            Life = 250,
            LifeMax = 250,
        });

        return world;
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
