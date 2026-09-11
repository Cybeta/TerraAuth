// TerraAuth — Phase 6: 程序化世界生成（最小可加载世界）
// 目的：解除进服阻塞——默认 new WorldState() 是 1×1 世界，NetworkHost 的
//   maxSectionsX = MaxTilesX/200 = 0 → 区块列表为空 → 客户端收不到任何 TileSection（包 10）而掉线。
// 本生成器产出与原版小世界同尺寸（4200×1200）的确定性地形，供 NetworkHost 构造包 7（WorldInfo）
//   与包 10（TileSection）。字段 / 尺寸权威来源：原版 Main.maxTilesX/Y、sectionWidth/Height。

using System;

namespace TerraAuth.Simulation;

/// <summary>确定性程序化世界生成（当前仅"最小可加载世界"）。</summary>
public static class WorldGenerator
{
    /// <summary>原版小世界尺寸（Main：rightWorld/16+1 × bottomWorld/16+1）。</summary>
    public const int SmallWorldWidth = 4200;
    public const int SmallWorldHeight = 1200;

    /// <summary>原版 <c>Main.WorldGeneratorVersion</c>（1.4.5.8）。</summary>
    public const ulong GeneratorVersion = 1400159338497UL;

    // 图格 ID（Terraria.TileID）：0=泥土、1=石、2=草
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;

    // 墙壁 ID（Terraria.WallID）：1=石墙、2=土墙
    private const ushort StoneWall = 1;
    private const ushort DirtWall = 2;

    /// <summary>基准地表行与岩层行（小世界量级，均在 short 范围内）。</summary>
    private const int BaseSurfaceY = 360;
    private const int RockLayerY = 720;

    /// <summary>向导 NPC 的类型 ID（权威：原版 <c>Terraria.ID.NPCID.Guide</c>）。</summary>
    private const short GuideNpcType = 22;

    /// <summary>出生点周围压平的半宽（图格），保证稳定出生。</summary>
    private const int SpawnFlatRadius = 8;

    /// <summary>
    /// 生成一个与原版小世界同尺寸的确定性世界。
    /// 地形：正弦叠加的地表起伏 → 草皮 / 泥土 / 石层 + 对应背景墙；出生点位于世界中央的平坦地表。
    /// </summary>
    public static WorldState GenerateSmall(string worldName = "TerraAuth", int seed = 20260909)
    {
        int maxTilesX = SmallWorldWidth;
        int maxTilesY = SmallWorldHeight;

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
            double h = BaseSurfaceY
                       + 14.0 * Math.Sin(x * 0.0043 + phase)
                       + 6.0 * Math.Sin(x * 0.017 + phase * 3.0)
                       + 3.0 * Math.Sin(x * 0.061 + phase * 7.0);
            int y = (int)Math.Round(h);
            if (y < BaseSurfaceY - 40) y = BaseSurfaceY - 40;
            if (y > BaseSurfaceY + 40) y = BaseSurfaceY + 40;
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
                else if (y < RockLayerY)
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
        world.WorldSurface = BaseSurfaceY;
        world.RockLayer = RockLayerY;

        // 4. 出生点旁放置一名城镇 NPC（向导），使 NPC 同步（包 23）具备可观测对象
        int guideTileX = spawnX + 2;
        world.Npcs.Add(new WorldNpc
        {
            Type = GuideNpcType,
            GivenName = "Guide",
            X = (guideTileX + 0.5f) * 16f,
            Y = (spawnGroundY - 2) * 16f,
            IsTownNpc = true,
            HomeTileX = guideTileX,
            HomeTileY = spawnGroundY,
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
