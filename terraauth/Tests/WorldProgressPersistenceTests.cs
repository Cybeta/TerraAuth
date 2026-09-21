// 世界进度落库（差距 G8「重启即回档」）的测试：
// 1) WorldProgressCodec 的位序 / 字节形状 / 错误输入 / 就地解码
// 2) 持久化层 SaveWorldProgressAsync / LoadWorldProgressAsync 的往返、清空与按世界隔离

using System;
using System.IO;
using TerraAuth.Persistence;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

public class WorldProgressPersistenceTests
{
    /// <summary>规格位图分组容量：b0..b2 各 7 位、b3..b9 各 8 位、b10 为 5 位（合计 82 位）。</summary>
    private static readonly int[] GroupSizes = [7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 5];

    /// <summary>
    /// 82 个进度位，顺序**独立抄自规格**（不是从 codec 里读的），
    /// 这样「codec 换了位序」会在下面的逐位断言里暴露，而不是被同源的编码 / 解码互相掩盖。
    /// </summary>
    private static readonly (Func<WorldProgress, bool> Get, Action<WorldProgress, bool> Set)[] Fields =
    [
        // b0
        (p => p.ShadowOrbSmashed, (p, v) => p.ShadowOrbSmashed = v),
        (p => p.DownedBoss1, (p, v) => p.DownedBoss1 = v),
        (p => p.DownedBoss2, (p, v) => p.DownedBoss2 = v),
        (p => p.DownedBoss3, (p, v) => p.DownedBoss3 = v),
        (p => p.HardMode, (p, v) => p.HardMode = v),
        (p => p.DownedClown, (p, v) => p.DownedClown = v),
        (p => p.DownedPlantBoss, (p, v) => p.DownedPlantBoss = v),
        // b1
        (p => p.DownedMechBoss1, (p, v) => p.DownedMechBoss1 = v),
        (p => p.DownedMechBoss2, (p, v) => p.DownedMechBoss2 = v),
        (p => p.DownedMechBoss3, (p, v) => p.DownedMechBoss3 = v),
        (p => p.DownedMechBossAny, (p, v) => p.DownedMechBossAny = v),
        (p => p.Crimson, (p, v) => p.Crimson = v),
        (p => p.PumpkinMoon, (p, v) => p.PumpkinMoon = v),
        (p => p.SnowMoon, (p, v) => p.SnowMoon = v),
        // b2
        (p => p.FastForwardTimeToDawn, (p, v) => p.FastForwardTimeToDawn = v),
        (p => p.SlimeRain, (p, v) => p.SlimeRain = v),
        (p => p.DownedSlimeKing, (p, v) => p.DownedSlimeKing = v),
        (p => p.DownedQueenBee, (p, v) => p.DownedQueenBee = v),
        (p => p.DownedFishron, (p, v) => p.DownedFishron = v),
        (p => p.DownedMartians, (p, v) => p.DownedMartians = v),
        (p => p.DownedAncientCultist, (p, v) => p.DownedAncientCultist = v),
        // b3
        (p => p.DownedMoonlord, (p, v) => p.DownedMoonlord = v),
        (p => p.DownedHalloweenKing, (p, v) => p.DownedHalloweenKing = v),
        (p => p.DownedHalloweenTree, (p, v) => p.DownedHalloweenTree = v),
        (p => p.DownedChristmasIceQueen, (p, v) => p.DownedChristmasIceQueen = v),
        (p => p.DownedChristmasSantank, (p, v) => p.DownedChristmasSantank = v),
        (p => p.DownedChristmasTree, (p, v) => p.DownedChristmasTree = v),
        (p => p.DownedGolemBoss, (p, v) => p.DownedGolemBoss = v),
        (p => p.PartyIsUp, (p, v) => p.PartyIsUp = v),
        // b4
        (p => p.DownedPirates, (p, v) => p.DownedPirates = v),
        (p => p.DownedFrost, (p, v) => p.DownedFrost = v),
        (p => p.DownedGoblins, (p, v) => p.DownedGoblins = v),
        (p => p.SandstormHappening, (p, v) => p.SandstormHappening = v),
        (p => p.Dd2Ongoing, (p, v) => p.Dd2Ongoing = v),
        (p => p.Dd2DownedInvasionT1, (p, v) => p.Dd2DownedInvasionT1 = v),
        (p => p.Dd2DownedInvasionT2, (p, v) => p.Dd2DownedInvasionT2 = v),
        (p => p.Dd2DownedInvasionT3, (p, v) => p.Dd2DownedInvasionT3 = v),
        // b5
        (p => p.CombatBookWasUsed, (p, v) => p.CombatBookWasUsed = v),
        (p => p.LanternsUp, (p, v) => p.LanternsUp = v),
        (p => p.DownedTowerSolar, (p, v) => p.DownedTowerSolar = v),
        (p => p.DownedTowerVortex, (p, v) => p.DownedTowerVortex = v),
        (p => p.DownedTowerNebula, (p, v) => p.DownedTowerNebula = v),
        (p => p.DownedTowerStardust, (p, v) => p.DownedTowerStardust = v),
        (p => p.ForceHalloweenForToday, (p, v) => p.ForceHalloweenForToday = v),
        (p => p.ForceXMasForToday, (p, v) => p.ForceXMasForToday = v),
        // b6
        (p => p.BoughtCat, (p, v) => p.BoughtCat = v),
        (p => p.BoughtDog, (p, v) => p.BoughtDog = v),
        (p => p.BoughtBunny, (p, v) => p.BoughtBunny = v),
        (p => p.FreeCake, (p, v) => p.FreeCake = v),
        (p => p.DrunkWorld, (p, v) => p.DrunkWorld = v),
        (p => p.DownedEmpressOfLight, (p, v) => p.DownedEmpressOfLight = v),
        (p => p.DownedQueenSlime, (p, v) => p.DownedQueenSlime = v),
        (p => p.GetGoodWorld, (p, v) => p.GetGoodWorld = v),
        // b7
        (p => p.TenthAnniversaryWorld, (p, v) => p.TenthAnniversaryWorld = v),
        (p => p.DontStarveWorld, (p, v) => p.DontStarveWorld = v),
        (p => p.DownedDeerclops, (p, v) => p.DownedDeerclops = v),
        (p => p.NotTheBeesWorld, (p, v) => p.NotTheBeesWorld = v),
        (p => p.RemixWorld, (p, v) => p.RemixWorld = v),
        (p => p.UnlockedSlimeBlueSpawn, (p, v) => p.UnlockedSlimeBlueSpawn = v),
        (p => p.CombatBookVolumeTwoWasUsed, (p, v) => p.CombatBookVolumeTwoWasUsed = v),
        (p => p.PeddlersSatchelWasUsed, (p, v) => p.PeddlersSatchelWasUsed = v),
        // b8
        (p => p.UnlockedSlimeGreenSpawn, (p, v) => p.UnlockedSlimeGreenSpawn = v),
        (p => p.UnlockedSlimeOldSpawn, (p, v) => p.UnlockedSlimeOldSpawn = v),
        (p => p.UnlockedSlimePurpleSpawn, (p, v) => p.UnlockedSlimePurpleSpawn = v),
        (p => p.UnlockedSlimeRainbowSpawn, (p, v) => p.UnlockedSlimeRainbowSpawn = v),
        (p => p.UnlockedSlimeRedSpawn, (p, v) => p.UnlockedSlimeRedSpawn = v),
        (p => p.UnlockedSlimeYellowSpawn, (p, v) => p.UnlockedSlimeYellowSpawn = v),
        (p => p.UnlockedSlimeCopperSpawn, (p, v) => p.UnlockedSlimeCopperSpawn = v),
        (p => p.FastForwardTimeToDusk, (p, v) => p.FastForwardTimeToDusk = v),
        // b9
        (p => p.NoTrapsWorld, (p, v) => p.NoTrapsWorld = v),
        (p => p.ZenithWorld, (p, v) => p.ZenithWorld = v),
        (p => p.UnlockedTruffleSpawn, (p, v) => p.UnlockedTruffleSpawn = v),
        (p => p.VampireSeed, (p, v) => p.VampireSeed = v),
        (p => p.InfectedSeed, (p, v) => p.InfectedSeed = v),
        (p => p.TeamBasedSpawnsSeed, (p, v) => p.TeamBasedSpawnsSeed = v),
        (p => p.SkyblockWorld, (p, v) => p.SkyblockWorld = v),
        (p => p.DualDungeonsSeed, (p, v) => p.DualDungeonsSeed = v),
        // b10
        (p => p.SkyblockLowTiles, (p, v) => p.SkyblockLowTiles = v),
        (p => p.ForceHalloweenForever, (p, v) => p.ForceHalloweenForever = v),
        (p => p.ForceXMasForever, (p, v) => p.ForceXMasForever = v),
        (p => p.MoreLightningSeed, (p, v) => p.MoreLightningSeed = v),
        (p => p.NoLightningSeed, (p, v) => p.NoLightningSeed = v),
    ];

    /// <summary>由分组容量推出第 <paramref name="fieldIndex"/> 个字段落在位图的哪个字节哪一位。</summary>
    private static (int ByteIndex, int BitIndex) SlotOf(int fieldIndex)
    {
        int byteIndex = 0;
        foreach (var size in GroupSizes)
        {
            if (fieldIndex < size) return (byteIndex, fieldIndex);
            fieldIndex -= size;
            byteIndex++;
        }

        throw new InvalidOperationException($"字段下标越界：{fieldIndex}");
    }

    private static WorldProgress AllTrue()
    {
        var progress = new WorldProgress();
        foreach (var field in Fields) field.Set(progress, true);
        return progress;
    }

    // ========================================================================
    // 编解码
    // ========================================================================

    /// <summary>
    /// 逐个字段单独置 true → 编码后必须是「该字段所属字节的该位为 1、其余为 0」，
    /// 且解码回全新对象后 82 个字段全与源一致。位序错位（含字节内错位与跨字节错位）会在这里失败。
    /// </summary>
    [Fact]
    public void Encode_PlacesEachProgressBitInItsSpecifiedSlot_AndDecodesBack()
    {
        Assert.Equal(82, Fields.Length);              // 规格共 82 位（11 字节 = 88 位，末 6 位保留）
        Assert.Equal(82, GroupSizes.Sum());

        for (int i = 0; i < Fields.Length; i++)
        {
            var (byteIndex, bitIndex) = SlotOf(i);

            var source = new WorldProgress();
            Fields[i].Set(source, true);

            var data = WorldProgressCodec.Encode(source);
            for (int b = 0; b < 11; b++)
                Assert.Equal(b == byteIndex ? 1 << bitIndex : 0, data[1 + b]);

            var restored = new WorldProgress();
            WorldProgressCodec.Decode(data, restored);
            for (int f = 0; f < Fields.Length; f++)
                Assert.Equal(Fields[f].Get(source), Fields[f].Get(restored));
        }
    }

    /// <summary>全 true / 全 false 两个极值：编码字节形状 + 解码后逐字段一致。</summary>
    [Fact]
    public void Encode_Decode_AllTrueAndAllFalse()
    {
        var allTrue = AllTrue();
        var trueBytes = WorldProgressCodec.Encode(allTrue);
        Assert.Equal(0x7F, trueBytes[1]);   // b0 只有 7 位，bit7 不参与
        Assert.Equal(0xFF, trueBytes[10]);  // b9 满 8 位
        Assert.Equal(0x1F, trueBytes[11]);  // b10 只有 5 位，高 3 位保留为 0

        var restoredTrue = new WorldProgress();
        WorldProgressCodec.Decode(trueBytes, restoredTrue);
        foreach (var field in Fields) Assert.True(field.Get(restoredTrue));

        var falseBytes = WorldProgressCodec.Encode(new WorldProgress());
        for (int b = 1; b < WorldProgressCodec.EncodedLength; b++)
            Assert.Equal(0, falseBytes[b]);

        // 目标对象先全置 true：证明解码确实把位「清掉」了，而不是只置位
        var restoredFalse = AllTrue();
        WorldProgressCodec.Decode(falseBytes, restoredFalse);
        foreach (var field in Fields) Assert.False(field.Get(restoredFalse));
    }

    /// <summary>字节形状 + 抽样位（只置 HardMode 与 DownedMoonlord）。</summary>
    [Fact]
    public void Encode_Shape_IsVersionBytePlusElevenBytes()
    {
        var source = new WorldProgress { HardMode = true, DownedMoonlord = true };
        var data = WorldProgressCodec.Encode(source);

        Assert.Equal(12, data.Length);
        Assert.Equal(WorldProgressCodec.EncodedLength, data.Length);
        Assert.Equal(WorldProgressCodec.Version, data[0]);
        Assert.Equal(0x10, data[1]);   // b0 bit4 = HardMode
        Assert.Equal(0x00, data[2]);   // b1
        Assert.Equal(0x00, data[3]);   // b2
        Assert.Equal(0x01, data[4]);   // b3 bit0 = DownedMoonlord
    }

    /// <summary>空 / 长度不符 / 版本不符 → <see cref="InvalidDataException"/>（调用方按「无进度」处理）。</summary>
    [Fact]
    public void Decode_RejectsEmptyTruncatedAndUnknownVersion()
    {
        var progress = new WorldProgress();
        var valid = WorldProgressCodec.Encode(progress);

        Assert.Throws<InvalidDataException>(() => WorldProgressCodec.Decode([], progress));
        Assert.Throws<InvalidDataException>(() => WorldProgressCodec.Decode(new byte[11], progress));
        Assert.Throws<InvalidDataException>(() => WorldProgressCodec.Decode(new byte[13], progress));

        var unknownVersion = (byte[])valid.Clone();
        unknownVersion[0] = 99;
        Assert.Throws<InvalidDataException>(() => WorldProgressCodec.Decode(unknownVersion, progress));
    }

    /// <summary>保留位必须被忽略（不报错），保证追加字段后旧数据仍可读。</summary>
    [Fact]
    public void Decode_IgnoresReservedBits()
    {
        var data = new byte[WorldProgressCodec.EncodedLength];
        data[0] = WorldProgressCodec.Version;
        data[11] = 0xE0;   // b10 的高 3 位是保留位

        var progress = new WorldProgress();
        WorldProgressCodec.Decode(data, progress);

        foreach (var field in Fields) Assert.False(field.Get(progress));
    }

    /// <summary>就地解码：调用方持有的旧引用能看到新进度（GameHost 传的是 world.Progress 这个既有对象）。</summary>
    [Fact]
    public void Decode_WritesIntoTheGivenInstance()
    {
        var progress = new WorldProgress();
        var holder = progress;   // 模拟「已被包 7 打包 / 困难模式判定等子系统持有的引用」
        var data = WorldProgressCodec.Encode(new WorldProgress { HardMode = true, DownedBoss1 = true });

        WorldProgressCodec.Decode(data, progress);

        Assert.True(holder.HardMode);
        Assert.True(holder.DownedBoss1);
    }

    /// <summary>
    /// 不得混入包 7 的非进度字段：<c>SscEnabled</c>（包 7 flags[0] bit6）与 <c>CloudBgActive</c>（flags[1] bit4）
    /// 都不是世界进度，落库的 b0 bit6 必须为 0。
    /// </summary>
    [Fact]
    public void Encode_DoesNotBorrowPacketFlags_FromWorldState()
    {
        var world = new WorldState { SscEnabled = true, CloudBgActive = 5 };

        var data = WorldProgressCodec.Encode(world.Progress);

        Assert.Equal(0, data[1] & (1 << 6));   // b0 bit6：包 7 里是 SscEnabled，这里必须空着
        Assert.Equal(0, data[2] & (1 << 4));   // b1 bit4：包 7 里是 CloudBgActive，这里必须空着

        var restored = new WorldProgress();
        WorldProgressCodec.Decode(data, restored);
        foreach (var field in Fields) Assert.False(field.Get(restored));
    }

    // ========================================================================
    // 持久化（SqlitePersistence 公开入口；构造方式与 IntegrationTests 的世界改动用例一致）
    // ========================================================================

    [Fact]
    public async Task Persistence_WorldProgress_SurvivesReopen()
    {
        var dbPath = NewDbPath();
        try
        {
            var data = WorldProgressCodec.Encode(AllTrue());

            using (var db = new SqlitePersistence(dbPath, worldId: "world-a"))
            {
                await db.SaveWorldProgressAsync(new WorldProgressRecord(data));

                var loaded = await db.LoadWorldProgressAsync();
                Assert.NotNull(loaded);
                Assert.Equal(data, loaded!.Data);
                Assert.Equal("world-a", loaded.WorldId);   // 公开入口按自己的 WorldId 覆写作用域
            }

            // 重开（模拟重启）：进度必须已在库里
            using (var db = new SqlitePersistence(dbPath, worldId: "world-a"))
                Assert.Equal(data, (await db.LoadWorldProgressAsync())!.Data);
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    /// <summary>换图清空：<c>ClearWorldChangesAsync</c> 也要把进度清掉（否则新地图带着旧地图的 Boss 击杀记录）。</summary>
    [Fact]
    public async Task Persistence_ClearWorldChanges_DropsWorldProgress()
    {
        var dbPath = NewDbPath();
        try
        {
            var data = WorldProgressCodec.Encode(new WorldProgress { HardMode = true });

            using (var db = new SqlitePersistence(dbPath, worldId: "world-a"))
            {
                await db.SaveWorldProgressAsync(new WorldProgressRecord(data));
                await db.SaveTileChangesAsync(new[] { new WorldTileRecord(10, 20, new byte[] { 1, 2 }) });
                Assert.NotNull(await db.LoadWorldProgressAsync());

                await db.ClearWorldChangesAsync();

                Assert.Null(await db.LoadWorldProgressAsync());
                Assert.Empty(await db.LoadTileChangesAsync());
            }

            // 重开（模拟重启）：清空必须已落盘
            using (var db = new SqlitePersistence(dbPath, worldId: "world-a"))
                Assert.Null(await db.LoadWorldProgressAsync());
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    /// <summary>按世界隔离：worldB 读不到 worldA 的进度，worldB 的清空不影响 worldA。</summary>
    [Fact]
    public async Task Persistence_WorldScopes_IsolateWorldProgress()
    {
        var dbPath = NewDbPath();
        try
        {
            var progressA = WorldProgressCodec.Encode(new WorldProgress { HardMode = true });
            var progressB = WorldProgressCodec.Encode(new WorldProgress { Crimson = true });

            using (var worldA = new SqlitePersistence(dbPath, worldId: "world-a"))
                await worldA.SaveWorldProgressAsync(new WorldProgressRecord(progressA));

            using (var worldB = new SqlitePersistence(dbPath, worldId: "world-b"))
            {
                Assert.Null(await worldB.LoadWorldProgressAsync());   // 新地图没进度 → 按全新世界启动

                await worldB.SaveWorldProgressAsync(new WorldProgressRecord(progressB));
                Assert.Equal(progressB, (await worldB.LoadWorldProgressAsync())!.Data);

                await worldB.ClearWorldChangesAsync();
                Assert.Null(await worldB.LoadWorldProgressAsync());
            }

            using (var worldA = new SqlitePersistence(dbPath, worldId: "world-a"))
                Assert.Equal(progressA, (await worldA.LoadWorldProgressAsync())!.Data);
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    // ========================================================================
    // GameHost 级闭环（差距 G8 的验收路径）
    // ========================================================================

    /// <summary>
    /// 落盘 → 进程重启（重新 Bootstrap 同一个 db / 配置 → 同一 WorldId）→ 进度回放：
    /// 困难模式与已击败 Boss 必须还在（回放前是「按全新世界启动」，会全部为 false）。
    /// </summary>
    [Fact]
    public async Task GameHost_ReplaysPersistedWorldProgress_AfterRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"terraauth-g8-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "state.db");
        var configPath = Path.Combine(dir, "server.json");
        try
        {
            using (var host = GameHost.Bootstrap(dbPath, configPath, metricsPort: 0, port: 0))
            {
                host.Simulator.State.Progress.HardMode = true;
                host.Simulator.State.Progress.DownedMoonlord = true;
                await host.FlushWorldProgressAsync();
            }

            using (var host = GameHost.Bootstrap(dbPath, configPath, metricsPort: 0, port: 0))
            {
                var progress = host.Simulator.State.Progress;
                Assert.True(progress.HardMode);
                Assert.True(progress.DownedMoonlord);
                Assert.False(progress.DownedDeerclops);   // 只有真正落盘的位被点亮
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewDbPath()
        => Path.Combine(Path.GetTempPath(), $"terraauth-wp-{Guid.NewGuid():N}.db");

    private static void CleanupDb(string dbPath)
    {
        foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + ".tmp" })
            if (File.Exists(path)) File.Delete(path);
    }
}
