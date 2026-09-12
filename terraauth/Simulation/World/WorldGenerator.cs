// TerraAuth — Phase 6: 程序化世界生成（确定性可加载地形）
// 目的：解除进服阻塞——默认 new WorldState() 是 1×1 世界，NetworkHost 的
//   maxSectionsX = MaxTilesX/200 = 0 → 区块列表为空 → 客户端收不到任何 TileSection（包 10）而掉线。
// 本生成器支持与原版一致的三档世界尺寸（小 4200×1200 / 中 6400×1800 / 大 8400×2400），产出确定性地形，
//   供 NetworkHost 构造包 7（WorldInfo）与包 10（TileSection）。尺寸权威来源：原版客户端的世界尺寸档与区块尺寸常量。
// 注：这是「可加载地形」而非原版地形生成器 —— 无矿石 / 洞穴 / 生物群系 / 树木 / 地牢等；要完整地形请用 WorldPath 指定真实 .wld。

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

    // 图格 ID（Terraria.TileID）：0=泥土、1=石、2=草
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;

    // 墙壁 ID（Terraria.WallID）：1=石墙、2=土墙
    private const ushort StoneWall = 1;
    private const ushort DirtWall = 2;

    /// <summary>基准地表行与岩层行占世界高度的比例（小世界 0.30 / 0.60 → 360 / 720，与既有行为一致）。</summary>
    private const double SurfaceRatio = 0.30;
    private const double RockLayerRatio = 0.60;

    /// <summary>向导 NPC 的类型 ID（原版常量 22）。</summary>
    private const short GuideNpcType = 22;

    /// <summary>出生点周围压平的半宽（图格），保证稳定出生。</summary>
    private const int SpawnFlatRadius = 8;

    /// <summary>生成与原版小世界同尺寸的确定性世界（等价于 <c>Generate(WorldSize.Small, ...)</c>）。</summary>
    public static WorldState GenerateSmall(string worldName = "TerraAuth", int seed = 20260909)
        => Generate(WorldSize.Small, worldName, seed);

    /// <summary>
    /// 按尺寸档生成确定性世界。
    /// 地形：正弦叠加的地表起伏 → 草皮 / 泥土 / 石层 + 对应背景墙；出生点位于世界中央的平坦地表。
    /// </summary>
    public static WorldState Generate(WorldSize size, string worldName = "TerraAuth", int seed = 20260909)
    {
        var (maxTilesX, maxTilesY) = Dimensions(size);

        // 地表 / 岩层按世界高度比例缩放（避免大世界地表偏上、岩层过浅）
        int baseSurfaceY = (int)(maxTilesY * SurfaceRatio);
        int rockLayerY = (int)(maxTilesY * RockLayerRatio);

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

        // 1. 每列地表行：确定性正弦叠加，模拟起伏（限制在基准线附近，避免越界）
        var groundY = new int[maxTilesX];
        double phase = seed * 0.0001;
        for (int x = 0; x < maxTilesX; x++)
        {
            double h = baseSurfaceY
                       + 14.0 * Math.Sin(x * 0.0043 + phase)
                       + 6.0 * Math.Sin(x * 0.017 + phase * 3.0)
                       + 3.0 * Math.Sin(x * 0.061 + phase * 7.0);
            int y = (int)Math.Round(h);
            if (y < baseSurfaceY - 40) y = baseSurfaceY - 40;
            if (y > baseSurfaceY + 40) y = baseSurfaceY + 40;
            groundY[x] = y;
        }

        // 2. 出生点：世界中央，压平一小段地表
        int spawnX = maxTilesX / 2;
        int spawnGroundY = groundY[spawnX];
        for (int x = spawnX - SpawnFlatRadius; x <= spawnX + SpawnFlatRadius; x++)
            groundY[x] = spawnGroundY;

        // 3. 填充图格：地表以上保持空气；地表草皮；其下泥土（岩层以上）/ 石（岩层以下），并写入背景墙
        for (int x = 0; x < maxTilesX; x++)
        {
            int gy = groundY[x];
            for (int y = gy; y < maxTilesY; y++)
            {
                ref var tile = ref world.Tiles[x, y];
                tile.Active = true;
                if (y == gy)
                {
                    tile.Type = Grass;
                }
                else if (y < rockLayerY)
                {
                    tile.Type = Dirt;
                    tile.Wall = DirtWall;
                }
                else
                {
                    tile.Type = Stone;
                    tile.Wall = StoneWall;
                }
            }
        }

        world.SpawnTileX = spawnX;
        world.SpawnTileY = spawnGroundY;
        world.WorldSurface = baseSurfaceY;
        world.RockLayer = rockLayerY;

        // 4. 出生点旁放置一名城镇 NPC（向导），使 NPC 同步（包 23）具备可观测对象
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
