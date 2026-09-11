// TerraAuth — .wld 世界文件解析测试
// 无真实存档文件可用，故按解析器的分段读取顺序反向构造最小合法文件（Version2 / 版本 88），
// 并覆盖版本 / 魔数 / footer 三类损坏输入的拒绝路径。

using System.IO;
using System.Text;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

public class WorldFileTests
{
    /// <summary>构造用世界文件版本：> 87（Version2）且 < 95（header 尾部字段最少）。</summary>
    private const int TestVersion = 88;
    private const string TestWorldName = "TestWorld";
    private const int TestWorldId = 12345;

    // ========================================================================
    // 一、正常解析
    // ========================================================================

    [Fact]
    public void Wld_Parses_Minimal_Valid_World()
    {
        var bytes = BuildMinimalWorld();

        var world = WorldFileReader.Read(new MemoryStream(bytes));

        Assert.Equal(TestWorldName, world.WorldName);
        Assert.Equal(TestWorldId, world.WorldId);
        Assert.Equal(16, world.MaxTilesX);
        Assert.Equal(16, world.MaxTilesY);
        Assert.Equal(5, world.SpawnTileX);
        Assert.Equal(8, world.SpawnTileY);
        Assert.Equal(8.0, world.WorldSurface);
        Assert.Equal(12.0, world.RockLayer);
        Assert.True(world.DayTime);

        // 全空世界：无实心图格、无箱子 / 牌子 / NPC
        Assert.False(world.Tiles[0, 0].Active);
        Assert.False(world.Tiles[15, 15].Active);
        Assert.Empty(world.Chests);
        Assert.Empty(world.Signs);
        Assert.Empty(world.Npcs);
    }

    // ========================================================================
    // 二、损坏输入拒绝
    // ========================================================================

    [Fact]
    public void Wld_Rejects_Unsupported_Version()
    {
        var bytes = BuildMinimalWorld();
        // 覆写版本号 → 高于支持上限
        BitConverter.GetBytes(WorldFileReader.MaxSupportedVersion + 1).CopyTo(bytes, 0);

        var ex = Assert.Throws<InvalidDataException>(() => WorldFileReader.Read(new MemoryStream(bytes)));
        Assert.Contains("高于支持上限", ex.Message);
    }

    [Fact]
    public void Wld_Rejects_Legacy_Version()
    {
        var bytes = BuildMinimalWorld();
        BitConverter.GetBytes(50).CopyTo(bytes, 0); // ≤ 87：非 Version2

        var ex = Assert.Throws<InvalidDataException>(() => WorldFileReader.Read(new MemoryStream(bytes)));
        Assert.Contains("Version2", ex.Message);
    }

    [Fact]
    public void Wld_Rejects_Bad_FileMetadata_Magic()
    {
        // 版本 ≥ 135 时首先读取 20 字节 FileMetadata（UInt64 魔数 + UInt32 + UInt64）
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(200);
            w.Write(0xDEADBEEFDEADBEEFUL); // 错误魔数
            w.Write(0u);
            w.Write(0UL);
        }

        var ex = Assert.Throws<InvalidDataException>(
            () => WorldFileReader.Read(new MemoryStream(ms.ToArray())));
        Assert.Contains("魔数", ex.Message);
    }

    [Fact]
    public void Wld_Rejects_Footer_WorldName_Mismatch()
    {
        var bytes = BuildMinimalWorld();

        // footer 位于文件末尾：bool + 7-bit 长度前缀字符串 + Int32 WorldId。
        // 定位方式：从尾部回推 WorldId(4) + 名称字节 + 长度前缀(1) + bool(1)。
        int nameBytes = Encoding.UTF8.GetByteCount(TestWorldName);
        int footerStart = bytes.Length - 4 - nameBytes - 1 - 1;
        int nameStart = footerStart + 1 + 1; // 跳过 bool + 长度前缀
        bytes[nameStart] = (byte)'X';        // 改动世界名首字符

        var ex = Assert.Throws<InvalidDataException>(() => WorldFileReader.Read(new MemoryStream(bytes)));
        Assert.Contains("世界名不匹配", ex.Message);
    }

    // ========================================================================
    // 构造最小合法 .wld（按 WorldFileReader 的读取顺序反向写出）
    // ========================================================================

    private static byte[] BuildMinimalWorld()
    {
        const int sectionCount = 11;   // 解析器要求分段指针 ≥ 11
        const int importanceCount = 8; // 重要性位图 8 位 = 1 字节

        // FileFormatHeader：Int32 版本 + Int16 段数 + 段指针×N + UInt16 重要性数 + 位图
        int headerOffset = sizeof(int) + sizeof(short) + sectionCount * sizeof(int)
                           + sizeof(ushort) + importanceCount / 8;

        var header = BuildHeader();
        var tiles = BuildEmptyTiles();
        var chests = BuildEmptyChests();
        var signs = BuildEmptySigns();
        var npcs = BuildEmptyNpcs();
        var footer = BuildFooter();

        int p0 = headerOffset;
        int p1 = p0 + header.Length;
        int p2 = p1 + tiles.Length;
        int p3 = p2 + chests.Length;
        int p4 = p3 + signs.Length;
        int p5 = p4 + npcs.Length;      // 第 5 段（图格实体）起始 = NPC 段结束
        int p10 = p5;                   // 6..9 段为空，footer 紧随其后

        int[] positions = { p0, p1, p2, p3, p4, p5, p5, p5, p5, p5, p10 };

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(TestVersion);
            w.Write((short)sectionCount);
            foreach (var pos in positions) w.Write(pos);
            w.Write((ushort)importanceCount);
            w.Write((byte)0);           // 位图：8 位全 0（无 frame-important 图格）

            w.Write(header);
            w.Write(tiles);
            w.Write(chests);
            w.Write(signs);
            w.Write(npcs);
            w.Write(footer);
        }
        return ms.ToArray();
    }

    /// <summary>头部 + 世界旗标（版本 88 的读取顺序；version &lt; 95 在此结束）。</summary>
    private static byte[] BuildHeader()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(TestWorldName);          // WorldName（7-bit 长度前缀 + UTF-8）
            // v >= 179 Seed / WorldGeneratorVersion / v >= 181 UniqueId 均不适用

            w.Write(TestWorldId);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0); // left/right/top/bottom
            w.Write(16);                      // maxTilesY
            w.Write(16);                      // maxTilesX

            // ---- 世界旗标（v88）----
            // GameMode：v < 112 → 隐式 0，不写入
            w.Write((byte)0);                 // MoonType
            for (int i = 0; i < 3; i++) w.Write(0);   // TreeX
            for (int i = 0; i < 4; i++) w.Write(0);   // TreeStyle
            for (int i = 0; i < 3; i++) w.Write(0);   // CaveBackX
            for (int i = 0; i < 4; i++) w.Write(0);   // CaveBackStyle
            w.Write(0); w.Write(0); w.Write(0);       // Ice / Jungle / Hell back style

            w.Write(5); w.Write(8);           // SpawnTileX / SpawnTileY
            w.Write(8.0); w.Write(12.0);      // WorldSurface / RockLayer
            w.Write(0.0);                     // Time
            w.Write(true);                    // DayTime
            w.Write(0);                       // MoonPhase
            w.Write(false); w.Write(false);   // BloodMoon / Eclipse
            w.Write(0); w.Write(0);           // DungeonX / DungeonY

            w.Write(false);                   // Crimson
            w.Write(false); w.Write(false); w.Write(false); // DownedBoss1..3
            w.Write(false);                   // DownedQueenBee
            w.Write(false); w.Write(false); w.Write(false); w.Write(false); // Mech1..3 / Any
            w.Write(false); w.Write(false);   // DownedPlantBoss / DownedGolemBoss
            // v >= 118 DownedSlimeKing 不适用
            w.Write(false); w.Write(false); w.Write(false); // savedGoblin/Wizard/Mech
            w.Write(false); w.Write(false); w.Write(false); w.Write(false); // Goblins/Clown/Frost/Pirates
            w.Write(false);                   // ShadowOrbSmashed
            w.Write(false);                   // spawnMeteor
            w.Write((byte)0);                 // shadowOrbCount
            w.Write(0);                       // altarCount
            w.Write(false);                   // HardMode
            // v < 257 afterPartyOfDoom 不适用
            w.Write(0); w.Write(0); w.Write(0); // InvasionDelay / Size / Type
            w.Write(0.0);                     // InvasionX
            // v >= 118 SlimeRain / v >= 113 SundialCooldown 均不适用

            w.Write(false);                   // Raining
            w.Write(0);                       // RainTime
            w.Write(0f);                      // MaxRain
            w.Write(0); w.Write(0); w.Write(0); // OreTiers[4..6]

            for (int i = 0; i < 8; i++) w.Write((byte)0); // Backgrounds[0..7]

            w.Write(0);                       // CloudBgActive
            w.Write((short)0);                // NumClouds
            w.Write(0f);                      // WindSpeedTarget
            // v < 95 → 头部在此结束
        }
        return ms.ToArray();
    }

    /// <summary>16×16 全空图格：每格仅写主标志字节 0（无数据位、无 RLE 游程）。</summary>
    private static byte[] BuildEmptyTiles() => new byte[16 * 16];

    /// <summary>箱子段：Int16 数量 0 + （v &lt; 294）Int16 defaultMaxItems 0。</summary>
    private static byte[] BuildEmptyChests()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((short)0);
            w.Write((short)0);
        }
        return ms.ToArray();
    }

    /// <summary>牌子段：Int16 数量 0。</summary>
    private static byte[] BuildEmptySigns()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            w.Write((short)0);
        return ms.ToArray();
    }

    /// <summary>NPC 段：布尔 more=false（v88 &lt; 140，无第二段）。</summary>
    private static byte[] BuildEmptyNpcs() => new byte[] { 0x00 };

    /// <summary>尾部：布尔标记 true + WorldName + WorldId。</summary>
    private static byte[] BuildFooter()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(true);
            w.Write(TestWorldName);
            w.Write(TestWorldId);
        }
        return ms.ToArray();
    }
}
