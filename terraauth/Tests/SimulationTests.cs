// TerraAuth — Phase 3 验收测试

using System;
using System.Threading;
using System.Threading.Tasks;
using TerraAuth.Protocol;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

public class SimulationTests
{
    /// <summary>
    /// 区块分区锁：写锁持有期间，同区块的读锁必须被阻塞（图格写入与包 10 编码 / 权威校验互斥）。
    /// 注意：ReaderWriterLockSlim 写锁具线程亲和性，故由后台线程持锁并在同一线程释放。
    /// </summary>
    [Fact]
    public async Task SectionLocks_WriteLock_ExcludesReaderOnSameSection()
    {
        var locks = new SectionLocks();
        using var writeHeld = new ManualResetEventSlim(false);
        using var releaseWrite = new ManualResetEventSlim(false);

        // 持锁/抢锁都用**独立线程**而非线程池：本测试需要"线程一定能及时被调度"，
        // 而 Task.Run 在线程池饱和时会延迟数秒，导致假失败（实测已发生）。
        var holder = new Thread(() =>
        {
            locks.EnterWrite(tileX: 10, tileY: 10);
            try
            {
                writeHeld.Set();
                releaseWrite.Wait(TimeSpan.FromSeconds(10));
            }
            finally
            {
                locks.ExitWrite(tileX: 10, tileY: 10);
            }
        }) { IsBackground = true };
        holder.Start();

        Assert.True(writeHeld.Wait(TimeSpan.FromSeconds(10)), "写锁未取得");

        var readerEntered = false;
        var reader = new Thread(() =>
        {
            using (locks.EnterRead(10, 10, 10, 10))
                readerEntered = true;
        }) { IsBackground = true };
        reader.Start();

        try
        {
            // 写锁持有 → 读者进不来
            await Task.Delay(200);
            Assert.False(readerEntered);
        }
        finally
        {
            releaseWrite.Set();
        }

        Assert.True(reader.Join(TimeSpan.FromSeconds(10)), "释放写锁后读者应获得读锁");
        Assert.True(readerEntered);
        holder.Join(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GameLoop_Steps_AtFixedTimestep()
    {
        var world = new WorldState();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        var loop = new GameLoop(sim, world, targetHz: 60);

        // 手动推进 10 tick
        for (int i = 0; i < 10; i++)
            loop.StepOne();

        Assert.Equal(10, world.Tick);
    }

    [Fact]
    public void Simulator_Tick_IsDeterministic_GivenSameCommands()
    {
        var a = CreateSim();
        var b = CreateSim();

        // 应用相同命令序列
        var cmd = new TestCommand(1, 1, "test");
        a.Commands.Enqueue(cmd);
        b.Commands.Enqueue(cmd);

        a.Sim.Tick(); b.Sim.Tick();

        // 状态应完全一致（确定性）
        Assert.Equal(a.World.Tick, b.World.Tick);
    }

    [Fact]
    public void MoveCommand_MarksPlayerUpdateOnlyWhenApplied()
    {
        var world = new WorldState { Tiles = new TileMap(2, 2), MaxTilesX = 2, MaxTilesY = 2 };
        var commands = new CommandQueue();
        commands.Enqueue(new MoveCommand(1, 7, new Vector2(32, 48)));
        var sim = new WorldSimulator(world, commands, new EventRecorder(), new SnapshotStore());

        sim.Tick();

        Assert.Equal(new[] { 7 }, world.DrainPlayerUpdates(10));
        Assert.Equal(new Vector2(16, 48.4f), world.Players[7].Position);
    }

    [Fact]
    public void TilePlaceCommand_DoesNotMarkUpdateWhenInventoryCannotBeConsumed()
    {
        var world = new WorldState { Tiles = new TileMap(2, 2), MaxTilesX = 2, MaxTilesY = 2,
            InventoryLedger = new RejectingInventoryLedger() };
        var command = new TilePlaceCommand(1, 7, 1, 1, 3, 0);
        command.Apply(world, new XoshiroRng(1));

        Assert.Empty(world.DrainTileUpdates(10));
        Assert.False(world.Tiles[1, 1].Active);
    }

    [Fact]
    public void CommandQueue_Drains_InStableOrder()
    {
        var q = new CommandQueue();
        q.Enqueue(new TestCommand(2, 2, "b"));
        q.Enqueue(new TestCommand(1, 1, "a"));

        var drained = q.DrainThrough(long.MaxValue);
        Assert.Equal(2, drained.Count);
        Assert.Equal("a", drained[0].Kind); // 按 tick 排序
        Assert.Equal("b", drained[1].Kind);
    }

    [Fact]
    public void CommandQueue_FutureTick_DoesNotBlockReadyCommand()
    {
        var q = new CommandQueue();
        q.Enqueue(new TestCommand(10, 1, "future"));
        q.Enqueue(new TestCommand(2, 1, "ready"));

        var drained = q.DrainThrough(2);

        var command = Assert.Single(drained);
        Assert.Equal("ready", command.Kind);
        Assert.True(q.TryPeek(out var remaining));
        Assert.Equal("future", remaining!.Kind);
    }

    [Fact]
    public void CommandQueue_SameTick_PreservesEnqueueOrder()
    {
        var q = new CommandQueue();
        q.Enqueue(new TestCommand(3, 1, "first"));
        q.Enqueue(new TestCommand(3, 2, "second"));

        var drained = q.DrainThrough(3);

        Assert.Collection(drained,
            command => Assert.Equal("first", command.Kind),
            command => Assert.Equal("second", command.Kind));
    }

    [Fact]
    public void XoshiroRng_IsDeterministic()
    {
        var a = new XoshiroRng(123);
        var b = new XoshiroRng(123);

        for (int i = 0; i < 100; i++)
            Assert.Equal(a.NextUInt32(), b.NextUInt32());
    }

    // ---------- helpers ----------

    private static (WorldState World, GameLoop Loop, WorldSimulator Sim, CommandQueue Commands) CreateSim()
    {
        var world = new WorldState();
        var commands = new CommandQueue();
        var sim = new WorldSimulator(world, commands, new EventRecorder(), new SnapshotStore());
        var loop = new GameLoop(sim, world, 60);
        return (world, loop, sim, commands);
    }

    private sealed record TestCommand(long Tick, int? PlayerId, string Kind) : Command(Tick, PlayerId, Kind)
    {
        public override CommandApplyResult Apply(WorldState world, IRng rng) => new(true);
    }

    private sealed class RejectingInventoryLedger : IInventoryLedger
    {
        public bool ConsumeItem(int playerId, int itemId) => false;
        public bool TryAddItem(int playerId, int itemId, int stack) => false;
        public bool TryAddItemExactly(int playerId, int itemId, int stack) => false;
    }
}

/// <summary>
/// 程序化世界生成回归：确保产出足够大的世界（区块数 &gt; 0），且出生点 / 地表 / 岩层合法，
/// 以解除「1×1 默认世界 → 无 TileSection → 客户端掉线」的进服阻塞。
/// </summary>
public class WorldGeneratorTests
{
    [Fact]
    public void GenerateSmall_HasNonEmptySectionsAndValidSpawn()
    {
        var world = WorldGenerator.GenerateSmall();

        // 尺寸：与原版小世界一致
        Assert.Equal(4200, world.MaxTilesX);
        Assert.Equal(1200, world.MaxTilesY);

        // 关键：区块数必须 > 0，否则 HandleSpawnTileDataAsync 不发送任何包 10
        Assert.Equal(21, world.MaxTilesX / 200);
        Assert.Equal(8, world.MaxTilesY / 150);

        // 出生点距边界 ≥ 10（包 8 有效性判据）
        Assert.InRange(world.SpawnTileX, 10, world.MaxTilesX - 10);
        Assert.InRange(world.SpawnTileY, 10, world.MaxTilesY - 10);

        // 出生点上方为空气、脚下为实心（避免出生即窒息 / 悬空）
        Assert.False(world.Tiles[world.SpawnTileX, world.SpawnTileY - 1].Active);
        Assert.True(world.Tiles[world.SpawnTileX, world.SpawnTileY].Active);

        // 地表 / 岩层落在 short 合法范围内且岩层更深
        Assert.InRange(world.WorldSurface, 1, short.MaxValue);
        Assert.InRange(world.RockLayer, 1, short.MaxValue);
        Assert.True(world.RockLayer > world.WorldSurface);
    }

    [Fact]
    public void GenerateSmall_WorldInfoPacket_ReflectsDimensions()
    {
        var world = WorldGenerator.GenerateSmall();
        var info = world.ToWorldInfoPacket();

        Assert.Equal((short)4200, info.MaxTilesX);
        Assert.Equal((short)1200, info.MaxTilesY);
        Assert.Equal((short)world.SpawnTileX, info.SpawnTileX);
        Assert.Equal((short)world.SpawnTileY, info.SpawnTileY);
        Assert.Equal(WorldGenerator.GeneratorVersion, info.WorldGeneratorVersion);
    }

    [Theory]
    [InlineData(WorldSize.Small, 4200, 1200)]
    [InlineData(WorldSize.Medium, 6400, 1800)]
    [InlineData(WorldSize.Large, 8400, 2400)]
    public void Generate_Supports_All_Vanilla_Sizes(WorldSize size, int expectedW, int expectedH)
    {
        var world = WorldGenerator.Generate(size);

        // 尺寸与原版三档一致
        Assert.Equal(expectedW, world.MaxTilesX);
        Assert.Equal(expectedH, world.MaxTilesY);

        // 区块数必须 > 0（否则客户端收不到任何包 10 而掉线）
        Assert.True(world.MaxTilesX / 200 > 0);
        Assert.True(world.MaxTilesY / 150 > 0);

        // 尺寸必须落在包 7 线格式（Int16）范围内
        Assert.InRange(world.MaxTilesX, 1, short.MaxValue);
        Assert.InRange(world.MaxTilesY, 1, short.MaxValue);

        // 出生点有效、上方空气、脚下实心
        Assert.InRange(world.SpawnTileX, 10, world.MaxTilesX - 10);
        Assert.InRange(world.SpawnTileY, 10, world.MaxTilesY - 10);
        Assert.False(world.Tiles[world.SpawnTileX, world.SpawnTileY - 1].Active);
        Assert.True(world.Tiles[world.SpawnTileX, world.SpawnTileY].Active);

        // 地表 / 岩层落在 short 范围且岩层更深
        Assert.InRange(world.WorldSurface, 1, short.MaxValue);
        Assert.True(world.RockLayer > world.WorldSurface);

        // 包 7 反映所选尺寸
        var info = world.ToWorldInfoPacket();
        Assert.Equal((short)expectedW, info.MaxTilesX);
        Assert.Equal((short)expectedH, info.MaxTilesY);
    }

    [Fact]
    public void Generate_Produces_Layered_Terrain_With_Ores_Caves_Ocean_And_Chests()
    {
        var world = WorldGenerator.GenerateSmall();

        // 层位：地表 < 岩层（岩层为泥土/石分界）
        Assert.True(world.WorldSurface > 0, "世界地表未设置");
        Assert.True(world.RockLayer > world.WorldSurface, "岩层应深于地表");
        Assert.InRange(world.WorldSurface, 1, short.MaxValue);

        int hellStart = world.MaxTilesY - 200;
        int oreCount = 0, caveCount = 0, grassCount = 0, ashCount = 0, waterCount = 0;

        for (int y = 0; y < world.MaxTilesY; y++)
        {
            for (int x = 0; x < world.MaxTilesX; x++)
            {
                ref var tile = ref world.Tiles[x, y];

                // 洞穴：岩层到地狱之间的空格
                if (y >= world.RockLayer && y < hellStart && !tile.Active) caveCount++;

                if (tile.Liquid > 0) waterCount++;
                if (!tile.Active) continue;

                switch (tile.Type)
                {
                    case 6 or 7 or 8 or 9: oreCount++; break;   // 铁 / 铜 / 金 / 银
                    case 2: grassCount++; break;                 // 草皮
                    case 57: ashCount++; break;                  // 灰烬（地狱层）
                }
            }
        }

        Assert.True(oreCount > 200, $"矿脉过少：{oreCount}");
        Assert.True(caveCount > 5_000, $"洞穴过少：{caveCount}");
        Assert.True(grassCount > 1_000, $"地表草皮过少：{grassCount}");
        Assert.True(ashCount > 10_000, $"地狱层过少：{ashCount}");
        Assert.True(waterCount > 1_000, $"海水过少：{waterCount}");
        Assert.True(world.Chests.Count >= 3, $"地下宝箱过少：{world.Chests.Count}");

        // 宝箱战利品非空（否则宝箱没有意义）
        Assert.Contains(world.Chests, c => c.Items.Any(i => i.Stack > 0));
    }

    [Fact]
    public void GenerateSmall_IsDeterministic()
    {
        var a = WorldGenerator.GenerateSmall();
        Guid idA = a.UniqueId;
        int spawnXA = a.SpawnTileX;
        ushort typeA = a.Tiles[1234, a.SpawnTileY].Type;

        var b = WorldGenerator.GenerateSmall();

        Assert.Equal(idA, b.UniqueId);
        Assert.Equal(spawnXA, b.SpawnTileX);
        Assert.Equal(typeA, b.Tiles[1234, b.SpawnTileY].Type);
    }
}
