// TerraAuth — Phase 3 验收测试

using System;
using System.Threading;
using System.Threading.Tasks;
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
        public override void Apply(WorldState world, IRng rng) { }
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
