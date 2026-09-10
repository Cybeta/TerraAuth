// TerraAuth — Phase 6: .wld 世界文件解析器
// 权威来源：本地客户端原版 WorldFile.LoadWorld_Version2 及其分段加载方法
//   LoadFileFormatHeader / LoadHeader / LoadWorldFlags / LoadWorldTiles / LoadChests / LoadSigns /
//   LoadNPCs / LoadFooter（Terraria 1.4.5.8 / 世界版本 326）
// 兼容：自动识别 gzip 压缩（1f 8b）与原始字节两种世界文件。

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TerraAuth.Simulation;

/// <summary>解析 Terraria <c>.wld</c> 世界文件为 <see cref="WorldState"/>。</summary>
public static class WorldFileReader
{
    /// <summary>原版 <c>WorldFile.SaveFileFormatHeader</c> 写入的世界文件版本。</summary>
    public const int MaxSupportedVersion = 326;

    /// <summary>原版 <c>WallID.Count</c>；墙壁 ID 越界时归零。</summary>
    private const int WallIdCount = 367;

    private const ulong FileMetadataMagicLow56 = 0x6369676F6C6572uL;

    public static WorldState Read(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    public static WorldState Read(Stream stream)
    {
        Stream data = stream;
        GZipStream? gzip = null;

        if (stream.CanSeek)
        {
            long pos = stream.Position;
            int b1 = stream.ReadByte();
            int b2 = stream.ReadByte();
            stream.Position = pos;
            if (b1 == 0x1F && b2 == 0x8B)
                data = gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        }

        try
        {
            using var reader = new BinaryReader(data, Encoding.UTF8, leaveOpen: true);
            return ReadCore(reader);
        }
        finally
        {
            gzip?.Dispose();
        }
    }

    private static WorldState ReadCore(BinaryReader reader)
    {
        int version = reader.ReadInt32();
        if (version <= 0)
            throw new InvalidDataException($"世界文件版本非法：{version}");
        if (version > MaxSupportedVersion)
            throw new InvalidDataException($"世界文件版本 {version} 高于支持上限 {MaxSupportedVersion}");
        if (version <= 87)
            throw new InvalidDataException($"仅支持 Version2 世界文件（版本 > 87），当前 {version}");

        bool[] importance = LoadFileFormatHeader(reader, version, out int[] positions);

        if (positions.Length < 11)
            throw new InvalidDataException($"世界文件分段指针数量不足（{positions.Length} < 11），不支持版本 {version}");

        Expect(reader, positions[0], "header");

        var state = new WorldState();
        LoadHeader(reader, version, state);

        Expect(reader, positions[1], "tiles");
        state.Tiles = new TileMap(state.MaxTilesX, state.MaxTilesY);
        LoadWorldTiles(reader, importance, state);

        Expect(reader, positions[2], "chests");
        LoadChests(reader, version, state);

        Expect(reader, positions[3], "signs");
        LoadSigns(reader, state);

        Expect(reader, positions[4], "npcs");
        LoadNpcs(reader, version, state);

        Expect(reader, positions[5], "tile entities");

        // section 6..10：图格实体 / 压力板 / 城镇管理 / 图鉴 / 创造之力。
        // 服务端进入世界不依赖这些数据，直接跳到 footer 起始指针。
        reader.BaseStream.Position = positions[10];
        LoadFooter(reader, state);

        return state;
    }

    private static void Expect(BinaryReader reader, int expected, string section)
    {
        if (reader.BaseStream.Position != expected)
            throw new InvalidDataException($"世界文件分段指针不匹配（{section}）：期望 {expected}，实际 {reader.BaseStream.Position}");
    }

    // ---------------- File format header ----------------

    private static bool[] LoadFileFormatHeader(BinaryReader reader, int version, out int[] positions)
    {
        if (version >= 135)
            ReadFileMetadata(reader);

        short count = reader.ReadInt16();
        positions = new int[count];
        for (int i = 0; i < count; i++)
            positions[i] = reader.ReadInt32();

        ushort importanceCount = reader.ReadUInt16();
        var importance = new bool[importanceCount];
        byte b = 0;
        byte b2 = 128;
        for (int i = 0; i < importanceCount; i++)
        {
            if (b2 == 128)
            {
                b = reader.ReadByte();
                b2 = 1;
            }
            else
            {
                b2 <<= 1;
            }

            if ((b & b2) == b2)
                importance[i] = true;
        }

        return importance;
    }

    /// <summary>原版 <c>FileMetadata.Read</c>：UInt64 魔数 + UInt32 Revision + UInt64 flags（共 20 字节）。</summary>
    private static void ReadFileMetadata(BinaryReader reader)
    {
        ulong num = reader.ReadUInt64();
        if ((num & 0x00FFFFFFFFFFFFFFuL) != FileMetadataMagicLow56)
            throw new InvalidDataException("世界文件元数据魔数不匹配");
        _ = reader.ReadUInt32(); // Revision
        _ = reader.ReadUInt64(); // flags
    }

    // ---------------- Header ----------------

    private static void LoadHeader(BinaryReader reader, int version, WorldState state)
    {
        state.WorldName = reader.ReadString();

        if (version >= 179)
        {
            state.Seed = version != 179 ? reader.ReadString() : reader.ReadInt32().ToString();
            state.WorldGeneratorVersion = reader.ReadUInt64();
        }

        state.UniqueId = version >= 181 ? new Guid(reader.ReadBytes(16)) : Guid.NewGuid();

        state.WorldId = reader.ReadInt32();
        _ = reader.ReadInt32(); // leftWorld
        _ = reader.ReadInt32(); // rightWorld
        _ = reader.ReadInt32(); // topWorld
        _ = reader.ReadInt32(); // bottomWorld

        int maxTilesY = reader.ReadInt32();
        int maxTilesX = reader.ReadInt32();

        if (maxTilesX <= 0 || maxTilesY <= 0 || maxTilesX > 8400 || maxTilesY > 2400)
            throw new InvalidDataException($"世界尺寸非法：{maxTilesX} x {maxTilesY}");

        state.MaxTilesX = maxTilesX;
        state.MaxTilesY = maxTilesY;

        LoadWorldFlags(reader, version, state);
    }

    // ---------------- World flags ----------------

    private static void LoadWorldFlags(BinaryReader reader, int version, WorldState state)
    {
        var p = state.Progress;

        if (version >= 209)
        {
            state.GameMode = reader.ReadInt32();
            if (version >= 222) p.DrunkWorld = reader.ReadBoolean();
            if (version >= 227) p.GetGoodWorld = reader.ReadBoolean();
            if (version >= 238) p.TenthAnniversaryWorld = reader.ReadBoolean();
            if (version >= 239) p.DontStarveWorld = reader.ReadBoolean();
            if (version >= 241) p.NotTheBeesWorld = reader.ReadBoolean();
            if (version >= 249) p.RemixWorld = reader.ReadBoolean();
            if (version >= 266) p.NoTrapsWorld = reader.ReadBoolean();
            if (version >= 267) p.ZenithWorld = reader.ReadBoolean();
            else p.ZenithWorld = p.RemixWorld && p.DrunkWorld;
            if (version >= 302) p.SkyblockWorld = reader.ReadBoolean();
        }
        else
        {
            state.GameMode = version >= 112 ? (reader.ReadBoolean() ? 1 : 0) : 0;
            if (version == 208 && reader.ReadBoolean())
                state.GameMode = 2;
        }

        if (version >= 141) _ = reader.ReadInt64(); // CreationTime
        if (version >= 284) _ = reader.ReadInt64(); // LastPlayed

        state.MoonType = reader.ReadByte();

        for (int i = 0; i < 3; i++) state.TreeX[i] = reader.ReadInt32();
        for (int i = 0; i < 4; i++) state.TreeStyle[i] = reader.ReadInt32();
        for (int i = 0; i < 3; i++) state.CaveBackX[i] = reader.ReadInt32();
        for (int i = 0; i < 4; i++) state.CaveBackStyle[i] = reader.ReadInt32();

        state.IceBackStyle = reader.ReadInt32();
        state.JungleBackStyle = reader.ReadInt32();
        state.HellBackStyle = reader.ReadInt32();

        state.SpawnTileX = reader.ReadInt32();
        state.SpawnTileY = reader.ReadInt32();
        state.WorldSurface = reader.ReadDouble();
        state.RockLayer = reader.ReadDouble();

        state.Time = reader.ReadDouble();
        state.DayTime = reader.ReadBoolean();
        state.MoonPhase = reader.ReadInt32();
        state.BloodMoon = reader.ReadBoolean();
        state.Eclipse = reader.ReadBoolean();

        state.DungeonX = reader.ReadInt32();
        state.DungeonY = reader.ReadInt32();

        p.Crimson = reader.ReadBoolean();

        p.DownedBoss1 = reader.ReadBoolean();
        p.DownedBoss2 = reader.ReadBoolean();
        p.DownedBoss3 = reader.ReadBoolean();
        p.DownedQueenBee = reader.ReadBoolean();
        p.DownedMechBoss1 = reader.ReadBoolean();
        p.DownedMechBoss2 = reader.ReadBoolean();
        p.DownedMechBoss3 = reader.ReadBoolean();
        p.DownedMechBossAny = reader.ReadBoolean();
        p.DownedPlantBoss = reader.ReadBoolean();
        p.DownedGolemBoss = reader.ReadBoolean();
        if (version >= 118) p.DownedSlimeKing = reader.ReadBoolean();

        _ = reader.ReadBoolean(); // savedGoblin
        _ = reader.ReadBoolean(); // savedWizard
        _ = reader.ReadBoolean(); // savedMech
        p.DownedGoblins = reader.ReadBoolean();
        p.DownedClown = reader.ReadBoolean();
        p.DownedFrost = reader.ReadBoolean();
        p.DownedPirates = reader.ReadBoolean();

        p.ShadowOrbSmashed = reader.ReadBoolean();
        _ = reader.ReadBoolean(); // spawnMeteor
        _ = reader.ReadByte();    // shadowOrbCount
        _ = reader.ReadInt32();   // altarCount
        p.HardMode = reader.ReadBoolean();
        if (version >= 257) _ = reader.ReadBoolean(); // afterPartyOfDoom

        state.InvasionDelay = reader.ReadInt32();
        state.InvasionSize = reader.ReadInt32();
        state.InvasionType = reader.ReadInt32();
        state.InvasionX = reader.ReadDouble();

        if (version >= 118) p.SlimeRain = reader.ReadDouble() > 0.0;
        if (version >= 113) state.SundialCooldown = reader.ReadByte();

        state.Raining = reader.ReadBoolean();
        state.RainTime = reader.ReadInt32();
        state.MaxRain = reader.ReadSingle();

        state.OreTiers[4] = (short)reader.ReadInt32(); // Cobalt
        state.OreTiers[5] = (short)reader.ReadInt32(); // Mythril
        state.OreTiers[6] = (short)reader.ReadInt32(); // Adamantite

        for (int i = 0; i < 8; i++) state.Backgrounds[i] = reader.ReadByte();

        state.CloudBgActive = reader.ReadInt32();
        state.NumClouds = reader.ReadInt16();
        state.WindSpeedTarget = reader.ReadSingle();

        if (version < 95) return;

        int anglerCount = reader.ReadInt32();
        for (int i = 0; i < anglerCount; i++) _ = reader.ReadString();

        if (version < 99) return;
        _ = reader.ReadBoolean(); // savedAngler
        if (version < 101) return;
        _ = reader.ReadInt32();   // anglerQuest
        if (version < 104) return;
        _ = reader.ReadBoolean(); // savedStylist
        if (version >= 129) _ = reader.ReadBoolean(); // savedTaxCollector
        if (version >= 201) _ = reader.ReadBoolean(); // savedGolfer

        if (version < 107)
        {
            state.InvasionSizeStart = state.InvasionSize;
        }
        else
        {
            state.InvasionSizeStart = reader.ReadInt32();
        }

        if (version >= 108) _ = reader.ReadInt32(); // _tempCultistDelay

        if (version < 109) return;
        BannerSystemLoad(reader, version);

        if (version < 128) return;
        p.FastForwardTimeToDawn = reader.ReadBoolean();

        if (version < 131) return;
        p.DownedFishron = reader.ReadBoolean();
        p.DownedMartians = reader.ReadBoolean();
        p.DownedAncientCultist = reader.ReadBoolean();
        p.DownedMoonlord = reader.ReadBoolean();
        p.DownedHalloweenKing = reader.ReadBoolean();
        p.DownedHalloweenTree = reader.ReadBoolean();
        p.DownedChristmasIceQueen = reader.ReadBoolean();
        p.DownedChristmasSantank = reader.ReadBoolean();
        p.DownedChristmasTree = reader.ReadBoolean();

        if (version < 140) return;
        p.DownedTowerSolar = reader.ReadBoolean();
        p.DownedTowerVortex = reader.ReadBoolean();
        p.DownedTowerNebula = reader.ReadBoolean();
        p.DownedTowerStardust = reader.ReadBoolean();
        _ = reader.ReadBoolean(); // TowerActiveSolar
        _ = reader.ReadBoolean(); // TowerActiveVortex
        _ = reader.ReadBoolean(); // TowerActiveNebula
        _ = reader.ReadBoolean(); // TowerActiveStardust
        _ = reader.ReadBoolean(); // LunarApocalypseIsUp

        if (version < 170)
        {
            p.PartyIsUp = false;
        }
        else
        {
            _ = reader.ReadBoolean(); // _tempPartyManual
            p.PartyIsUp = reader.ReadBoolean(); // _tempPartyGenuine
            _ = reader.ReadInt32();   // _tempPartyCooldown
            int partyCount = reader.ReadInt32();
            for (int i = 0; i < partyCount; i++) _ = reader.ReadInt32();
        }

        if (version < 174)
        {
            p.SandstormHappening = false;
            state.SandstormIntensity = 0f;
        }
        else
        {
            p.SandstormHappening = reader.ReadBoolean();
            _ = reader.ReadInt32(); // _tempSandstormTimeLeft
            _ = reader.ReadSingle(); // _tempSandstormSeverity
            state.SandstormIntensity = reader.ReadSingle(); // _tempSandstormIntendedSeverity
        }

        Dd2EventLoad(reader, version, p);

        state.Backgrounds[8] = version > 194 ? reader.ReadByte() : (byte)0;
        state.Backgrounds[9] = version >= 215 ? reader.ReadByte() : (byte)0;
        if (version > 195)
        {
            state.Backgrounds[10] = reader.ReadByte();
            state.Backgrounds[11] = reader.ReadByte();
            state.Backgrounds[12] = reader.ReadByte();
        }

        if (version >= 204) p.CombatBookWasUsed = reader.ReadBoolean();

        if (version >= 207)
        {
            _ = reader.ReadInt32();          // _tempLanternNightCooldown
            p.LanternsUp = reader.ReadBoolean(); // _tempLanternNightGenuine
            _ = reader.ReadBoolean();        // _tempLanternNightManual
            _ = reader.ReadBoolean();        // _tempLanternNightNextNightIsGenuine
        }

        TreeTopsLoad(reader, version, state);

        if (version >= 212)
        {
            p.ForceHalloweenForToday = reader.ReadBoolean();
            p.ForceXMasForToday = reader.ReadBoolean();
        }

        if (version >= 216)
        {
            state.OreTiers[0] = (short)reader.ReadInt32(); // Copper
            state.OreTiers[1] = (short)reader.ReadInt32(); // Iron
            state.OreTiers[2] = (short)reader.ReadInt32(); // Silver
            state.OreTiers[3] = (short)reader.ReadInt32(); // Gold
        }
        else
        {
            for (int i = 0; i < 4; i++) state.OreTiers[i] = -1;
        }

        if (version >= 217)
        {
            p.BoughtCat = reader.ReadBoolean();
            p.BoughtDog = reader.ReadBoolean();
            p.BoughtBunny = reader.ReadBoolean();
        }

        if (version >= 223)
        {
            p.DownedEmpressOfLight = reader.ReadBoolean();
            p.DownedQueenSlime = reader.ReadBoolean();
        }

        if (version >= 240) p.DownedDeerclops = reader.ReadBoolean();
        if (version >= 250) p.UnlockedSlimeBlueSpawn = reader.ReadBoolean();

        if (version >= 251)
        {
            _ = reader.ReadBoolean(); // unlockedMerchantSpawn
            _ = reader.ReadBoolean(); // unlockedDemolitionistSpawn
            _ = reader.ReadBoolean(); // unlockedPartyGirlSpawn
            _ = reader.ReadBoolean(); // unlockedDyeTraderSpawn
            p.UnlockedTruffleSpawn = reader.ReadBoolean();
            _ = reader.ReadBoolean(); // unlockedArmsDealerSpawn
            _ = reader.ReadBoolean(); // unlockedNurseSpawn
            _ = reader.ReadBoolean(); // unlockedPrincessSpawn
        }

        if (version >= 259) p.CombatBookVolumeTwoWasUsed = reader.ReadBoolean();
        if (version >= 260) p.PeddlersSatchelWasUsed = reader.ReadBoolean();

        if (version >= 261)
        {
            p.UnlockedSlimeGreenSpawn = reader.ReadBoolean();
            p.UnlockedSlimeOldSpawn = reader.ReadBoolean();
            p.UnlockedSlimePurpleSpawn = reader.ReadBoolean();
            p.UnlockedSlimeRainbowSpawn = reader.ReadBoolean();
            p.UnlockedSlimeRedSpawn = reader.ReadBoolean();
            p.UnlockedSlimeYellowSpawn = reader.ReadBoolean();
            p.UnlockedSlimeCopperSpawn = reader.ReadBoolean();
        }

        if (version >= 264)
        {
            p.FastForwardTimeToDusk = reader.ReadBoolean();
            state.MoondialCooldown = reader.ReadByte();
        }

        if (version >= 287)
        {
            p.ForceHalloweenForever = reader.ReadBoolean();
            p.ForceXMasForever = reader.ReadBoolean();
        }

        if (version >= 288) p.VampireSeed = reader.ReadBoolean();
        if (version >= 296) p.InfectedSeed = reader.ReadBoolean();

        if (version >= 291)
        {
            _ = reader.ReadInt32(); // _tempMeteorShowerCount
            _ = reader.ReadInt32(); // _tempCoinRain
        }

        if (version >= 297)
        {
            p.TeamBasedSpawnsSeed = reader.ReadBoolean();
            ExtraSpawnPointManagerLoad(reader, state);
        }

        p.DualDungeonsSeed = version >= 304 && reader.ReadBoolean();
        p.MoreLightningSeed = version >= 323 && reader.ReadBoolean();
        p.NoLightningSeed = version >= 323 && reader.ReadBoolean();

        if (version >= 299 && version < 313) _ = reader.ReadUInt32();
        if (version >= 299) _ = reader.ReadString(); // WorldGen.Manifest
    }

    /// <summary>原版 <c>BannerSystem.Load</c>：Int16 数量 + N×Int32；v≥289 再读 Int16 数量 + N×UInt16。</summary>
    private static void BannerSystemLoad(BinaryReader reader, int version)
    {
        int count = reader.ReadInt16();
        for (int i = 0; i < count; i++) _ = reader.ReadInt32();

        if (version < 289) return;

        count = reader.ReadInt16();
        for (int i = 0; i < count; i++) _ = reader.ReadUInt16();
    }

    /// <summary>原版 <c>DD2Event.Load</c>：v≥178 读 savedBartender + DownedInvasionT1/2/3。</summary>
    private static void Dd2EventLoad(BinaryReader reader, int version, WorldProgress p)
    {
        if (version < 178) return;

        _ = reader.ReadBoolean(); // savedBartender
        p.Dd2DownedInvasionT1 = reader.ReadBoolean();
        p.Dd2DownedInvasionT2 = reader.ReadBoolean();
        p.Dd2DownedInvasionT3 = reader.ReadBoolean();
    }

    /// <summary>原版 <c>WorldGen.TreeTops.Load</c>：v≥211 读 Int32 数量 + N×Int32（仅前 13 项有效）。</summary>
    private static void TreeTopsLoad(BinaryReader reader, int version, WorldState state)
    {
        if (version < 211)
        {
            for (int i = 0; i < 4; i++) state.TreeTops[i] = state.TreeStyle[i];
            for (int i = 0; i < 9; i++) state.TreeTops[4 + i] = state.Backgrounds[4 + i];
            return;
        }

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            int v = reader.ReadInt32();
            if (i < state.TreeTops.Length) state.TreeTops[i] = v;
        }
    }

    /// <summary>原版 <c>ExtraSpawnPointManager.Read</c>：Byte 数量 + N×(Int16 X, Int16 Y)。</summary>
    private static void ExtraSpawnPointManagerLoad(BinaryReader reader, WorldState state)
    {
        int count = reader.ReadByte();
        for (int i = 0; i < count; i++)
        {
            short x = reader.ReadInt16();
            short y = reader.ReadInt16();
            state.ExtraSpawnPoints.Add((x, y));
        }
    }

    // ---------------- Tiles ----------------

    private static void LoadWorldTiles(BinaryReader reader, bool[] importance, WorldState state)
    {
        var tiles = state.Tiles;

        for (int i = 0; i < state.MaxTilesX; i++)
        {
            for (int j = 0; j < state.MaxTilesY; j++)
            {
                ref Tile tile = ref tiles[i, j];

                int type = -1;
                byte b = 0, b2 = 0, b3 = 0;

                byte b4 = reader.ReadByte();
                bool hasB3 = false;
                if ((b4 & 1) == 1)
                {
                    hasB3 = true;
                    b3 = reader.ReadByte();
                }

                bool hasB2 = false;
                if (hasB3 && (b3 & 1) == 1)
                {
                    hasB2 = true;
                    b2 = reader.ReadByte();
                }

                if (hasB2 && (b2 & 1) == 1)
                    b = reader.ReadByte();

                if ((b4 & 2) == 2)
                {
                    tile.Active = true;
                    if ((b4 & 0x20) == 0x20)
                    {
                        byte low = reader.ReadByte();
                        type = reader.ReadByte();
                        type = (type << 8) | low;
                    }
                    else
                    {
                        type = reader.ReadByte();
                    }

                    tile.Type = (ushort)type;

                    if (type >= 0 && type < importance.Length && importance[type])
                    {
                        tile.FrameX = reader.ReadInt16();
                        tile.FrameY = reader.ReadInt16();
                        if (tile.Type == 144) tile.FrameY = 0;
                    }
                    else
                    {
                        tile.FrameX = -1;
                        tile.FrameY = -1;
                    }

                    if ((b2 & 8) == 8)
                        tile.TileColor = reader.ReadByte();
                }

                if ((b4 & 4) == 4)
                {
                    tile.Wall = reader.ReadByte();
                    if (tile.Wall >= WallIdCount) tile.Wall = 0;

                    if ((b2 & 0x10) == 0x10)
                        tile.WallColor = reader.ReadByte();
                }

                byte liquidType = (byte)((b4 & 0x18) >> 3);
                if (liquidType != 0)
                {
                    tile.Liquid = reader.ReadByte();
                    if ((b2 & 0x80) == 0x80)
                        tile.LiquidType = 3; // shimmer
                    else if (liquidType == 2)
                        tile.LiquidType = 1; // lava
                    else if (liquidType == 3)
                        tile.LiquidType = 2; // honey
                    else
                        tile.LiquidType = 0; // water
                }

                if (b3 > 1)
                {
                    if ((b3 & 2) == 2) tile.Wire = true;
                    if ((b3 & 4) == 4) tile.Wire2 = true;
                    if ((b3 & 8) == 8) tile.Wire3 = true;

                    byte slope = (byte)((b3 & 0x70) >> 4);
                    if (slope != 0 && TileIdSets.SaveSlopes(tile.Type))
                    {
                        if (slope == 1) tile.HalfBrick = true;
                        else tile.Slope = (byte)(slope - 1);
                    }
                }

                if (b2 > 1)
                {
                    if ((b2 & 2) == 2) tile.Actuator = true;
                    if ((b2 & 4) == 4) tile.InActive = true;
                    if ((b2 & 0x20) == 0x20) tile.Wire4 = true;

                    if ((b2 & 0x40) == 0x40)
                    {
                        byte high = reader.ReadByte();
                        tile.Wall = (ushort)((high << 8) | tile.Wall);
                        if (tile.Wall >= WallIdCount) tile.Wall = 0;
                    }
                }

                if (b > 1)
                {
                    if ((b & 2) == 2) tile.InvisibleBlock = true;
                    if ((b & 4) == 4) tile.InvisibleWall = true;
                    if ((b & 8) == 8) tile.FullbrightBlock = true;
                    if ((b & 0x10) == 0x10) tile.FullbrightWall = true;
                }

                int run = (b4 & 0xC0) >> 6 switch
                {
                    0 => 0,
                    1 => reader.ReadByte(),
                    _ => reader.ReadInt16(),
                };

                while (run-- > 0)
                {
                    j++;
                    if (j < state.MaxTilesY)
                        tiles[i, j].CopyFrom(tile);
                }
            }
        }
    }

    // ---------------- Chests ----------------

    private static void LoadChests(BinaryReader reader, int version, WorldState state)
    {
        int count = reader.ReadInt16();
        int defaultMaxItems = version < 294 ? reader.ReadInt16() : 0;

        for (int i = 0; i < count; i++)
        {
            var chest = new Chest
            {
                Index = i,
                X = reader.ReadInt32(),
                Y = reader.ReadInt32(),
                Name = reader.ReadString(),
            };

            int maxItems = defaultMaxItems;
            if (version >= 294)
            {
                maxItems = reader.ReadInt32();
                if (maxItems < 0 || maxItems > 1000)
                    throw new InvalidDataException($"宝箱物品数量非法：{maxItems}");
            }

            var items = new ChestItem[Math.Max(0, maxItems)];
            for (int j = 0; j < items.Length; j++)
            {
                short stack = reader.ReadInt16();
                var item = new ChestItem { Stack = stack };
                if (stack > 0)
                {
                    item.Type = reader.ReadInt32();
                    item.Prefix = reader.ReadByte();
                }
                else if (stack < 0)
                {
                    item.Type = reader.ReadInt32();
                    item.Prefix = reader.ReadByte();
                    item.Stack = 1;
                }

                items[j] = item;
            }

            chest.Items = items;
            state.Chests.Add(chest);
        }

        DeduplicateChests(state);
    }

    private static void DeduplicateChests(WorldState state)
    {
        var seen = new HashSet<(int, int)>();
        for (int i = 0; i < state.Chests.Count; i++)
        {
            var c = state.Chests[i];
            if (seen.Add((c.X, c.Y))) continue;
            state.Chests[i] = null!; // 重复坐标：原版 RemoveChest
        }

        for (int i = state.Chests.Count - 1; i >= 0; i--)
        {
            if (state.Chests[i] is null) state.Chests.RemoveAt(i);
        }
    }

    // ---------------- Signs ----------------

    private static void LoadSigns(BinaryReader reader, WorldState state)
    {
        int count = reader.ReadInt16();

        for (int i = 0; i < count; i++)
        {
            string text = reader.ReadString();
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();

            bool valid = x >= 0 && y >= 0 && x < state.MaxTilesX && y < state.MaxTilesY
                         && state.Tiles[x, y].Active && TileIdSets.IsSign(state.Tiles[x, y].Type);

            if (!valid) continue;

            state.Signs.Add(new Sign { Index = i, X = x, Y = y, Text = text });
        }

        // 重复坐标去重
        var seen = new HashSet<(int, int)>();
        for (int i = state.Signs.Count - 1; i >= 0; i--)
        {
            if (!seen.Add((state.Signs[i].X, state.Signs[i].Y)))
                state.Signs.RemoveAt(i);
        }
    }

    // ---------------- NPCs ----------------

    private static void LoadNpcs(BinaryReader reader, int version, WorldState state)
    {
        if (version >= 268)
        {
            int shimmerCount = reader.ReadInt32();
            for (int i = 0; i < shimmerCount; i++) _ = reader.ReadInt32();
        }

        bool more = reader.ReadBoolean();
        while (more)
        {
            var npc = new WorldNpc { IsTownNpc = true };
            npc.Type = version >= 190 ? reader.ReadInt32() : ReadLegacyNpcType(reader);
            npc.GivenName = reader.ReadString();
            npc.X = reader.ReadSingle();
            npc.Y = reader.ReadSingle();
            npc.Homeless = reader.ReadBoolean();
            npc.HomeTileX = reader.ReadInt32();
            npc.HomeTileY = reader.ReadInt32();

            if (version >= 213 && (reader.ReadByte() & 1) != 0)
                _ = reader.ReadInt32(); // townNpcVariationIndex

            if (version >= 315)
                _ = reader.ReadBoolean(); // homelessDespawn

            state.Npcs.Add(npc);
            more = reader.ReadBoolean();
        }

        if (version >= 140)
        {
            more = reader.ReadBoolean();
            while (more)
            {
                var npc = new WorldNpc { IsTownNpc = false };
                npc.Type = version >= 190 ? reader.ReadInt32() : ReadLegacyNpcType(reader);
                npc.X = reader.ReadSingle();
                npc.Y = reader.ReadSingle();
                state.Npcs.Add(npc);
                more = reader.ReadBoolean();
            }
        }
    }

    private static int ReadLegacyNpcType(BinaryReader reader)
    {
        _ = reader.ReadString(); // NPCID.FromLegacyName（未移植，忽略）
        return 0;
    }

    // ---------------- Footer ----------------

    private static void LoadFooter(BinaryReader reader, WorldState state)
    {
        if (!reader.ReadBoolean())
            throw new InvalidDataException("世界文件 footer 标记非法");
        if (reader.ReadString() != state.WorldName)
            throw new InvalidDataException("世界文件 footer 世界名不匹配");
        if (reader.ReadInt32() != state.WorldId)
            throw new InvalidDataException("世界文件 footer WorldId 不匹配");
    }
}
