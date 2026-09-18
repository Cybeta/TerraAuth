// TerraAuth — 世界文件写出器（与 WorldFileReader 的布局严格对称）
// 用途：把服务端权威世界导出为 .wld（备份 / 迁移 / 换基准世界）。
// 工程要点（自研，非照搬）：
//   1. 先序列化到内存 → 写临时文件 → 用自身读取器读回校验 → 校验通过才原子替换目标文件；
//      校验失败保留原文件并抛出，避免写出「读不回来」的世界。
//   2. 替换前把旧文件滚动为 <path>.bak（回滚点）。
//   3. 分段指针（section positions）先占位、写完各段后回填，保证与读取器的位置断言一致。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TerraAuth.Simulation;

/// <summary>把 <see cref="WorldState"/> 序列化为 Terraria <c>.wld</c>（版本与读取器一致）。</summary>
public static class WorldFileWriter
{
    /// <summary>写出的世界文件版本（与读取器支持上限一致）。</summary>
    public const int WriteVersion = WorldFileReader.MaxSupportedVersion;

    /// <summary>分段数量：0..4 有内容，5..9 为空，10 为 footer（读取器按下标访问）。</summary>
    private const int SectionCount = 11;

    /// <summary>图格「重要度」位图长度：决定 FrameX/FrameY 是否随文件保存。</summary>
    public const int ImportanceCount = 1024;

    /// <summary>文件元数据魔数低 56 位（与读取器校验一致）。</summary>
    private const ulong MetadataMagicLow56 = 0x6369676F6C6572uL;

    /// <summary>元数据最高字节 = 文件类型；原版 <c>FileType.World</c> = 2。</summary>
    private const byte MetadataWorldFileType = 2;

    /// <summary>
    /// 写出世界文件：临时文件 → 读回校验 → 原子替换（旧文件滚动为 <c>.bak</c>）。
    /// </summary>
    public static void Write(string path, WorldState state, bool keepBackup = true)
    {
        byte[] payload = Serialize(state);

        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(dir);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, payload);

        // 写后校验：必须能被自身读取器完整读回（版本 / 分段指针 / footer 全部对得上）
        _ = WorldFileReader.Read(temp);

        if (keepBackup && File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>把世界序列化为 .wld 字节（不做 IO，便于测试与内存校验）。</summary>
    public static byte[] Serialize(WorldState state)
    {
        using var ms = new MemoryStream(capacity: 8 * 1024 * 1024);
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            Write(ms, w, state);
        }
        return ms.ToArray();
    }

    private static void Write(MemoryStream ms, BinaryWriter w, WorldState state)
    {
        w.Write(WriteVersion);

        // ---- 文件元数据（版本 >= 135 才有）：UInt64（低 56 位魔数 + 最高字节文件类型）+ 修订号 + 旗标 ----
        // 最高字节必须为 FileType.World(=2)，否则原版 FileMetadata.Read 会抛 "Found invalid file type."
        w.Write(MetadataMagicLow56 | ((ulong)MetadataWorldFileType << 56));
        w.Write(0u);      // Revision
        w.Write(0UL);     // Flags

        // ---- 分段指针（先占位，写完各段后回填）----
        w.Write((short)SectionCount);
        long positionsAt = ms.Position;
        for (int i = 0; i < SectionCount; i++) w.Write(0);

        // ---- 图格重要度位图 ----
        w.Write((ushort)ImportanceCount);
        for (int i = 0; i < ImportanceCount; i += 8)
        {
            byte bits = 0;
            for (int b = 0; b < 8; b++)
            {
                int type = i + b;
                if (type < ImportanceCount && TileIdSets.IsTileFrameImportant((ushort)type))
                    bits |= (byte)(1 << b);
            }
            w.Write(bits);
        }

        var positions = new int[SectionCount];

        positions[0] = (int)ms.Position;
        WriteHeader(w, state);

        positions[1] = (int)ms.Position;
        WriteTiles(w, state);

        positions[2] = (int)ms.Position;
        WriteChests(w, state);

        positions[3] = (int)ms.Position;
        WriteSigns(w, state);

        positions[4] = (int)ms.Position;
        WriteNpcs(w, state);

        // 段 5..9 必须写出合法编码（各段起始位置互不相同），否则原版会判定 BadSectionPointer。
        positions[5] = (int)ms.Position;
        WriteTileEntities(w, state);

        positions[6] = (int)ms.Position;
        w.Write(0);                     // 加权压力板：Int32 数量 0

        positions[7] = (int)ms.Position;
        w.Write(0);                     // 城镇房间管理：Int32 数量 0

        positions[8] = (int)ms.Position;
        w.Write(0);                     // 图鉴 · 击杀统计：Int32 数量 0
        w.Write(0);                     // 图鉴 · 目击统计：Int32 数量 0
        w.Write(0);                     // 图鉴 · 交谈统计：Int32 数量 0

        positions[9] = (int)ms.Position;
        w.Write(false);                 // 创造之力：终止标记（无条目）

        positions[10] = (int)ms.Position;
        WriteFooter(w, state);

        // ---- 回填分段指针 ----
        long end = ms.Position;
        ms.Position = positionsAt;
        for (int i = 0; i < SectionCount; i++) w.Write(positions[i]);
        ms.Position = end;
    }

    // ---------------- 段 0：世界头部（含世界旗标）----------------

    private static void WriteHeader(BinaryWriter w, WorldState state)
    {
        var p = state.Progress;

        w.Write(state.WorldName);
        w.Write(state.Seed);
        w.Write(state.WorldGeneratorVersion);
        w.Write(state.UniqueId.ToByteArray());

        w.Write(state.WorldId);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);     // left/right/top/bottomWorld

        w.Write(state.MaxTilesY);
        w.Write(state.MaxTilesX);

        // ---- 世界旗标（顺序与读取器严格对应）----
        w.Write(state.GameMode);
        w.Write(p.DrunkWorld);
        w.Write(p.GetGoodWorld);
        w.Write(p.TenthAnniversaryWorld);
        w.Write(p.DontStarveWorld);
        w.Write(p.NotTheBeesWorld);
        w.Write(p.RemixWorld);
        w.Write(p.NoTrapsWorld);
        w.Write(p.ZenithWorld);
        w.Write(p.SkyblockWorld);

        w.Write((long)0);   // CreationTime
        w.Write((long)0);   // LastPlayed

        w.Write(state.MoonType);
        for (int i = 0; i < 3; i++) w.Write(state.TreeX[i]);
        for (int i = 0; i < 4; i++) w.Write(state.TreeStyle[i]);
        for (int i = 0; i < 3; i++) w.Write(state.CaveBackX[i]);
        for (int i = 0; i < 4; i++) w.Write(state.CaveBackStyle[i]);
        w.Write(state.IceBackStyle);
        w.Write(state.JungleBackStyle);
        w.Write(state.HellBackStyle);

        w.Write(state.SpawnTileX);
        w.Write(state.SpawnTileY);
        w.Write(state.WorldSurface);
        w.Write(state.RockLayer);
        w.Write(state.Time);
        w.Write(state.DayTime);
        w.Write(state.MoonPhase);
        w.Write(state.BloodMoon);
        w.Write(state.Eclipse);
        w.Write(state.DungeonX);
        w.Write(state.DungeonY);
        w.Write(p.Crimson);

        w.Write(p.DownedBoss1);
        w.Write(p.DownedBoss2);
        w.Write(p.DownedBoss3);
        w.Write(p.DownedQueenBee);
        w.Write(p.DownedMechBoss1);
        w.Write(p.DownedMechBoss2);
        w.Write(p.DownedMechBoss3);
        w.Write(p.DownedMechBossAny);
        w.Write(p.DownedPlantBoss);
        w.Write(p.DownedGolemBoss);
        w.Write(p.DownedSlimeKing);

        w.Write(false);     // savedGoblin
        w.Write(false);     // savedWizard
        w.Write(false);     // savedMech
        w.Write(p.DownedGoblins);
        w.Write(p.DownedClown);
        w.Write(p.DownedFrost);
        w.Write(p.DownedPirates);

        w.Write(p.ShadowOrbSmashed);
        w.Write(false);     // spawnMeteor
        w.Write((byte)0);   // shadowOrbCount
        w.Write(0);         // altarCount
        w.Write(p.HardMode);
        w.Write(false);     // afterPartyOfDoom

        w.Write(state.InvasionDelay);
        w.Write(state.InvasionSize);
        w.Write(state.InvasionType);
        w.Write(state.InvasionX);
        w.Write(p.SlimeRain ? 1.0 : 0.0);
        w.Write(state.SundialCooldown);
        w.Write(state.Raining);
        w.Write(state.RainTime);
        w.Write(state.MaxRain);

        w.Write((int)state.OreTiers[4]);
        w.Write((int)state.OreTiers[5]);
        w.Write((int)state.OreTiers[6]);

        for (int i = 0; i < 8; i++) w.Write(state.Backgrounds[i]);
        w.Write(state.CloudBgActive);
        w.Write((short)state.NumClouds);    // 线格式为 Int16（内存中为 int）
        w.Write(state.WindSpeedTarget);

        w.Write(0);         // 渔夫任务名列表
        w.Write(false);     // savedAngler
        w.Write(0);         // anglerQuest
        w.Write(false);     // savedStylist
        w.Write(false);     // savedTaxCollector
        w.Write(false);     // savedGolfer

        w.Write(state.InvasionSizeStart);
        w.Write(0);         // _tempCultistDelay

        w.Write((short)0);  // 旗帜系统：Int32 列表
        w.Write((short)0);  // 旗帜系统：UInt16 列表

        w.Write(p.FastForwardTimeToDawn);
        w.Write(p.DownedFishron);
        w.Write(p.DownedMartians);
        w.Write(p.DownedAncientCultist);
        w.Write(p.DownedMoonlord);
        w.Write(p.DownedHalloweenKing);
        w.Write(p.DownedHalloweenTree);
        w.Write(p.DownedChristmasIceQueen);
        w.Write(p.DownedChristmasSantank);
        w.Write(p.DownedChristmasTree);

        w.Write(p.DownedTowerSolar);
        w.Write(p.DownedTowerVortex);
        w.Write(p.DownedTowerNebula);
        w.Write(p.DownedTowerStardust);
        w.Write(false);     // TowerActiveSolar
        w.Write(false);     // TowerActiveVortex
        w.Write(false);     // TowerActiveNebula
        w.Write(false);     // TowerActiveStardust
        w.Write(false);     // LunarApocalypseIsUp

        w.Write(false);     // _tempPartyManual
        w.Write(p.PartyIsUp);
        w.Write(0);         // _tempPartyCooldown
        w.Write(0);         // 派对参与者列表

        w.Write(p.SandstormHappening);
        w.Write(0);         // _tempSandstormTimeLeft
        w.Write(0f);        // _tempSandstormSeverity
        w.Write(state.SandstormIntensity);

        w.Write(false);     // savedBartender
        w.Write(p.Dd2DownedInvasionT1);
        w.Write(p.Dd2DownedInvasionT2);
        w.Write(p.Dd2DownedInvasionT3);

        w.Write(state.Backgrounds[8]);
        w.Write(state.Backgrounds[9]);
        w.Write(state.Backgrounds[10]);
        w.Write(state.Backgrounds[11]);
        w.Write(state.Backgrounds[12]);

        w.Write(p.CombatBookWasUsed);

        w.Write(0);         // _tempLanternNightCooldown
        w.Write(p.LanternsUp);
        w.Write(false);     // _tempLanternNightManual
        w.Write(false);     // _tempLanternNightNextNightIsGenuine

        w.Write(state.TreeTops.Length);
        for (int i = 0; i < state.TreeTops.Length; i++) w.Write(state.TreeTops[i]);

        w.Write(p.ForceHalloweenForToday);
        w.Write(p.ForceXMasForToday);

        w.Write((int)state.OreTiers[0]);
        w.Write((int)state.OreTiers[1]);
        w.Write((int)state.OreTiers[2]);
        w.Write((int)state.OreTiers[3]);

        w.Write(p.BoughtCat);
        w.Write(p.BoughtDog);
        w.Write(p.BoughtBunny);

        w.Write(p.DownedEmpressOfLight);
        w.Write(p.DownedQueenSlime);

        w.Write(p.DownedDeerclops);
        w.Write(p.UnlockedSlimeBlueSpawn);

        w.Write(false);     // unlockedMerchantSpawn
        w.Write(false);     // unlockedDemolitionistSpawn
        w.Write(false);     // unlockedPartyGirlSpawn
        w.Write(false);     // unlockedDyeTraderSpawn
        w.Write(p.UnlockedTruffleSpawn);
        w.Write(false);     // unlockedArmsDealerSpawn
        w.Write(false);     // unlockedNurseSpawn
        w.Write(false);     // unlockedPrincessSpawn

        w.Write(p.CombatBookVolumeTwoWasUsed);
        w.Write(p.PeddlersSatchelWasUsed);

        w.Write(p.UnlockedSlimeGreenSpawn);
        w.Write(p.UnlockedSlimeOldSpawn);
        w.Write(p.UnlockedSlimePurpleSpawn);
        w.Write(p.UnlockedSlimeRainbowSpawn);
        w.Write(p.UnlockedSlimeRedSpawn);
        w.Write(p.UnlockedSlimeYellowSpawn);
        w.Write(p.UnlockedSlimeCopperSpawn);

        w.Write(p.FastForwardTimeToDusk);
        w.Write(state.MoondialCooldown);

        w.Write(p.ForceHalloweenForever);
        w.Write(p.ForceXMasForever);
        w.Write(p.VampireSeed);
        w.Write(p.InfectedSeed);
        w.Write(0);         // _tempMeteorShowerCount
        w.Write(0);         // _tempCoinRain
        w.Write(p.TeamBasedSpawnsSeed);

        // 额外出生点：Byte 数量 + N×(Int16 X, Int16 Y)
        int spawnCount = Math.Min(state.ExtraSpawnPoints.Count, 255);
        w.Write((byte)spawnCount);
        for (int i = 0; i < spawnCount; i++)
        {
            w.Write(state.ExtraSpawnPoints[i].X);
            w.Write(state.ExtraSpawnPoints[i].Y);
        }

        w.Write(p.DualDungeonsSeed);
        w.Write(p.MoreLightningSeed);
        w.Write(p.NoLightningSeed);
        w.Write("");        // WorldGen.Manifest
    }

    // ---------------- 段 1：图格 ----------------

    private static void WriteTiles(BinaryWriter w, WorldState state)
    {
        for (int x = 0; x < state.MaxTilesX; x++)
        {
            for (int y = 0; y < state.MaxTilesY; y++)
            {
                WriteTile(w, state.Tiles[x, y]);
            }
        }
    }

    /// <summary>
    /// 单格编码。标志字节链与读取器严格对称：
    /// <c>b4</c> 主标志（bit0 = 是否有 <c>b3</c>）；<c>b3</c> bit0 = 是否有 <c>b2</c>；<c>b2</c> bit0 = 是否有 <c>b</c>。
    /// 不使用行程压缩（b4 的 bit6/bit7 置 0，逐格写出）。
    /// </summary>
    private static void WriteTile(BinaryWriter w, in Tile tile)
    {
        bool important = tile.Active && tile.Type < ImportanceCount && TileIdSets.IsTileFrameImportant(tile.Type);

        bool shimmer = tile.LiquidType == 3;
        bool lava = tile.LiquidType == 1;
        bool honey = tile.LiquidType == 2;
        bool hasLiquid = tile.Liquid > 0;

        // 油漆只在「方块 / 墙壁存在」时才随文件读写，否则标志位与负载不匹配会导致整段错位
        bool hasTileColor = tile.Active && tile.TileColor != 0;
        bool hasWall = tile.Wall != 0;
        bool hasWallColor = hasWall && tile.WallColor != 0;

        // 三级附加标志字节 b3 → b2 → b：每级 bit0 表示「下一级是否存在」，
        // 故 b2 存在与否必须连带决定 b，b3 存在与否必须连带决定 b2（否则读取器会多读 / 少读字节）。
        bool bFlags = tile.InvisibleBlock || tile.InvisibleWall || tile.FullbrightBlock || tile.FullbrightWall;
        bool b2Flags = tile.Actuator || tile.InActive || tile.Wire4 || shimmer
                       || hasTileColor || hasWallColor || tile.Wall > 255;

        byte slopeCode = 0;
        if (tile.HalfBrick) slopeCode = 1;
        else if (tile.Slope != 0) slopeCode = (byte)Math.Min(6, tile.Slope + 1);
        if (!TileIdSets.SaveSlopes(tile.Type)) slopeCode = 0;

        bool b3Flags = tile.Wire || tile.Wire2 || tile.Wire3 || slopeCode != 0;

        bool writeB = bFlags;
        bool writeB2 = b2Flags || writeB;
        bool writeB3 = b3Flags || writeB2;

        byte b3 = 0;
        if (writeB3)
        {
            if (tile.Wire) b3 |= 0x02;
            if (tile.Wire2) b3 |= 0x04;
            if (tile.Wire3) b3 |= 0x08;
            b3 |= (byte)(slopeCode << 4);
            if (writeB2) b3 |= 0x01;
        }

        byte b2 = 0;
        if (writeB2)
        {
            if (tile.Actuator) b2 |= 0x02;
            if (tile.InActive) b2 |= 0x04;
            if (hasTileColor) b2 |= 0x08;
            if (hasWallColor) b2 |= 0x10;
            if (tile.Wire4) b2 |= 0x20;
            if (tile.Wall > 255) b2 |= 0x40;
            if (shimmer) b2 |= 0x80;
            if (writeB) b2 |= 0x01;
        }

        byte b = 0;
        if (writeB)
        {
            b = 1;
            if (tile.InvisibleBlock) b |= 0x02;
            if (tile.InvisibleWall) b |= 0x04;
            if (tile.FullbrightBlock) b |= 0x08;
            if (tile.FullbrightWall) b |= 0x10;
        }

        // 线格式的液体类型编号：1 = 水 / 微光，2 = 岩浆，3 = 蜂蜜（微光另用 b2 bit7 标记）
        byte liquidCode = 0;
        if (hasLiquid) liquidCode = lava ? (byte)2 : honey ? (byte)3 : (byte)1;

        byte b4 = 0;
        if (writeB3) b4 |= 0x01;
        if (tile.Active) b4 |= 0x02;
        if (hasWall) b4 |= 0x04;
        if (tile.Type > 255) b4 |= 0x20;
        b4 |= (byte)(liquidCode << 3);

        w.Write(b4);
        if (writeB3) w.Write(b3);
        if (writeB2) w.Write(b2);
        if (writeB) w.Write(b);

        if (tile.Active)
        {
            if (tile.Type > 255)
            {
                w.Write((byte)(tile.Type & 0xFF));
                w.Write((byte)(tile.Type >> 8));
            }
            else
            {
                w.Write((byte)tile.Type);
            }

            if (important)
            {
                w.Write(tile.FrameX);
                w.Write(tile.FrameY);
            }

            if (hasTileColor) w.Write(tile.TileColor);
        }

        if (hasWall)
        {
            w.Write((byte)(tile.Wall & 0xFF));
            if (hasWallColor) w.Write(tile.WallColor);
        }

        if (hasLiquid) w.Write(tile.Liquid);

        if (tile.Wall > 255) w.Write((byte)(tile.Wall >> 8));
    }

    // ---------------- 段 2：箱子 ----------------

    private static void WriteChests(BinaryWriter w, WorldState state)
    {
        var chests = state.Chests.Where(static chest => !chest.Deleted).ToArray();
        w.Write((short)chests.Length);

        foreach (var chest in chests)
        {
            w.Write(chest.X);
            w.Write(chest.Y);
            w.Write(chest.Name ?? "");

            var items = chest.Items ?? Array.Empty<ChestItem>();
            w.Write(items.Length);
            foreach (var item in items)
            {
                if (item.Stack == 0 || item.Type == 0)
                {
                    w.Write((short)0);
                    continue;
                }

                w.Write(item.Stack);
                w.Write(item.Type);
                w.Write(item.Prefix);
            }
        }
    }

    // ---------------- 段 5：图格实体 ----------------

    private static void WriteTileEntities(BinaryWriter w, WorldState state)
    {
        var entities = state.SnapshotTileEntities();
        w.Write(entities.Count);
        foreach (var entity in entities)
        {
            if (entity.Type > 10)
                throw new InvalidDataException($"不支持的图格实体类型：{entity.Type}");

            w.Write(entity.Type);
            w.Write(entity.FileId);
            w.Write(entity.X);
            w.Write(entity.Y);
            switch (entity.Type)
            {
                case 0:
                    w.Write((short)entity.NpcSlot);
                    break;
                case 1:
                case 4:
                case 6:
                case 8:
                    WriteTileEntityItem(w, entity.Item);
                    break;
                case 2:
                    w.Write(entity.LogicCheck);
                    w.Write(entity.On);
                    break;
                case 3:
                    WriteDisplayDoll(w, entity);
                    break;
                case 5:
                    WriteHatRack(w, entity);
                    break;
                case 7:
                    break;
                case 9:
                case 10:
                    w.Write(entity.Item.Type);
                    break;
            }
        }
    }

    private static void WriteTileEntityItem(BinaryWriter w, TileEntityItem item)
    {
        w.Write(item.Type);
        w.Write(item.Prefix);
        w.Write(item.Stack);
    }

    private static void WriteDisplayDoll(BinaryWriter w, TileEntity entity)
    {
        byte itemBits = 0;
        byte dyeBits = 0;
        byte extraBits = 0;
        for (int i = 0; i < 8; i++)
        {
            if (!entity.DisplayDollItems[i].IsAir) itemBits |= (byte)(1 << i);
            if (!entity.DisplayDollDyes[i].IsAir) dyeBits |= (byte)(1 << i);
        }
        if (!entity.DisplayDollItems[8].IsAir) extraBits |= 2;
        if (!entity.DisplayDollDyes[8].IsAir) extraBits |= 4;
        if (!entity.DisplayDollMisc.IsAir) extraBits |= 1;

        w.Write(itemBits);
        w.Write(dyeBits);
        w.Write(entity.DisplayDollPose);
        w.Write(extraBits);
        for (int i = 0; i < 9; i++)
            if (i < 8 ? (itemBits & (1 << i)) != 0 : (extraBits & 2) != 0)
                WriteTileEntityItem(w, entity.DisplayDollItems[i]);
        for (int i = 0; i < 9; i++)
            if (i < 8 ? (dyeBits & (1 << i)) != 0 : (extraBits & 4) != 0)
                WriteTileEntityItem(w, entity.DisplayDollDyes[i]);
        if ((extraBits & 1) != 0) WriteTileEntityItem(w, entity.DisplayDollMisc);
    }

    private static void WriteHatRack(BinaryWriter w, TileEntity entity)
    {
        byte bits = 0;
        for (int i = 0; i < 2; i++)
        {
            if (!entity.HatRackHats[i].IsAir) bits |= (byte)(1 << i);
            if (!entity.HatRackDyes[i].IsAir) bits |= (byte)(1 << (i + 2));
        }
        w.Write(bits);
        for (int i = 0; i < 2; i++)
            if ((bits & (1 << i)) != 0) WriteTileEntityItem(w, entity.HatRackHats[i]);
        for (int i = 0; i < 2; i++)
            if ((bits & (1 << (i + 2))) != 0) WriteTileEntityItem(w, entity.HatRackDyes[i]);
    }

    // ---------------- 段 3：告示牌 ----------------

    private static void WriteSigns(BinaryWriter w, WorldState state)
    {
        w.Write((short)state.Signs.Count);
        foreach (var sign in state.Signs)
        {
            w.Write(sign.Text ?? "");
            w.Write(sign.X);
            w.Write(sign.Y);
        }
    }

    // ---------------- 段 4：NPC ----------------

    private static void WriteNpcs(BinaryWriter w, WorldState state)
    {
        var town = new List<WorldNpc>();
        var others = new List<WorldNpc>();
        foreach (var npc in state.Npcs)
        {
            if (!npc.Active) continue;   // 死亡槽位不落盘（whoAmI 原位保留，仅存活 NPC 导出）
            (npc.IsTownNpc ? town : others).Add(npc);
        }

        w.Write(0);     // 微光 NPC 列表（v >= 268）

        foreach (var npc in town)
        {
            w.Write(true);
            w.Write(npc.Type);
            w.Write(npc.GivenName ?? "");
            w.Write(npc.X);
            w.Write(npc.Y);
            w.Write(npc.Homeless);
            w.Write(npc.HomeTileX);
            w.Write(npc.HomeTileY);
            w.Write((byte)0);   // 变体索引标记（bit0 = 是否携带变体）
            w.Write(false);     // homelessDespawn（v >= 315）
        }
        w.Write(false);

        foreach (var npc in others)
        {
            w.Write(true);
            w.Write(npc.Type);
            w.Write(npc.X);
            w.Write(npc.Y);
        }
        w.Write(false);
    }

    // ---------------- 段 10：footer ----------------

    private static void WriteFooter(BinaryWriter w, WorldState state)
    {
        w.Write(true);
        w.Write(state.WorldName);
        w.Write(state.WorldId);
    }
}
