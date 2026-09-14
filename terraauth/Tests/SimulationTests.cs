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
    public void SessionBoundCommands_RejectStaleSession()
    {
        var world = new WorldState { Tiles = new TileMap(2, 2), MaxTilesX = 2, MaxTilesY = 2,
            InventoryLedger = new RejectingInventoryLedger() };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, SessionId = 22, Active = true };

        Assert.Equal("stale_session", new MoveCommand(1, 1, new Vector2(1, 1)) { SessionId = 11 }.Apply(world, new XoshiroRng(1)).Reason);
        Assert.Equal("stale_session", new PickupItemCommand(1, 1, 0) { SessionId = 11 }.Apply(world, new XoshiroRng(1)).Reason);
        Assert.Equal("stale_session", new SyncChestItemCommand(1, 1, 0, 0, 1, 0, 1) { SessionId = 11 }.Apply(world, new XoshiroRng(1)).Reason);
    }

    [Fact]
    public void SessionConditionedOfflineCleanup_DoesNotRemoveReplacement()
    {
        var world = new WorldState();
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, SessionId = 22, Active = true };

        world.MarkPlayerOffline(1, 11, "old", 10);

        Assert.True(world.TryGetCurrentPlayer(1, 22, out _));
        Assert.False(world.TryGetCurrentPlayer(1, 11, out _));
    }

    [Fact]
    public void ChestSession_RejectsStaleConnectionAndPreservesReplacement()
    {
        var world = new WorldState();
        world.OpenChestSession(1, 22, 3);
        world.OpenChestSession(1, 33, 4);

        Assert.False(world.HasChestSession(1, 22, 3));
        Assert.True(world.HasChestSession(1, 33, 4));

        world.CloseChestSession(1, 22);
        Assert.True(world.HasChestSession(1, 33, 4));
    }

    [Fact]
    public void StaleSessionOfflineCleanup_PreservesReplacementChestSession()
    {
        var world = new WorldState();
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, SessionId = 22, Active = true };
        world.OpenChestSession(1, 22, 3);

        // 旧连接（会话 11）的清理不得关闭复用同槽位的新连接的箱子会话
        world.MarkPlayerOffline(1, 11, "old", 10);

        Assert.True(world.TryGetCurrentPlayer(1, 22, out _));
        Assert.True(world.HasChestSession(1, 22, 3));
    }

    [Fact]
    public void MatchingSessionOfflineCleanup_ClosesOwnChestSession()
    {
        var world = new WorldState();
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, SessionId = 22, Active = true };
        world.OpenChestSession(1, 22, 3);

        world.MarkPlayerOffline(1, 22, "old", 10);

        Assert.False(world.TryGetCurrentPlayer(1, 22, out _));
        Assert.False(world.HasChestSession(1, 22, 3));
    }

    [Fact]
    public void SessionConditionedOfflineCleanup_RemovesMatchingPlayer()
    {
        var world = new WorldState();
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, SessionId = 22, Active = true };

        world.MarkPlayerOffline(1, 22, "old", 10);

        Assert.False(world.TryGetCurrentPlayer(1, 22, out _));
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
    public void ChestUpdates_CanBeRequeued_AfterBroadcastFailure()
    {
        var world = new WorldState();
        world.MarkChestChanged(3, 7);

        var firstAttempt = world.DrainChestUpdates(10);
        Assert.Contains((3, 7), firstAttempt);

        world.MarkChestChanged(3, 7);
        var retry = world.DrainChestUpdates(10);
        Assert.Contains((3, 7), retry);
    }

    [Fact]
    public void ChestUpdates_AreNotDrained_WhenLimitIsZero()
    {
        var world = new WorldState();
        world.MarkChestChanged(3, 7);

        Assert.Empty(world.DrainChestUpdates(0));
        Assert.Contains((3, 7), world.DrainChestUpdates(10));
    }

    [Fact]
    public void ClosedChestSession_RejectsAuthoritativeWrite()
    {
        var world = new WorldState
        {
            Tiles = new TileMap(2, 2),
            MaxTilesX = 2,
            MaxTilesY = 2,
        };
        lock (world.PlayersLock)
        {
            world.Players[1] = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Position = new Vector2(8, 8),
            };
        }
        world.Chests.Add(new Chest
        {
            Index = 0,
            X = 0,
            Y = 0,
            Items = new ChestItem[40],
        });

        var result = new SyncChestItemCommand(
            1, 1, 0, 2, 4, 0, 5).Apply(world, new XoshiroRng(1));

        Assert.False(result.Applied);
        Assert.Equal("chest_not_open", result.Reason);
        Assert.Equal(0, world.Chests[0].Items[2].Stack);
        Assert.Empty(world.DrainChestUpdates(10));
    }

    [Fact]
    public void SwitchingChestSession_InvalidatesPreviousChest()
    {
        var world = new WorldState();
        world.OpenChestSession(1, 3);
        world.OpenChestSession(1, 4);

        Assert.False(world.HasChestSession(1, 3));
        Assert.True(world.HasChestSession(1, 4));

        world.CloseChestSession(1);
        Assert.False(world.HasChestSession(1, 4));
    }

    [Fact]
    public void GoingOffline_ClosesChestSession()
    {
        var world = new WorldState();
        lock (world.PlayersLock)
        {
            world.Players[1] = new PlayerRuntime
            {
                Id = 1,
                Active = true,
            };
        }

        world.OpenChestSession(1, 3);
        world.MarkPlayerOffline(1, string.Empty, 0);

        Assert.False(world.HasChestSession(1, 3));
        Assert.False(world.Players.ContainsKey(1));
    }

    [Fact]
    public void CloseChestCommand_ClosesSession()
    {
        var world = new WorldState();
        world.OpenChestSession(1, 22, 3);

        var closed = new CloseChestCommand(1, 1) { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.True(closed.Applied);
        Assert.False(world.HasChestSession(1, 22, 3));
    }

    [Fact]
    public void StaleSessionCloseChestCommand_DoesNotCloseReplacementSession()
    {
        var world = new WorldState();
        world.OpenChestSession(1, 33, 4);

        // 旧连接（会话 22）的关箱请求不得关闭复用同槽位的新连接会话
        new CloseChestCommand(1, 1) { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.True(world.HasChestSession(1, 33, 4));
    }

    [Fact]
    public void ChestSession_ClosesOnlyAfterPlayerLeavesReach()
    {
        // 箱子位于图格 (0,0)（中心像素 8,8），玩家先站在箱子旁、再走远
        var world = new WorldState { Tiles = new TileMap(16, 16), MaxTilesX = 16, MaxTilesY = 16 };
        lock (world.PlayersLock)
        {
            world.Players[1] = new PlayerRuntime
            {
                Id = 1,
                SessionId = 7,
                Active = true,
                Position = new Vector2(8, 8),
            };
        }

        world.Chests.Add(new Chest { Index = 0, X = 0, Y = 0, Items = new ChestItem[40] });
        world.OpenChestSession(1, 7, 0);
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        sim.Tick();
        Assert.True(world.HasChestSession(1, 7, 0));   // 仍在交互距离内 → 会话保留

        lock (world.PlayersLock)
            world.Players[1].Position = new Vector2(8 + 160f * 2, 8);

        sim.Tick();
        Assert.False(world.HasChestSession(1, 7, 0));  // 离开距离 → 仿真关闭会话
    }

    [Fact]
    public void PickupItemCommand_FullInventory_KeepsItemAndDefersRemoval()
    {
        var world = new WorldState { InventoryLedger = new RejectingInventoryLedger() };
        lock (world.PlayersLock)
        {
            world.Players[1] = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Position = new Vector2(8, 8),
            };
        }
        lock (world.ItemsLock)
        {
            world.Items.Add(new WorldItemEntity
            {
                Slot = 0,
                ItemId = 1,
                Stack = 1,
                Position = new Vector2(8, 8),
            });
        }

        var result = new PickupItemCommand(1, 1, 0).Apply(world, new XoshiroRng(1));

        Assert.False(result.Applied);
        Assert.Equal("inventory_full", result.Reason);

        // 背包不足 → 掉落物必须保留，且不得进入「已通知移除」状态（否则客户端会看到物品凭空消失）
        var item = Assert.Single(world.Items);
        Assert.True(item.Active);
        Assert.False(item.RemovalNotified);
    }

    [Fact]
    public void PickupItemCommand_OutOfReach_DoesNotQueueRemoval()
    {
        var world = new WorldState { InventoryLedger = new AcceptingInventoryLedger() };
        lock (world.PlayersLock)
        {
            world.Players[1] = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Position = new Vector2(8, 8),
            };
        }
        lock (world.ItemsLock)
        {
            world.Items.Add(new WorldItemEntity
            {
                Slot = 0,
                ItemId = 1,
                Stack = 1,
                Position = new Vector2(8 + 160f * 4, 8),
            });
        }

        var result = new PickupItemCommand(1, 1, 0).Apply(world, new XoshiroRng(1));

        Assert.Equal("out_of_reach", result.Reason);
        Assert.True(Assert.Single(world.Items).Active);
    }

    [Fact]
    public void PickupItemCommand_ConcurrentPickups_OnlyOneSucceeds()
    {
        var world = new WorldState { InventoryLedger = new AcceptingInventoryLedger() };
        lock (world.PlayersLock)
        {
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(8, 8) };
            world.Players[2] = new PlayerRuntime { Id = 2, Active = true, Position = new Vector2(8, 8) };
        }
        lock (world.ItemsLock)
        {
            world.Items.Add(new WorldItemEntity
            {
                Slot = 0,
                ItemId = 1,
                Stack = 1,
                Position = new Vector2(8, 8),
            });
        }

        // 两个玩家同时抢同一掉落物：掉落物实体的「失效 + 入库」必须在同一把锁内原子完成
        int succeeded = 0;
        Parallel.For(1, 3, playerId =>
        {
            var result = new PickupItemCommand(1, playerId, 0).Apply(world, new XoshiroRng(1));
            if (result.Applied) Interlocked.Increment(ref succeeded);
        });

        Assert.Equal(1, succeeded);
        Assert.False(Assert.Single(world.Items).Active);
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

    /// <summary>背包一律接受（用于并发拾取等只关心原子性的用例）。</summary>
    private sealed class AcceptingInventoryLedger : IInventoryLedger
    {
        public bool ConsumeItem(int playerId, int itemId) => true;
        public bool TryAddItem(int playerId, int itemId, int stack) => true;
        public bool TryAddItemExactly(int playerId, int itemId, int stack) => true;
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

    /// <summary>
    /// 城镇 NPC（向导）：按原版 AI_007_TownEntities 的地面运动核心行走 ——
    /// 速度上限 1f、加速度 0.07f、家附近每 tick 有 1/80 概率掉头（故方向不频繁翻转），
    /// 且不会跑到离家 25 格之外（原版 leash）。
    /// </summary>
    [Fact]
    public void TownNpc_WalksSmoothly_WithoutPerTickDirectionFlip()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        WorldNpc guide;
        lock (world.NpcsLock) guide = world.Npcs.Single(n => n.IsTownNpc);

        float homeX = (guide.HomeTileX + 0.5f) * 16f;
        float leash = 25f * 16f;
        float startX = guide.X;

        int flips = 0;
        float lastSign = 0f;
        for (int i = 0; i < 240; i++)
        {
            float before = guide.X;
            sim.Tick();
            float delta = guide.X - before;

            Assert.InRange(guide.X, homeX - leash - 1f, homeX + leash + 1f);   // 不离家 25 格以上
            Assert.True(MathF.Abs(delta) <= 1.1f, $"单 tick 位移异常：{delta}");  // 原版走速上限 1f（+ 加速度）

            if (MathF.Abs(delta) > 0.0001f)
            {
                float sign = MathF.Sign(delta);
                if (lastSign != 0f && sign != lastSign) flips++;
                lastSign = sign;
            }
        }

        Assert.NotEqual(startX, guide.X);   // 确实在走动
        Assert.True(flips <= 10, $"方向翻转 {flips} 次（原版为「家附近随机掉头」，不应逐 tick 抖动）");
    }

    /// <summary>
    /// 向导生成时应**脚底贴地表上沿**。原版约定 <c>position</c> = 碰撞盒左上角、脚底 = <c>position.Y + height</c>，
    /// 向导碰撞盒为 18×40（原版 <c>NPC.SetDefaults</c>），故 Y = 地表图格上沿 − 40。
    /// 早期实现按「21 高的盒子」写（且用 (spawnGroundY − 2) × 16），与客户端尺寸不符 → 贴图陷地 + 抖动。
    /// </summary>
    [Fact]
    public void Guide_Spawns_WithFeet_On_Ground()
    {
        var world = WorldGenerator.GenerateSmall();

        WorldNpc guide;
        lock (world.NpcsLock) guide = world.Npcs.Single(n => n.IsTownNpc);

        var (guideW, guideH) = NpcSizes.Of(22);
        Assert.Equal((18, 40), (guideW, guideH));   // 原版向导尺寸（NPC.SetDefaults type 22）

        float groundTop = world.SpawnTileY * 16f;
        Assert.Equal(groundTop, guide.Y + guideH, 3);   // 脚底正好压在地表上沿
    }

    /// <summary>
    /// 向导在平坦地面上步行走动，不应不停起跳。
    /// 障碍探测必须看**身体所在行**（支撑行上方），而不是脚下的支撑行 ——
    /// 早期实现探支撑行，平地上「前方本来就有地面」→ 每 tick 都判成障碍 → 向导一直跳。
    /// </summary>
    [Fact]
    public void Guide_DoesNotJump_OnFlatGround()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        WorldNpc guide;
        lock (world.NpcsLock) guide = world.Npcs.Single(n => n.IsTownNpc);

        // 出生点周围是被压平的，向导起点 +2 格、走速上限 1px/tick → 120 tick 内仍在平地上
        int jumps = 0;
        for (int i = 0; i < 120; i++)
        {
            sim.Tick();
            if (guide.VelocityY < 0f) jumps++;
        }

        Assert.True(jumps <= 1, $"平地 120 tick 内起跳 {jumps} 次（应为 0）");
    }

    /// <summary>
    /// NPC 物理必须用原版常数：<c>gravity = 0.3</c>、<c>maxFallSpeed = 10</c>
    /// （原版 <c>NPC.UpdateNPC_UpdateGravity</c>）。用别的值（早期为 0.4 / 16）会让服务端的跳跃 /
    /// 下落弧线与客户端按原版算出的位置持续发散 —— 客户端把 NPC 画在自己算出的位置（`netOffset` 平滑），
    /// 而**碰撞判定用服务端位置** → 玩家看到「史莱姆没碰到我却在扣血」。
    /// </summary>
    [Fact]
    public void Enemy_Fall_Uses_Vanilla_Gravity_And_TerminalSpeed()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        WorldNpc slime;
        lock (world.NpcsLock)
        {
            slime = new WorldNpc
            {
                Type = 1,
                NetId = 1,
                AiStyle = 1,
                Active = true,
                Life = 25,
                LifeMax = 25,
                X = world.SpawnTileX * 16f,
                Y = 4 * 16f,        // 高空：60 tick 内不会落地
                VelocityX = 0f,
                VelocityY = 0f,
            };
            world.Npcs.Add(slime);
        }

        sim.Tick();
        Assert.Equal(0.3f, slime.VelocityY, 4);   // 每 tick 加 0.3（原版 NPC 重力）

        for (int i = 0; i < 60; i++) sim.Tick();
        Assert.Equal(10f, slime.VelocityY, 4);    // 终端速度 10（原版 maxFallSpeed）
    }

    /// <summary>
    /// 接触判定对齐原版 <c>Player.Update_NPCCollision</c>：取整后 AABB 求交、**不设最小重叠** ——
    /// 两轴各重叠 1px 即命中，边缘相切（重叠 0px）不命中。
    /// 早期版本用 8px 最小重叠去「吸收位置偏差」，结果服务端判定比原版严格、与客户端不一致
    /// （客户端按原版 0px 判定并发包 117，服务端却漏判自己那一次）；位置偏差要靠对齐位置解决，而不是放大阈值。
    /// </summary>
    [Fact]
    public void ContactDamage_Aligns_With_Vanilla_Intersect()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        float groundTop = world.SpawnTileY * 16f;
        float px = world.SpawnTileX * 16f + 8f;
        float py = groundTop - NpcSizes.PlayerHeight;          // 玩家站姿：脚底贴地表上沿

        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Hp = 100,
            HpMax = 100,
            Position = new Vector2(px, py),
        };
        lock (world.PlayersLock) world.Players[1] = player;

        WorldNpc npc;
        lock (world.NpcsLock)
        {
            npc = new WorldNpc
            {
                // 用「城镇 NPC 的 aiStyle 7 + IsTownNpc=false」构造一个**水平完全静止**的敌人：
                // aiStyle 7 在未落地时直接返回、不产生水平速度，避免 AI 位移干扰边界判定。
                Type = 22,
                NetId = 22,
                AiStyle = 7,
                IsTownNpc = false,
                Active = true,
                Life = 25,
                LifeMax = 25,
                X = px + NpcSizes.PlayerWidth,   // 玩家右边缘 == NPC 左边缘（重叠 0px）
                Y = py,                          // 悬空（脚底未落地）→ aiStyle 7 不做水平移动
            };
            world.Npcs.Add(npc);
        }

        // 边缘相切（水平重叠 0px）→ 不结算
        for (int i = 0; i < 5; i++)
        {
            lock (world.NpcsLock)
            {
                npc.X = px + NpcSizes.PlayerWidth;
                npc.Y = py;
                npc.VelocityX = 0f;
                npc.VelocityY = 0f;
            }
            sim.Tick();
        }
        Assert.Equal(100, player.Hp);

        // 压进 1px（原版判定即为接触）→ 必须结算
        bool damaged = false;
        for (int i = 0; i < 10 && !damaged; i++)
        {
            lock (world.NpcsLock)
            {
                npc.X = px + NpcSizes.PlayerWidth - 1f;
                npc.Y = py;
                npc.VelocityX = 0f;
                npc.VelocityY = 0f;
            }
            sim.Tick();
            damaged = player.Hp < 100;
        }
        Assert.True(damaged, "1px 重叠（原版判定为接触）未被结算");
    }

    /// <summary>
    /// NPC 水平阻挡：前方是实心格时必须停下，不得"穿墙"。
    /// 早期物理只做垂直落地、水平完全不阻挡 → 服务端 NPC 会穿过地形直线前进，
    /// 而客户端有完整碰撞 → 两边位置持续发散（表现为「史莱姆一直朝某个方向走」「看着离得很远却在掉血」）。
    /// </summary>
    [Fact]
    public void Enemy_Stops_At_Wall()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        int groundRow = world.SpawnTileY;
        int wallCol = world.SpawnTileX + 3;

        // 造一根 5 格高的土墙（把该列地表以上填实）
        for (int y = groundRow - 4; y < groundRow; y++)
        {
            ref var tile = ref world.Tiles[wallCol, y];
            tile.Active = true;
            tile.Type = 0;   // Dirt
        }

        var (w, h) = NpcSizes.Of(1);
        WorldNpc slime;
        lock (world.NpcsLock)
        {
            slime = new WorldNpc
            {
                Type = 1,
                NetId = 1,
                AiStyle = 1,
                Active = true,
                Life = 25,
                LifeMax = 25,
                X = wallCol * 16f - w - 4f,     // 右边缘距墙 4px
                Y = groundRow * 16f - h,        // 脚底贴地表
                VelocityX = 3f,                 // 朝右冲
            };
            world.Npcs.Add(slime);
        }

        for (int i = 0; i < 20; i++) sim.Tick();

        Assert.True(slime.X + w <= wallCol * 16f + 0.5f,
            $"史莱姆穿过了墙：右边缘 {slime.X + w} > 墙左边界 {wallCol * 16f}");
    }

    /// <summary>
    /// 客户端上报的包 117 走**区间校验 + 权威扣血**闭环（服务端权威结算玩家受击）：
    /// 未接触敌怪 → 拒绝（防伪造远程受伤）；接触但上报值超上界 → 拒绝；
    /// 区间内 → 按上报值扣血并置免伤帧（客户端显示 = 服务端扣血，完全一致）。
    /// </summary>
    [Fact]
    public void ClientReportedDamage_Is_Validated_And_Applied_Authoritatively()
    {
        var world = WorldGenerator.GenerateSmall();
        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Hp = 100,
            HpMax = 100,
            Position = new Vector2(320f, 460f),
            AimPosition = new Vector2(320f, 460f),
            DeathNotified = true,
        };
        lock (world.PlayersLock) world.Players[1] = player;

        var rng = new XoshiroRng(1);

        // 1) 未接触敌怪 → 拒绝（防伪造远程受伤），生命 / 受击状态不变
        var noContact = new DamagePlayerCommand(1, 1, 10).Apply(world, rng);
        Assert.False(noContact.Applied);
        Assert.Equal(CommandFailures.NotApplied, noContact.Reason);
        Assert.Equal(100, player.Hp);

        // 2) 放置一只史莱姆钉在玩家碰撞盒中心（接触伤害 7，防御 0 → 上界 ceil(7×1.15) = 9）
        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 25,
            LifeMax = 25,
            X = player.Position.X,
            Y = player.Position.Y + 21f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);

        // 3) 区间内（上报 7）→ 按上报值扣血 + 置免伤帧
        Assert.True(new DamagePlayerCommand(2, 1, 7).Apply(world, rng).Applied);
        Assert.Equal(93, player.Hp);
        Assert.Equal(PlayerRuntime.GeneralImmunityTicks(7), player.HurtCooldown);
        Assert.False(player.Dead);

        // 4) 超上界（上报 10 > 9）→ 拒绝，生命 / 免伤帧不变
        var above = new DamagePlayerCommand(3, 1, 10).Apply(world, rng);
        Assert.False(above.Applied);
        Assert.Equal(CommandFailures.HurtDamageAboveLimit, above.Reason);
        Assert.Equal(93, player.Hp);
        Assert.Equal(PlayerRuntime.GeneralImmunityTicks(7), player.HurtCooldown);
    }

    /// <summary>
    /// 原版 <c>Main.CalculateDamagePlayersTake</c> 按难度取分支（阶段 D）：
    /// 经典 <c>dmg − def×0.5</c>、专家 <c>dmg×2 − def×0.75</c>、大师 <c>dmg×3 − def</c>，最低 1。
    /// </summary>
    [Theory]
    [InlineData(GameMode.Classic, 7, 2, 6)]     // 7 − round(2×0.5)=1 → 6
    [InlineData(GameMode.Expert, 7, 2, 12)]     // 14 − round(2×0.75)=2 → 12
    [InlineData(GameMode.Master, 7, 2, 19)]     // 21 − 2 → 19
    [InlineData(GameMode.Classic, 1, 10, 1)]    // 下限 1
    [InlineData(GameMode.Expert, 1, 10, 1)]     // 2 − round(7.5)=8 → 下限 1
    [InlineData(GameMode.Master, 0, 0, 1)]      // 0 → 下限 1
    public void CalculateDamagePlayersTake_Applies_Mode_Formula(GameMode mode, int damage, int defense, int expected)
        => Assert.Equal(expected, CombatResolver.CalculateDamagePlayersTake(damage, defense, mode));

    /// <summary>
    /// 阶段 D：专家模式下包 117 区间上界按难度放大（原版 1.4 起难度倍率内置于
    /// <c>CalculateDamagePlayersTake</c>，不再单独放大 <c>npc.damage</c>）。
    /// 史莱姆接触 7、玩家防御 0 → 专家上界 = ceil(7×1.15)=9 × 2 − 0 = 18（经典为 9）。
    /// </summary>
    [Fact]
    public void ExpertMode_Contact_UpperBound_Scales_With_Difficulty()
    {
        var world = WorldGenerator.GenerateSmall();
        world.GameMode = (int)GameMode.Expert;

        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Hp = 100,
            HpMax = 100,
            Position = new Vector2(320f, 460f),
            AimPosition = new Vector2(320f, 460f),
            DeathNotified = true,
        };
        lock (world.PlayersLock) world.Players[1] = player;

        var rng = new XoshiroRng(1);

        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 25,
            LifeMax = 25,
            X = player.Position.X,
            Y = player.Position.Y + 21f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);

        // 区间内（专家上界 18）→ 接受并按上报值扣血：100 − 18 = 82
        Assert.True(new DamagePlayerCommand(2, 1, 18).Apply(world, rng).Applied);
        Assert.Equal(82, player.Hp);

        // 超上界（19 > 18）→ 拒绝（hurt_damage_above_limit），生命不变
        var above = new DamagePlayerCommand(3, 1, 19).Apply(world, rng);
        Assert.False(above.Applied);
        Assert.Equal(CommandFailures.HurtDamageAboveLimit, above.Reason);
        Assert.Equal(82, player.Hp);
    }

    /// <summary>
    /// 阶段 D 第二部分：物品栏装备（包 5 → SetInventorySlotCommand）回填 <see cref="PlayerRuntime.Defense"/>，
    /// 包 117 区间上界按真实装备减防（原版 CalculateDamagePlayersTake）。
    /// 铜套（79/80/81 = 1/3/2）→ 防御 6；史莱姆接触 7 → 上界 = ceil(7×1.15)=9 − round(6×0.5)=3 → 6。
    /// </summary>
    [Fact]
    public void EquippedArmor_Feeds_Defense_Into_117_UpperBound()
    {
        var world = WorldGenerator.GenerateSmall();
        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Hp = 100,
            HpMax = 100,
            Position = new Vector2(320f, 460f),
            AimPosition = new Vector2(320f, 460f),
            DeathNotified = true,
        };
        lock (world.PlayersLock) world.Players[1] = player;

        var rng = new XoshiroRng(1);

        // 穿铜套：头盔 79(1) + 链甲 80(3) + 护腿 81(2) → 防御 6
        Assert.True(new SetInventorySlotCommand(1, 1, 0, 79, 1).Apply(world, rng).Applied);
        Assert.True(new SetInventorySlotCommand(2, 1, 1, 80, 1).Apply(world, rng).Applied);
        Assert.True(new SetInventorySlotCommand(3, 1, 2, 81, 1).Apply(world, rng).Applied);
        Assert.Equal(6, player.Defense);
        Assert.Equal(79, player.Items[0]);
        Assert.Equal(80, player.Items[1]);
        Assert.Equal(81, player.Items[2]);

        // 放置一只史莱姆钉在玩家碰撞盒中心（接触伤害 7）
        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 25,
            LifeMax = 25,
            X = player.Position.X,
            Y = player.Position.Y + 21f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);

        // 区间内（上报 6 = 上界）→ 接受并按上报值扣血：100 − 6 = 94
        Assert.True(new DamagePlayerCommand(4, 1, 6).Apply(world, rng).Applied);
        Assert.Equal(94, player.Hp);

        // 超上界（7 > 6，裸装上界为 9）→ 拒绝（防伪造伤害），生命不变
        var above = new DamagePlayerCommand(5, 1, 7).Apply(world, rng);
        Assert.False(above.Applied);
        Assert.Equal(CommandFailures.HurtDamageAboveLimit, above.Reason);
        Assert.Equal(94, player.Hp);

        // 脱头盔（空槽清空语义）→ 防御降为 5（3+2）
        Assert.True(new SetInventorySlotCommand(6, 1, 0, 0, 0).Apply(world, rng).Applied);
        Assert.Equal(5, player.Defense);
        Assert.Equal(0, player.Items[0]);
    }

    /// <summary>
    /// 阶段 D 第三部分：NPC 防御减伤在包 28 结算时应用（原版服务端收包后
    /// <c>CalculateDamageNPCsTake(dmg, def) × (crit ? 2 : 1)</c>——先减伤、再暴击）。
    /// 哥布林（type=26，防御 4）：上报 10 → 10 − round(4×0.5)=2 → 8；暴击 20 → 8×2=16。
    /// 防御 0 的史莱姆按上报值原样扣减（既有行为不回归）。
    /// </summary>
    [Fact]
    public void NpcStrike_Applies_Defense_Reduction()
    {
        var world = WorldGenerator.GenerateSmall();
        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Hp = 100,
            HpMax = 100,
            Position = new Vector2(320f, 460f),
            AimPosition = new Vector2(320f, 460f),
            DeathNotified = true,
        };
        lock (world.PlayersLock) world.Players[1] = player;

        var rng = new XoshiroRng(1);

        // 哥布林（type=26，防御 4，生命 60）——手动构造不走 AddNpc，需显式给防御
        var goblin = new WorldNpc
        {
            Type = 26,
            NetId = 26,
            Active = true,
            Life = 60,
            LifeMax = 60,
            Defense = 4,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(goblin);
        int index = world.Npcs.IndexOf(goblin);

        // 上报 10 → 减防御 2 → 8；60 − 8 = 52
        Assert.True(new NpcStrikeCommand(1, 1, index, 10, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(52, goblin.Life);

        // 暴击：上报 20 → 减防御 2 → 18 × 2 = 36；52 − 36 = 16
        Assert.True(new NpcStrikeCommand(2, 1, index, 20, Generation: 3, Crit: true).Apply(world, rng).Applied);
        Assert.Equal(16, goblin.Life);

        // 防御为 0 的史莱姆：上报值原样扣减（不受减伤影响）
        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 25,
            LifeMax = 25,
            Generation = 7,
            X = 400f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);
        int sIndex = world.Npcs.IndexOf(slime);

        Assert.True(new NpcStrikeCommand(3, 1, sIndex, 10, Generation: 7).Apply(world, rng).Applied);
        Assert.Equal(15, slime.Life);
    }

    /// <summary>
    /// 清一条水平走廊（上方留空、地面铺平），让玩家物理的断言不受地形影响。
    /// </summary>
    private static void ClearCorridor(WorldState world, int centerTileX, int span)
    {
        int gy = world.SpawnTileY;
        for (int x = centerTileX - 4; x <= centerTileX + span; x++)
        {
            for (int y = gy - 4; y < gy; y++)
                world.Tiles[x, y].Active = false;

            ref var ground = ref world.Tiles[x, gy];
            ground.Active = true;
            ground.Type = 0;   // Dirt
        }
    }

    /// <summary>
    /// 玩家物理按包 13 的**控制位**在服务端推进（对齐原版 <c>Player.Update</c>，见 <c>WorldSimulator.StepPlayerPhysics</c>）。
    /// 客户端只在操作变化时发位置包，只信位置包会让服务端坐标长时间停在原地
    /// → NPC 追/打的是玩家早已离开的位置（真机症状：看着没被碰到却在扣血）。
    /// 原版数值：runAcceleration 0.08 / runSlowdown 0.2 / maxRunSpeed 3。
    /// </summary>
    [Fact]
    public void PlayerPhysics_Accelerates_WithControlBits_AndStops_OnRelease()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        ClearCorridor(world, world.SpawnTileX, 40);

        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Hp = 100,
            HpMax = 100,
            Position = new Vector2(world.SpawnTileX * 16f, world.SpawnTileY * 16f - NpcSizes.PlayerHeight),
        };
        lock (world.PlayersLock) world.Players[1] = player;

        float startX = player.Position.X;

        // 按住右键：每 tick +0.08（原版 runAcceleration）
        player.ControlBits = PlayerRuntime.ControlRight;
        for (int i = 0; i < 10; i++) sim.Tick();
        Assert.Equal(0.8f, player.Velocity.X, 3);
        Assert.True(player.Position.X > startX + 3f,
            $"按住右键 10 tick 只前进了 {player.Position.X - startX:F1}px");

        // 继续按住：收敛到原版 maxRunSpeed = 3
        for (int i = 0; i < 40; i++) sim.Tick();
        Assert.Equal(3f, player.Velocity.X, 3);

        // 松开方向键：每 tick 减 0.2（原版 runSlowdown）直到 0
        player.ControlBits = 0;
        for (int i = 0; i < 20; i++) sim.Tick();
        Assert.Equal(0f, player.Velocity.X, 3);

        float stoppedX = player.Position.X;
        for (int i = 0; i < 5; i++) sim.Tick();
        Assert.Equal(stoppedX, player.Position.X, 3);   // 停下后不再漂移
    }

    /// <summary>
    /// 起跳：原版 <c>jumpSpeed = 5.01</c>；且必须是「松开后再按」（原版 <c>releaseJump</c>，按住不放不连跳）。
    /// </summary>
    [Fact]
    public void PlayerPhysics_Jumps_OnFreshPress_Only()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        ClearCorridor(world, world.SpawnTileX, 20);

        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Hp = 100,
            HpMax = 100,
            Position = new Vector2(world.SpawnTileX * 16f, world.SpawnTileY * 16f - NpcSizes.PlayerHeight),
        };
        lock (world.PlayersLock) world.Players[1] = player;

        float groundY = player.Position.Y;
        player.ControlBits = PlayerRuntime.ControlJump;

        sim.Tick();
        Assert.True(player.Velocity.Y < 0f, "按下跳跃键后应向上运动");
        Assert.False(player.Grounded);

        // 按住不放：不得反复起跳（上升期间垂直速度必须逐 tick 受重力衰减）
        float previousVy = player.Velocity.Y;
        for (int i = 0; i < 3; i++)
        {
            sim.Tick();
            Assert.True(player.Velocity.Y > previousVy, "按住跳跃键不应反复起跳");
            previousVy = player.Velocity.Y;
        }

        // 升到最高点：原版 ≈ 2 格（jumpSpeed 5.01 / gravity 0.4）
        float minY = player.Position.Y;
        for (int i = 0; i < 20; i++)
        {
            sim.Tick();
            minY = Math.Min(minY, player.Position.Y);
        }
        Assert.True(groundY - minY > 20f, $"跳跃高度只有 {groundY - minY:F1}px");

        // 落回地面
        for (int i = 0; i < 60; i++) sim.Tick();
        Assert.True(player.Grounded, "应已落回地面");
    }

    /// <summary>
    /// 站在地面上的玩家不得累积下落距离，更不得被结算下落伤害（真机症状：站着莫名掉血）。
    /// 原版玩家碰撞盒为 20×42、<c>position</c> 为左上角 → 脚底 = <c>Position.Y + 42</c>；
    /// 早期实现把 21（半高）当成**全高**判脚底，永远检测不到落地 → <c>FallDistance</c> 持续累积，
    /// 而客户端每次位置包都会把 Velocity 清零（被当成「刚落地」）→ 凭空结算下落伤害。
    /// </summary>
    [Fact]
    public void StandingPlayer_DoesNotAccumulate_FallDistance()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        float groundTop = world.SpawnTileY * 16f;
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Hp = 100,
                HpMax = 100,
                Position = new Vector2(world.SpawnTileX * 16f + 8f, groundTop - NpcSizes.PlayerHeight),
            };

        for (int i = 0; i < 120; i++) sim.Tick();

        var player = world.Players[1];
        Assert.Equal(100, player.Hp);                                          // 没有被凭空扣血
        Assert.Equal(0f, player.FallDistance);                                 // 没有累积下落距离
        Assert.Equal(groundTop - NpcSizes.PlayerHeight, player.Position.Y, 3); // 站稳（脚下沉 21px 的旧行为会失败）
    }

    /// <summary>
    /// 贴地 NPC 落地时**不得清零水平速度**（原版地面摩擦由各 aiStyle 自己处理：史莱姆 <c>×= 0.8</c>）。
    /// 早期实现每 tick 清零 → 贴地 NPC 永远加不起速度，客户端按自己的 AI 跑到前面、再被服务端位置拉回 = 抖动。
    /// </summary>
    [Fact]
    public void GroundedEnemy_Keeps_Horizontal_Velocity()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        var (_, slimeH) = NpcSizes.Of(1);
        float groundTop = world.SpawnTileY * 16f;

        WorldNpc slime;
        lock (world.NpcsLock)
        {
            slime = new WorldNpc
            {
                Type = 1,
                NetId = 1,
                AiStyle = 1,
                Active = true,
                Life = 25,
                LifeMax = 25,
                X = (world.SpawnTileX + 6) * 16f,
                Y = groundTop - slimeH,
                VelocityX = 1.5f,
            };
            world.Npcs.Add(slime);
        }

        float x0 = slime.X;
        for (int i = 0; i < 3; i++) sim.Tick();

        // 三 tick 内应保持水平位移（1.5 + 1.2 + 0.96）；若落地被清零则只会移动一 tick（1.5）
        Assert.True(slime.X - x0 > 2.5f, $"贴地 NPC 水平速度被清零（3 tick 位移仅 {slime.X - x0}）");
    }

    /// <summary>
    /// 失效实体回收：弹幕到期失效后，在「销毁包已下发」（<c>RemovalNotified</c>）后必须从世界列表移除，
    /// 否则列表只增不减，长跑下每 tick 遍历与快照过滤会持续变慢。
    /// </summary>
    [Fact]
    public void Expired_Projectiles_Are_Reclaimed_After_Removal_Notified()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        lock (world.ProjectilesLock)
            world.Projectiles.Add(new ProjectileEntity
            {
                Key = 5,
                Owner = 1,
                Type = 1,
                Position = new Vector2(100f, 100f),
                Velocity = new Vector2(0f, 0f),
                Damage = 1,
                TimeLeft = 1,
                Active = true,
            });

        // 到期 → 失效（此时销毁包尚未下发，必须保留）
        sim.Tick();
        lock (world.ProjectilesLock)
            Assert.Contains(world.Projectiles, p => p.Key == 5 && !p.Active);

        // 世界同步下发销毁包后置位 → 下一 tick 回收
        lock (world.ProjectilesLock)
            foreach (var p in world.Projectiles) p.RemovalNotified = true;

        sim.Tick();
        lock (world.ProjectilesLock)
            Assert.DoesNotContain(world.Projectiles, p => p.Key == 5);
    }

    /// <summary>
    /// 敌怪为**跳跃式移动**：落地后有静止等待期，起跳时 <c>VelocityY &lt; 0</c>（向上）。
    /// 早期实现是「贴地每 tick 水平滑行 1px」，VelocityY 永不为负 —— 本用例钉住该差异。
    /// </summary>
    [Fact]
    public void Enemy_Hops_WithGroundedWait_InsteadOfGroundGliding()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        // 放一名玩家（敌怪以最近玩家为水平目标）；贴地放置，避免先经历长距离落体
        float px = (world.SpawnTileX + 12) * 16f;
        float py = world.SpawnTileY * 16f - 8f;
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(px, py) };

        WorldNpc slime;
        lock (world.NpcsLock)
        {
            slime = new WorldNpc
            {
                Type = 1,
                NetId = 1,
                Active = true,
                Life = 25,
                LifeMax = 25,
                X = px - 64f,
                Y = py,
            };
            world.Npcs.Add(slime);
        }

        bool jumped = false;
        int stillRun = 0, maxStillRun = 0;
        float lastX = slime.X;

        for (int i = 0; i < 300; i++)
        {
            sim.Tick();

            if (slime.VelocityY < 0f) jumped = true;

            // 地面静止等待：竖直速度为 0 且本 tick 水平未移动
            if (slime.VelocityY == 0f && slime.X == lastX)
            {
                stillRun++;
                maxStillRun = Math.Max(maxStillRun, stillRun);
            }
            else
            {
                stillRun = 0;
            }

            lastX = slime.X;
        }

        Assert.True(jumped, "史莱姆从未起跳（VelocityY 始终 >= 0，说明仍是贴地滑行）");
        Assert.True(maxStillRun >= 2, $"缺少地面静止等待期（最长连续仅 {maxStillRun} tick）");
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
