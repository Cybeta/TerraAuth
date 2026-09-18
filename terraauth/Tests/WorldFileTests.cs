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
        var tileEntities = new byte[sizeof(int)];
        var footer = BuildFooter();

        int p0 = headerOffset;
        int p1 = p0 + header.Length;
        int p2 = p1 + tiles.Length;
        int p3 = p2 + chests.Length;
        int p4 = p3 + signs.Length;
        int p5 = p4 + npcs.Length;
        int p6 = p5 + tileEntities.Length;
        int p10 = p6;

        int[] positions = { p0, p1, p2, p3, p4, p5, p6, p6, p6, p6, p10 };

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
            w.Write(tileEntities);
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

    // ========================================================================
    // 三、写出 → 读回（round-trip）
    // ========================================================================

    [Fact]
    public void Wld_Write_Then_Read_RoundTrips_All_Tile_Features()
    {
        var world = BuildFeatureWorld();

        var loaded = WorldFileReader.Read(new MemoryStream(WorldFileWriter.Serialize(world)));

        Assert.Equal(world.WorldName, loaded.WorldName);
        Assert.Equal(world.WorldId, loaded.WorldId);
        Assert.Equal(world.MaxTilesX, loaded.MaxTilesX);
        Assert.Equal(world.MaxTilesY, loaded.MaxTilesY);
        Assert.Equal(world.SpawnTileX, loaded.SpawnTileX);
        Assert.Equal(world.SpawnTileY, loaded.SpawnTileY);
        Assert.Equal(world.UniqueId, loaded.UniqueId);
        Assert.Equal(world.Seed, loaded.Seed);
        Assert.Equal(world.WorldSurface, loaded.WorldSurface);
        Assert.Equal(world.RockLayer, loaded.RockLayer);

        for (int x = 0; x < world.MaxTilesX; x++)
            for (int y = 0; y < world.MaxTilesY; y++)
                AssertTileEqual(world.Tiles[x, y], loaded.Tiles[x, y], x, y);

        // 箱子内容
        Assert.Equal(world.Chests.Count, loaded.Chests.Count);
        Assert.Equal(world.Chests[0].Name, loaded.Chests[0].Name);
        Assert.Equal(world.Chests[0].X, loaded.Chests[0].X);
        Assert.Equal(world.Chests[0].Items[0].Type, loaded.Chests[0].Items[0].Type);
        Assert.Equal(world.Chests[0].Items[0].Stack, loaded.Chests[0].Items[0].Stack);
        Assert.Equal(world.Chests[0].Items[0].Prefix, loaded.Chests[0].Items[0].Prefix);
        Assert.Equal(0, loaded.Chests[0].Items[1].Stack);

        // 告示牌（读取器要求所在图格是可放牌子的方块）
        Assert.Equal(world.Signs.Count, loaded.Signs.Count);
        Assert.Equal(world.Signs[0].Text, loaded.Signs[0].Text);

        // NPC（城镇 + 普通各一）
        Assert.Equal(2, loaded.Npcs.Count);
        Assert.Equal(world.Npcs[0].GivenName, loaded.Npcs[0].GivenName);
        Assert.Equal(world.Npcs[0].Type, loaded.Npcs[0].Type);
        Assert.Equal(world.Npcs[1].Type, loaded.Npcs[1].Type);
    }

    [Fact]
    public void Wld_Write_Then_Read_RoundTrips_All_TileEntity_Types()
    {
        var world = BuildFeatureWorld();
        world.InsertTileEntity(new TileEntity { Type = 0, X = 1, Y = 1, NpcSlot = 123 });
        world.InsertTileEntity(new TileEntity { Type = 1, X = 2, Y = 1, Item = Item(100, 2, 3) });
        world.InsertTileEntity(new TileEntity { Type = 2, X = 3, Y = 1, LogicCheck = 7, On = true });
        var doll = new TileEntity { Type = 3, X = 4, Y = 1, DisplayDollPose = 9 };
        doll.DisplayDollItems[0] = Item(200, 1, 1);
        doll.DisplayDollItems[8] = Item(201, 2, 2);
        doll.DisplayDollDyes[3] = Item(202, 3, 1);
        doll.DisplayDollDyes[8] = Item(203, 4, 1);
        doll.DisplayDollMisc = Item(204, 5, 1);
        world.InsertTileEntity(doll);
        world.InsertTileEntity(new TileEntity { Type = 4, X = 5, Y = 1, Item = Item(300, 1, 1) });
        var rack = new TileEntity { Type = 5, X = 6, Y = 1 };
        rack.HatRackHats[0] = Item(400, 1, 1);
        rack.HatRackHats[1] = Item(401, 2, 1);
        rack.HatRackDyes[0] = Item(402, 3, 1);
        rack.HatRackDyes[1] = Item(403, 4, 1);
        world.InsertTileEntity(rack);
        world.InsertTileEntity(new TileEntity { Type = 6, X = 7, Y = 1, Item = Item(500, 1, 1) });
        world.InsertTileEntity(new TileEntity { Type = 7, X = 8, Y = 1 });
        world.InsertTileEntity(new TileEntity { Type = 8, X = 9, Y = 1, Item = Item(600, 1, 1) });
        world.InsertTileEntity(new TileEntity { Type = 9, X = 10, Y = 1, Item = new TileEntityItem { Type = 700 } });
        world.InsertTileEntity(new TileEntity { Type = 10, X = 11, Y = 1, Item = new TileEntityItem { Type = 701 } });

        var loaded = WorldFileReader.Read(new MemoryStream(WorldFileWriter.Serialize(world))).SnapshotTileEntities();

        Assert.Equal(11, loaded.Count);
        Assert.Equal(Enumerable.Range(0, 11), loaded.Select(static entity => entity.Id));
        Assert.Equal(123, loaded[0].NpcSlot);
        AssertTileEntityItem(Item(100, 2, 3), loaded[1].Item);
        Assert.Equal((byte)7, loaded[2].LogicCheck);
        Assert.True(loaded[2].On);
        Assert.Equal((byte)9, loaded[3].DisplayDollPose);
        AssertTileEntityItem(Item(200, 1, 1), loaded[3].DisplayDollItems[0]);
        AssertTileEntityItem(Item(201, 2, 2), loaded[3].DisplayDollItems[8]);
        AssertTileEntityItem(Item(203, 4, 1), loaded[3].DisplayDollDyes[8]);
        AssertTileEntityItem(Item(204, 5, 1), loaded[3].DisplayDollMisc);
        AssertTileEntityItem(Item(403, 4, 1), loaded[5].HatRackDyes[1]);
        Assert.Equal((short)700, loaded[9].Item.Type);
        Assert.Equal((short)701, loaded[10].Item.Type);
    }

    [Fact]
    public void TileEntity_FilePayload_RoundTrips_ComplexEntities()
    {
        var doll = new TileEntity { Type = 3, FileId = 41, X = 12, Y = 13, DisplayDollPose = 9 };
        doll.DisplayDollItems[0] = Item(200, 1, 1);
        doll.DisplayDollItems[8] = Item(201, 2, 2);
        doll.DisplayDollDyes[8] = Item(203, 4, 1);
        doll.DisplayDollMisc = Item(204, 5, 1);

        var rack = new TileEntity { Type = 5, FileId = 42, X = 14, Y = 15 };
        rack.HatRackHats[0] = Item(400, 1, 1);
        rack.HatRackDyes[1] = Item(403, 4, 1);

        var loadedDoll = TileEntity.DeserializeFilePayload(doll.SerializeFilePayload());
        var loadedRack = TileEntity.DeserializeFilePayload(rack.SerializeFilePayload());

        Assert.Equal(doll.FileId, loadedDoll.FileId);
        Assert.Equal(doll.X, loadedDoll.X);
        Assert.Equal(doll.Y, loadedDoll.Y);
        Assert.Equal(doll.DisplayDollPose, loadedDoll.DisplayDollPose);
        AssertTileEntityItem(doll.DisplayDollItems[8], loadedDoll.DisplayDollItems[8]);
        AssertTileEntityItem(doll.DisplayDollDyes[8], loadedDoll.DisplayDollDyes[8]);
        AssertTileEntityItem(doll.DisplayDollMisc, loadedDoll.DisplayDollMisc);

        Assert.Equal(rack.FileId, loadedRack.FileId);
        AssertTileEntityItem(rack.HatRackHats[0], loadedRack.HatRackHats[0]);
        AssertTileEntityItem(rack.HatRackDyes[1], loadedRack.HatRackDyes[1]);
    }

    [Fact]
    public void Wld_Rejects_Unknown_TileEntity_Type()
    {
        var world = BuildFeatureWorld();
        world.InsertTileEntity(new TileEntity { Type = 7, X = 1, Y = 1 });
        var bytes = WorldFileWriter.Serialize(world);
        using (var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8, leaveOpen: false))
        {
            _ = r.ReadInt32(); _ = r.ReadUInt64(); _ = r.ReadUInt32(); _ = r.ReadUInt64();
            int count = r.ReadInt16();
            int[] positions = Enumerable.Range(0, count).Select(_ => r.ReadInt32()).ToArray();
            bytes[positions[5] + sizeof(int)] = 11;
        }

        var error = Assert.Throws<InvalidDataException>(() => WorldFileReader.Read(new MemoryStream(bytes)));
        Assert.Contains("不支持的图格实体类型", error.Message);
    }

    private static TileEntityItem Item(short type, byte prefix, short stack)
        => new() { Type = type, Prefix = prefix, Stack = stack };

    private static void AssertTileEntityItem(TileEntityItem expected, TileEntityItem actual)
    {
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Prefix, actual.Prefix);
        Assert.Equal(expected.Stack, actual.Stack);
    }

    [Fact]
    public void Wld_Write_Then_Read_RoundTrips_Header_Flags()
    {
        var world = BuildFeatureWorld();
        world.Progress.HardMode = true;
        world.Progress.DownedMoonlord = true;
        world.Progress.DownedMechBoss2 = true;
        world.Progress.Crimson = true;
        world.Progress.ZenithWorld = true;
        world.GameMode = 3;
        world.MoonPhase = 5;
        world.Time = 12345.5;
        world.DayTime = false;
        world.SandstormIntensity = 0.75f;

        var loaded = WorldFileReader.Read(new MemoryStream(WorldFileWriter.Serialize(world)));

        Assert.True(loaded.Progress.HardMode);
        Assert.True(loaded.Progress.DownedMoonlord);
        Assert.True(loaded.Progress.DownedMechBoss2);
        Assert.True(loaded.Progress.Crimson);
        Assert.True(loaded.Progress.ZenithWorld);
        Assert.Equal(3, loaded.GameMode);
        Assert.Equal(5, loaded.MoonPhase);
        Assert.Equal(12345.5, loaded.Time);
        Assert.False(loaded.DayTime);
        Assert.Equal(0.75f, loaded.SandstormIntensity);
    }

    /// <summary>
    /// 原版 <c>FileMetadata.Read</c> 要求元数据 UInt64 的**最高字节 = FileType**（World = 2），
    /// 否则抛 <c>Found invalid file type.</c> 而拒绝加载。本服务端读取器只校验低 56 位魔数，
    /// round-trip 覆盖不到该字节 —— 故单独钉住（曾被原版服务端实测拒绝）。
    /// </summary>
    [Fact]
    public void Wld_Written_Metadata_Carries_WorldFileType()
    {
        var bytes = WorldFileWriter.Serialize(BuildFeatureWorld());

        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        _ = r.ReadInt32();                     // 版本
        ulong metadata = r.ReadUInt64();       // 元数据：低 56 位魔数 + 最高字节文件类型

        Assert.Equal(0x6369676F6C6572uL, metadata & 0x00FFFFFFFFFFFFFFuL); // "relogic"
        Assert.Equal(2, (byte)(metadata >> 56));                           // FileType.World
    }

    /// <summary>
    /// 严格分段走查：按**原版加载器**的顺序与位置断言逐段校验写出文件。
    /// 本服务端读取器会直接跳到 footer（跳过 5..9 段），因此 round-trip 覆盖不到这些段；
    /// 而原版加载器会逐段读取并要求「读完正好落在下一段起点」。本用例正是补上这一层。
    /// </summary>
    [Fact]
    public void Wld_Written_File_Passes_Strict_Section_Walk()
    {
        var world = BuildFeatureWorld();
        var bytes = WorldFileWriter.Serialize(world);

        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        Assert.True(r.ReadInt32() is > 87 and <= 326, "世界文件版本非法");
        _ = r.ReadUInt64();      // 文件元数据：魔数
        _ = r.ReadUInt32();      // 修订号
        _ = r.ReadUInt64();      // 旗标

        int sectionCount = r.ReadInt16();
        Assert.True(sectionCount >= 11, $"分段数量不足：{sectionCount}");
        var pos = new int[sectionCount];
        for (int i = 0; i < sectionCount; i++) pos[i] = r.ReadInt32();

        int importanceCount = r.ReadUInt16();
        r.BaseStream.Position += (importanceCount + 7) / 8;

        // 各分段起点必须严格递增（原版会逐段比对位置，重复 / 倒序即 BadSectionPointer）
        for (int i = 1; i < pos.Length; i++)
            Assert.True(pos[i] > pos[i - 1], $"分段 {i} 起点未递增：{pos[i - 1]} → {pos[i]}");

        Assert.Equal(pos[0], ms.Position);   // 头部紧接在分段指针 + 重要度位图之后

        // 段 5 = 图格实体（Int32 数量）
        ms.Position = pos[5];
        Assert.Equal(0, r.ReadInt32());
        Assert.Equal(pos[6], ms.Position);

        // 段 6 = 加权压力板（Int32 数量）
        ms.Position = pos[6];
        Assert.Equal(0, r.ReadInt32());
        Assert.Equal(pos[7], ms.Position);

        // 段 7 = 城镇房间管理（Int32 数量）
        ms.Position = pos[7];
        Assert.Equal(0, r.ReadInt32());
        Assert.Equal(pos[8], ms.Position);

        // 段 8 = 图鉴（击杀 / 目击 / 交谈 三个统计块，各 Int32 数量）
        ms.Position = pos[8];
        Assert.Equal(0, r.ReadInt32());
        Assert.Equal(0, r.ReadInt32());
        Assert.Equal(0, r.ReadInt32());
        Assert.Equal(pos[9], ms.Position);

        // 段 9 = 创造之力（以 false 终止）
        ms.Position = pos[9];
        Assert.False(r.ReadBoolean());
        Assert.Equal(pos[10], ms.Position);

        // 段 10 = footer：标记 + 世界名 + WorldId，且正好用完文件
        ms.Position = pos[10];
        Assert.True(r.ReadBoolean());
        Assert.Equal(world.WorldName, r.ReadString());
        Assert.Equal(world.WorldId, r.ReadInt32());
        Assert.Equal(bytes.Length, ms.Position);
    }

    /// <summary>构造覆盖各类图格特征的小世界（64×32，便于逐格比对）。</summary>
    private static WorldState BuildFeatureWorld()
    {
        var world = new WorldState
        {
            MaxTilesX = 64,
            MaxTilesY = 32,
            Tiles = new TileMap(64, 32),
            WorldName = "FeatureWorld",
            WorldId = 777,
            UniqueId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Seed = "seed-1",
            WorldGeneratorVersion = 1400159338497UL,
            SpawnTileX = 16,
            SpawnTileY = 10,
            WorldSurface = 8.0,
            RockLayer = 20.0,
            DayTime = true,
            Time = 27000.0,
            MoonType = 2,
        };

        void Set(int x, int y, Tile tile) => world.Tiles[x, y] = tile;

        Set(1, 1, new Tile { Active = true, Type = 1, Wall = 2 });                       // 普通实心 + 墙
        Set(2, 2, new Tile { Active = true, Type = 4, FrameX = 18, FrameY = 36, TileColor = 7 }); // 重要图格 + 油漆
        Set(3, 3, new Tile { Wall = 300, WallColor = 9 });                               // 高位墙 + 墙漆
        Set(4, 4, new Tile { Liquid = 128, LiquidType = 0 });                            // 水
        Set(5, 5, new Tile { Liquid = 200, LiquidType = 1 });                            // 岩浆
        Set(6, 6, new Tile { Liquid = 90, LiquidType = 2 });                             // 蜂蜜
        Set(7, 7, new Tile { Liquid = 255, LiquidType = 3 });                            // 微光
        Set(8, 8, new Tile { Wire = true, Wire2 = true, Wire3 = true, Wire4 = true });   // 四色线
        Set(9, 9, new Tile { Active = true, Type = 1, Actuator = true, InActive = true });// 执行器
        Set(10, 10, new Tile { Active = true, Type = 1, Slope = 2 });                    // 斜坡
        Set(11, 11, new Tile { Active = true, Type = 1, HalfBrick = true });             // 半砖
        Set(12, 12, new Tile
        {
            InvisibleBlock = true, InvisibleWall = true,
            FullbrightBlock = true, FullbrightWall = true,
        });                                                                              // 隐形 / 全亮
        Set(13, 13, new Tile { Active = true, Type = 300 });                             // 双字节图格类型
        Set(14, 14, new Tile { Active = true, Type = 55 });                              // 牌子所在方块

        world.Chests.Add(new Chest
        {
            Index = 0,
            X = 20,
            Y = 12,
            Name = "宝箱",
            Items = new[]
            {
                new ChestItem { Type = 5, Stack = 7, Prefix = 3 },
                new ChestItem { Type = 0, Stack = 0 },
            },
        });

        world.Signs.Add(new Sign { Index = 0, X = 14, Y = 14, Text = "牌子文本" });
        world.Npcs.Add(new WorldNpc
        {
            Type = 22, IsTownNpc = true, GivenName = "Guide",
            X = 100.5f, Y = 200.25f, Homeless = false, HomeTileX = 20, HomeTileY = 12,
        });
        world.Npcs.Add(new WorldNpc { Type = 1, IsTownNpc = false, X = 300.5f, Y = 400.5f });

        return world;
    }

    /// <summary>
    /// 逐格比对。注意读取器会做两处规范化，比较时须按同一口径：
    /// 非重要图格的 FrameX/FrameY 归 -1；非 SaveSlopes 图格的斜坡 / 半砖被丢弃。
    /// </summary>
    private static void AssertTileEqual(Tile expected, Tile actual, int x, int y)
    {
        string at = $"({x},{y})";

        Assert.True(expected.Active == actual.Active, $"{at} Active");
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Wall, actual.Wall);
        Assert.Equal(expected.Liquid, actual.Liquid);
        Assert.Equal(expected.LiquidType, actual.LiquidType);
        Assert.Equal(expected.Wire, actual.Wire);
        Assert.Equal(expected.Wire2, actual.Wire2);
        Assert.Equal(expected.Wire3, actual.Wire3);
        Assert.Equal(expected.Wire4, actual.Wire4);
        Assert.Equal(expected.Actuator, actual.Actuator);
        Assert.Equal(expected.InActive, actual.InActive);
        Assert.Equal(expected.InvisibleBlock, actual.InvisibleBlock);
        Assert.Equal(expected.InvisibleWall, actual.InvisibleWall);
        Assert.Equal(expected.FullbrightBlock, actual.FullbrightBlock);
        Assert.Equal(expected.FullbrightWall, actual.FullbrightWall);
        Assert.Equal(expected.TileColor, actual.TileColor);
        Assert.Equal(expected.WallColor, actual.WallColor);

        if (expected.Active && TileIdSets.IsTileFrameImportant(expected.Type))
        {
            Assert.Equal(expected.FrameX, actual.FrameX);
            Assert.Equal(expected.FrameY, actual.FrameY);
        }
        else if (expected.Active)
        {
            Assert.Equal(-1, actual.FrameX);
            Assert.Equal(-1, actual.FrameY);
        }

        if (TileIdSets.SaveSlopes(expected.Type))
        {
            Assert.Equal(expected.Slope, actual.Slope);
            Assert.Equal(expected.HalfBrick, actual.HalfBrick);
        }
    }
}
