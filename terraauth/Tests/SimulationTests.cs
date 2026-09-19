// TerraAuth — Phase 3 验收测试

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraAuth.Authority;
using TerraAuth.Net.Transport;
using TerraAuth.Protocol;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

public class SimulationTests
{
    [Fact]
    public void TileEntities_Index_Delete_And_Queues_Remain_Consistent()
    {
        var world = new WorldState();
        var first = world.InsertTileEntity(new TileEntity { Type = 7, X = 10, Y = 20 });
        var replacement = world.InsertTileEntity(new TileEntity { Type = 1, X = 10, Y = 20 });
        var other = world.InsertTileEntity(new TileEntity { Type = 2, X = 11, Y = 20 });

        Assert.Equal(0, first.Id);
        Assert.Equal(1, replacement.Id);
        Assert.Equal(2, other.Id);
        Assert.False(world.TryGetTileEntity(first.Id, out _));
        Assert.True(world.TryGetTileEntityAt(10, 20, out var atAnchor));
        Assert.Same(replacement, atAnchor);
        Assert.Equal(new[] { 1, 2 }, world.DrainDirtyTileEntities(10));
        Assert.Equal(new[] { 0 }, world.DrainDeletedTileEntities(10).Select(static entity => entity.RuntimeId));

        world.MarkTileEntityDirty(replacement.Id);
        Assert.True(world.RemoveTileEntityAt(10, 20));
        Assert.False(world.TryGetTileEntity(replacement.Id, out _));
        Assert.False(world.TryGetTileEntityAt(10, 20, out _));
        Assert.Equal(new[] { 1 }, world.DrainDeletedTileEntities(10).Select(static entity => entity.RuntimeId));

        world.RequeueDirtyTileEntities(new[] { other.Id });
        Assert.Equal(new[] { other.Id }, world.DrainDirtyTileEntities(10));
        var deletion = new WorldState.TileEntityDeletion(replacement.Id, replacement.FileId, replacement.Type, replacement.X, replacement.Y);
        world.RequeueDeletedTileEntities(new[] { deletion });
        Assert.Equal(new[] { replacement.Id }, world.DrainDeletedTileEntities(10).Select(static entity => entity.RuntimeId));
    }

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
    public void Disconnect_Clears_Owned_Summon_Projectiles()
    {
        // 阶段 H：玩家断线时其召唤弹幕立即失效（对齐原版——掉线召唤物消失）；
        // 非召唤类型与他人弹幕不受影响；Active=false 后由世界同步循环补发包 29 广播销毁。
        var world = new WorldState();
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, SessionId = 22, Active = true };
        lock (world.ProjectilesLock)
        {
            world.Projectiles.Add(new ProjectileEntity { Key = 1, Owner = 1, Type = 191, IsSummon = true, Active = true });
            world.Projectiles.Add(new ProjectileEntity { Key = 2, Owner = 1, Type = 1, Active = true });
            world.Projectiles.Add(new ProjectileEntity { Key = 3, Owner = 2, Type = 191, Active = true });
        }

        world.MarkPlayerOffline(1, 22, "old", 0);

        Assert.False(world.Projectiles[0].Active);  // 本玩家召唤弹幕 → 失效
        Assert.True(world.Projectiles[1].Active);   // 非召唤类型不受影响
        Assert.True(world.Projectiles[2].Active);   // 他人召唤弹幕不受影响
    }

    [Fact]
    public void Summon_Projectile_Spawn_Validates_Weapon_And_Damage()
    {
        // 阶段 H：召唤弹幕 spawn 时校验权威伤害——手持已收录召唤武器则弹幕 Damage 须 ≤ 武器上界，
        // 空手 / 非召唤武器拒绝，未收录武器（mod）放行，非召唤弹幕不受影响。
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100 };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true };
        var player = world.Players[1];
        var rng = new XoshiroRng(1);

        // 1) 手持星尘细胞法杖（3474，60 伤 Summon）→ 弹幕伤害 60 ≤ ceil(60×1.15)=69 → 接受
        player.Items[3] = 3474;
        player.ItemPrefixes[3] = 0;
        player.SelectedSlot = 3;
        Assert.True(new SpawnProjectileCommand(1, 1, 7, 191, new Vector2(0, 0), new Vector2(0, 0), 60)
            .Apply(world, rng).Applied);
        lock (world.ProjectilesLock)
        {
            var summon = Assert.Single(world.Projectiles);
            Assert.True(summon.IsSummon);
            Assert.Equal(3474, summon.SourceItem);
            Assert.Equal(3474, summon.SourceWeaponItem);
            Assert.Equal((byte)0, summon.SourceWeaponPrefix);
            Assert.Equal(1, summon.SourceTransactionId);
            Assert.Equal(182, summon.SourceSummonBuffId);

            player.Items[3] = 24;
            player.ItemPrefixes[3] = 1;
            Assert.Equal(3474, summon.SourceWeaponItem);
            Assert.Equal((byte)0, summon.SourceWeaponPrefix);
            Assert.Equal(1, summon.SourceTransactionId);
            player.Items[3] = 3474;
            player.ItemPrefixes[3] = 0;
        }

        // 2) 弹幕伤害 999 超权威上界 69 → 拒绝（堵住虚报弹幕伤害抬高命中上界）
        Assert.Equal("projectile_damage_above_bound",
            new SpawnProjectileCommand(2, 1, 8, 191, new Vector2(0, 0), new Vector2(0, 0), 999)
                .Apply(world, rng).Reason);

        // 3) 空手 spawn 召唤弹幕 → 拒绝（原版空手不能召唤）
        player.Items[3] = 0;
        player.ItemPrefixes[3] = 0;
        Assert.Equal("summon_requires_summon_weapon",
            new SpawnProjectileCommand(3, 1, 9, 191, new Vector2(0, 0), new Vector2(0, 0), 10)
                .Apply(world, rng).Reason);

        // 4) 手持近战武器（木剑 24）spawn 召唤弹幕 → 拒绝
        player.Items[3] = 24;
        player.ItemPrefixes[3] = 0;
        Assert.Equal("summon_requires_summon_weapon",
            new SpawnProjectileCommand(4, 1, 10, 191, new Vector2(0, 0), new Vector2(0, 0), 10)
                .Apply(world, rng).Reason);

        // 5) 非召唤弹幕类型（Type=1）不受校验 → 空手也能 spawn
        player.Items[3] = 0;
        Assert.True(new SpawnProjectileCommand(5, 1, 11, 1, new Vector2(0, 0), new Vector2(0, 0), 10)
            .Apply(world, rng).Applied);

        // 6) 未收录武器（99999 不在表，mod 场景）spawn 召唤弹幕 → 放行（绝不误拒）
        player.Items[3] = 99999;
        Assert.True(new SpawnProjectileCommand(6, 1, 12, 191, new Vector2(0, 0), new Vector2(0, 0), 999)
            .Apply(world, rng).Applied);
    }

    [Fact]
    public void Summon_Projectile_Ownership_And_Source_Transaction_Are_Independent()
    {
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100 };
        lock (world.PlayersLock)
        {
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true };
            world.Players[2] = new PlayerRuntime { Id = 2, Active = true };
            world.Players[1].Items[3] = 3474;
            world.Players[2].Items[3] = 3474;
            world.Players[1].SelectedSlot = 3;
            world.Players[2].SelectedSlot = 3;
        }

        var rng = new XoshiroRng(1);
        Assert.True(new SpawnProjectileCommand(1, 1, 7, 191, new Vector2(0f, 0f), new Vector2(0f, 0f), 60)
            .Apply(world, rng).Applied);
        Assert.True(new SpawnProjectileCommand(2, 2, 7, 191, new Vector2(0f, 0f), new Vector2(0f, 0f), 60)
            .Apply(world, rng).Applied);

        lock (world.ProjectilesLock)
        {
            Assert.Equal(2, world.Projectiles.Count);
            Assert.Equal(new[] { 1, 2 }, world.Projectiles.Select(p => p.Owner).OrderBy(id => id));
            Assert.Equal(new[] { 1L, 2L }, world.Projectiles.Select(p => p.SourceTransactionId).OrderBy(id => id));
        }
    }

    [Fact]
    public void Summon_Entities_Have_Stable_Ids_And_Kinds()
    {
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100 };
        var player = new PlayerRuntime { Id = 1, Active = true, SelectedSlot = 3 };
        player.Items[3] = 3474;
        player.ItemStacks[3] = 1;
        lock (world.PlayersLock) world.Players[1] = player;

        var rng = new XoshiroRng(1);
        Assert.True(new SpawnProjectileCommand(1, 1, 7, 191,
            new Vector2(0f, 0f), new Vector2(0f, 0f), 60).Apply(world, rng).Applied);
        // 308 FrostHydra 是**真哨兵**（旧手写 SentryTypes 把 831 StormTigerGem 误标为哨兵，已随拆表修正）
        Assert.True(new SpawnProjectileCommand(2, 1, 8, 308,
            new Vector2(0f, 0f), new Vector2(0f, 0f), 60).Apply(world, rng).Applied);

        lock (world.ProjectilesLock)
        {
            var minion = Assert.Single(world.Projectiles, p => p.Key == 7);
            var sentry = Assert.Single(world.Projectiles, p => p.Key == 8);
            Assert.Equal(SummonKind.Minion, minion.SummonKind);
            Assert.Equal(SummonKind.Sentry, sentry.SummonKind);
            Assert.True(minion.SummonEntityId > 0);
            Assert.True(sentry.SummonEntityId > minion.SummonEntityId);

            long entityId = minion.SummonEntityId;
            Assert.True(new SpawnProjectileCommand(3, 1, 7, 191,
                new Vector2(50f, 50f), new Vector2(0f, 0f), 60).Apply(world, rng).Applied);
            Assert.Equal(2, world.Projectiles.Count);
            Assert.Equal(entityId, minion.SummonEntityId);
        }
    }

    [Fact]
    public void NonSummon_Projectile_Spawn_Validates_HeldWeapon()
    {
        // 阶段 H2：非召唤 / 非哨兵弹幕创建时，按「手持已收录武器」权威推导伤害——
        // 假报超高伤害应拒绝（堵住用高 Damage 弹幕抬阶段 C 命中上界）；合理上报则被服务端覆盖为权威值。
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100 };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true };
        var player = world.Players[1];
        var rng = new XoshiroRng(1);

        // 手持近战武器（木剑 24），spawn 非召唤弹幕（类型 20 = 恶魔飞刀）
        player.Items[3] = 24;
        player.ItemPrefixes[3] = 0;
        player.SelectedSlot = 3;
        int authoritative = CombatResolver.GetWeaponDamage(player, 24, 0);
        Assert.True(authoritative > 0);

        // 1) 合理上报（≤ ceil(权威×1.15)）→ 接受，且弹幕伤害被覆盖为服务端权威值
        var ok = new SpawnProjectileCommand(1, 1, 1, 20, new Vector2(0, 0), new Vector2(0, 0), authoritative)
            .Apply(world, rng);
        Assert.True(ok.Applied);
        lock (world.ProjectilesLock)
        {
            var placed = world.Projectiles.Single(p => p.Key == 1);
            Assert.Equal(authoritative, placed.Damage);
        }

        // 2) 假报超高伤害（> ceil(权威×1.15)）→ 拒绝，杜绝虚报弹幕伤害抬命中上界
        int tooHigh = (int)Math.Ceiling(authoritative * 1.15f) + 1;
        Assert.Equal("projectile_damage_above_bound",
            new SpawnProjectileCommand(2, 1, 2, 20, new Vector2(0, 0), new Vector2(0, 0), tooHigh)
                .Apply(world, rng).Reason);

        // 3) 空手 spawn 非召唤弹幕 → 无推导来源，仍放行（不误拒非武器弹幕）
        player.Items[3] = 0;
        Assert.True(new SpawnProjectileCommand(3, 1, 3, 9, new Vector2(0, 0), new Vector2(0, 0), 30)
            .Apply(world, rng).Applied);
    }

    [Fact]
    public void ProjectileSpawn_RequiresAuthoritativeWeaponAndAmmoType()
    {
        var world = new WorldState();
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, SelectedSlot = 3 };
        var player = world.Players[1];
        var rng = new XoshiroRng(1);
        var origin = new Vector2(0, 0);

        // 泰拉刃固定发射 985；客户端篡改成 2 必须被拒绝。
        player.Items[3] = 757;
        player.ItemStacks[3] = 1;
        Assert.Equal("projectile_type_not_allowed",
            new SpawnProjectileCommand(1, 1, 1, 2, origin, origin, 85).Apply(world, rng).Reason);
        Assert.True(new SpawnProjectileCommand(2, 1, 2, 985, origin, origin, 85).Apply(world, rng).Applied);

        // 木弓仅可使用服务端背包中的木箭（类型 1），不能伪造为火箭类型。
        player.Items[3] = 39;
        player.ItemStacks[3] = 1;
        player.Items[4] = 40;
        player.ItemStacks[4] = 20;
        Assert.Equal("projectile_type_not_allowed",
            new SpawnProjectileCommand(3, 1, 3, 134, origin, origin, 4).Apply(world, rng).Reason);
        Assert.True(new SpawnProjectileCommand(4, 1, 4, 1, origin, origin, 4).Apply(world, rng).Applied);

        // 已知弹药但无服务端弹幕映射时，不能回退为接受任意客户端类型。
        player.Items[4] = 771;
        player.ItemStacks[4] = 1;
        Assert.Equal("projectile_type_not_allowed",
            new SpawnProjectileCommand(5, 1, 5, 134, origin, origin, 4).Apply(world, rng).Reason);

        // 已收录远程武器没有可用同类弹药也不能凭空生成弹幕。
        player.ItemStacks[4] = 0;
        player.Items[4] = 0;
        Assert.Equal("projectile_type_not_allowed",
            new SpawnProjectileCommand(5, 1, 5, 1, origin, origin, 4).Apply(world, rng).Reason);
    }

    [Fact]
    public void ProjectileSpawn_Consumes_Authoritative_Ammo_Exactly_Once()
    {
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100 };
        lock (world.PlayersLock)
        {
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, SelectedSlot = 3 };
            world.Players[2] = new PlayerRuntime { Id = 2, Active = true, SelectedSlot = 3 };
        }

        var first = world.Players[1];
        first.Items[3] = 39;
        first.ItemStacks[3] = 1;
        first.Items[4] = 40;
        first.ItemStacks[4] = 2;

        var second = world.Players[2];
        second.Items[3] = 39;
        second.ItemStacks[3] = 1;
        second.Items[4] = 40;
        second.ItemStacks[4] = 1;

        var rng = new XoshiroRng(1);
        var origin = new Vector2(0, 0);

        Assert.True(new SpawnProjectileCommand(1, 1, 1, 1, origin, origin, 4)
            .Apply(world, rng).Applied);
        Assert.Equal(1, first.ItemStacks[4]);

        // 同一归属者的重复弹幕 Key 只是同步更新，不得再次扣除弹药。
        Assert.True(new SpawnProjectileCommand(2, 1, 1, 1, origin, origin, 4)
            .Apply(world, rng).Applied);
        Assert.Equal(1, first.ItemStacks[4]);

        // 不同玩家可以使用相同 Key，各自独立消费自己的库存。
        Assert.True(new SpawnProjectileCommand(3, 2, 1, 1, origin, origin, 4)
            .Apply(world, rng).Applied);
        Assert.Equal(0, second.ItemStacks[4]);
        Assert.Equal(0, second.Items[4]);

        // 无弹药时拒绝，且不会写入新的弹幕或改变库存。
        var rejected = new SpawnProjectileCommand(4, 2, 2, 1, origin, origin, 4)
            .Apply(world, rng);
        Assert.False(rejected.Applied);
        Assert.Equal("projectile_type_not_allowed", rejected.Reason);
        Assert.Equal(0, second.ItemStacks[4]);
    }

    [Fact]
    public void ProjectileSpawn_Allows_Infinite_Ammo_Without_Decrement()
    {
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100 };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, SelectedSlot = 3 };

        var player = world.Players[1];
        player.Items[3] = 39;
        player.ItemStacks[3] = 1;
        player.Items[4] = 3103;
        player.ItemStacks[4] = 1;

        var result = new SpawnProjectileCommand(1, 1, 1, 1,
            new Vector2(0, 0), new Vector2(0, 0), 4)
            .Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        Assert.Equal(3103, player.Items[4]);
        Assert.Equal(1, player.ItemStacks[4]);
    }

    [Fact]
    public void ProjectileSpawn_Rejects_Mismatched_Ammo_Without_Consuming()
    {
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100 };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, SelectedSlot = 3 };

        var player = world.Players[1];
        player.Items[3] = 39;
        player.ItemStacks[3] = 1;
        player.Items[4] = 40;
        player.ItemStacks[4] = 1;

        var result = new SpawnProjectileCommand(1, 1, 1, 14,
            new Vector2(0, 0), new Vector2(0, 0), 4)
            .Apply(world, new XoshiroRng(1));

        Assert.False(result.Applied);
        Assert.Equal("projectile_type_not_allowed", result.Reason);
        Assert.Equal(40, player.Items[4]);
        Assert.Equal(1, player.ItemStacks[4]);
    }

    [Fact]
    public void ProjectileSpawn_Validates_Initial_Position_And_Velocity()
    {
        var world = new WorldState { MaxTilesX = 1000, MaxTilesY = 1000 };
        lock (world.PlayersLock)
        {
            world.Players[1] = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Position = new Vector2(160, 160),
                AimPosition = new Vector2(160, 160),
                SelectedSlot = 3,
            };
        }
        var player = world.Players[1];
        player.Items[3] = 757;
        player.ItemStacks[3] = 1;
        var rng = new XoshiroRng(1);

        Assert.True(new SpawnProjectileCommand(1, 1, 1, 985, new Vector2(170, 170), new Vector2(20, 0), 85)
            .Apply(world, rng).Applied);
        Assert.Equal("projectile_spawn_too_far",
            new SpawnProjectileCommand(2, 1, 2, 985, new Vector2(500, 500), new Vector2(20, 0), 85)
                .Apply(world, rng).Reason);
        Assert.Equal("projectile_speed_exceeded",
            new SpawnProjectileCommand(3, 1, 3, 985, new Vector2(170, 170), new Vector2(513, 0), 85)
                .Apply(world, rng).Reason);
        Assert.Equal("projectile_spawn_out_of_world",
            new SpawnProjectileCommand(4, 1, 4, 985, new Vector2(-1, 0), new Vector2(20, 0), 85)
                .Apply(world, rng).Reason);
        Assert.Equal("invalid_projectile_spawn",
            new SpawnProjectileCommand(5, 1, 5, 985, new Vector2(float.NaN, 170), new Vector2(20, 0), 85)
                .Apply(world, rng).Reason);

        lock (world.ProjectilesLock)
        {
            Assert.Single(world.Projectiles);
            Assert.DoesNotContain(world.Projectiles, p => p.Key is 2 or 3 or 4 or 5);
        }
    }

    [Fact]
    public void ProjectileSpawn_UseItem_StateMachine_Gates_New_Mapped_Projectiles()
    {
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100, Tick = 10 };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, SelectedSlot = 3 };

        var player = world.Players[1];
        player.Items[3] = 757; // TerraBlade -> Direct projectile 985
        player.ItemStacks[3] = 1;
        player.Items[4] = 757;
        player.ItemStacks[4] = 1;
        var rng = new XoshiroRng(1);
        var origin = new Vector2(0, 0);

        Assert.True(new MoveCommand(1, 1, player.Position)
        {
            SelectedItem = 3,
            ControlBits = 0,
        }.Apply(world, rng).Applied);
        Assert.Equal(CommandFailures.ProjectileUseItemNotHeld,
            new SpawnProjectileCommand(2, 1, 1, 985, origin, origin, 85).Apply(world, rng).Reason);

        Assert.True(new MoveCommand(3, 1, player.Position)
        {
            SelectedItem = 3,
            ControlBits = PlayerRuntime.ControlUseItem,
        }.Apply(world, rng).Applied);
        Assert.Equal(3, player.UseItemSelectedSlot);
        Assert.True(new SpawnProjectileCommand(4, 1, 1, 985, origin, origin, 85).Apply(world, rng).Applied);

        // 同 Key 更新不重新授权，也不刷新新 Key 冷却。
        Assert.True(new MoveCommand(5, 1, player.Position)
        {
            SelectedItem = 3,
            ControlBits = PlayerRuntime.ControlUseItem,
        }.Apply(world, rng).Applied);
        Assert.True(new SpawnProjectileCommand(5, 1, 1, 985, origin, origin, 85).Apply(world, rng).Applied);
        Assert.Equal(CommandFailures.ProjectileUseItemCooldown,
            new SpawnProjectileCommand(5, 1, 2, 985, origin, origin, 85).Apply(world, rng).Reason);

        Assert.True(new MoveCommand(6, 1, player.Position)
        {
            SelectedItem = 4,
            ControlBits = PlayerRuntime.ControlUseItem,
        }.Apply(world, rng).Applied);
        Assert.Equal(4, player.UseItemSelectedSlot);
        Assert.Equal(CommandFailures.ProjectileUseItemCooldown,
            new SpawnProjectileCommand(6, 1, 3, 985, origin, origin, 85).Apply(world, rng).Reason);

        Assert.True(new MoveCommand(7, 1, player.Position)
        {
            SelectedItem = 3,
            ControlBits = PlayerRuntime.ControlUseItem,
        }.Apply(world, rng).Applied);
        world.Tick = 30;
        Assert.True(new SpawnProjectileCommand(8, 1, 2, 985, origin, origin, 85).Apply(world, rng).Applied);

        Assert.True(new MoveCommand(7, 1, player.Position)
        {
            SelectedItem = 3,
            ControlBits = 0,
        }.Apply(world, rng).Applied);
        Assert.Equal(CommandFailures.ProjectileUseItemNotHeld,
            new SpawnProjectileCommand(7, 1, 3, 985, origin, origin, 85).Apply(world, rng).Reason);

        // 近战与未知武器不进入 UseItem 弹幕状态机，保留旧命令层兼容。
        player.Items[3] = 24;
        int meleeDamage = CombatResolver.GetWeaponDamage(player, 24, 0);
        var meleeResult = new SpawnProjectileCommand(8, 1, 4, 20, origin, origin, meleeDamage).Apply(world, rng);
        Assert.True(meleeResult.Applied, meleeResult.Reason);
        player.Items[3] = 99999;
        var unknownResult = new SpawnProjectileCommand(9, 1, 5, 1, origin, origin, 10).Apply(world, rng);
        Assert.True(unknownResult.Applied, unknownResult.Reason);
    }

    [Fact]
    public void HeldWeaponFire_Uses_Server_Tick_Cadence_And_Stops_On_Release()
    {
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100 };
        var commands = new CommandQueue();
        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            SelectedSlot = 3,
            Position = new Vector2(160, 160),
            AimPosition = new Vector2(320, 160),
        };
        player.Items[3] = 533;
        player.ItemStacks[3] = 1;
        player.Items[4] = 97;
        player.ItemStacks[4] = 4;
        lock (world.PlayersLock)
            world.Players[1] = player;

        Assert.True(commands.Enqueue(new MoveCommand(1, 1, player.Position)
        {
            SelectedItem = 3,
            ControlBits = PlayerRuntime.ControlUseItem,
        }));
        var sim = new WorldSimulator(world, commands, new EventRecorder(), new SnapshotStore());

        sim.Tick();
        Assert.Single(world.Projectiles);
        Assert.Equal(3, player.ItemStacks[4]);
        Assert.Equal(14, world.Projectiles[0].Type);
        Assert.Equal(533, world.Projectiles[0].SourceWeaponItem);
        Assert.True(world.Projectiles[0].Velocity.X > 0);

        for (int i = 0; i < 6; i++)
            sim.Tick();
        Assert.Single(world.Projectiles);

        sim.Tick();
        Assert.Equal(2, world.Projectiles.Count);
        Assert.Equal(2, player.ItemStacks[4]);

        Assert.True(commands.Enqueue(new MoveCommand(world.Tick + 1, 1, player.Position)
        {
            SelectedItem = 3,
            ControlBits = 0,
        }));
        sim.Tick();
        for (int i = 0; i < 8; i++)
            sim.Tick();
        Assert.Equal(2, world.Projectiles.Count);
        Assert.Equal(2, player.ItemStacks[4]);
    }

    [Fact]
    public void ProjectileSpawn_Uses_Server_Fire_Cooldown_And_Transaction_Idempotency()
    {
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100, Tick = 1 };
        var player = new PlayerRuntime { Id = 1, Active = true, SelectedSlot = 3 };
        player.Items[3] = 533; // Megashark
        player.ItemStacks[3] = 1;
        player.Items[4] = 97;  // Musket Ball
        player.ItemStacks[4] = 3;
        lock (world.PlayersLock)
            world.Players[1] = player;

        var rng = new XoshiroRng(1);
        var origin = new Vector2(0, 0);
        Assert.True(new MoveCommand(1, 1, origin)
        {
            SelectedItem = 3,
            ControlBits = PlayerRuntime.ControlUseItem,
        }.Apply(world, rng).Applied);

        Assert.True(new SpawnProjectileCommand(1, 1, 1, 14, origin, origin, 25, 100)
            .Apply(world, rng).Applied);
        Assert.Equal(2, player.ItemStacks[4]);
        Assert.Equal(100, world.Projectiles.Single().FireTransactionId);

        Assert.Equal(CommandFailures.FireTransactionDuplicate,
            new SpawnProjectileCommand(2, 1, 2, 14, origin, origin, 25, 100)
                .Apply(world, rng).Reason);
        Assert.Equal(2, player.ItemStacks[4]);
        Assert.Single(world.Projectiles);

        world.Tick = 7;
        Assert.Equal(CommandFailures.ProjectileUseItemCooldown,
            new SpawnProjectileCommand(7, 1, 2, 14, origin, origin, 25, 101)
                .Apply(world, rng).Reason);

        world.Tick = 8;
        Assert.True(new SpawnProjectileCommand(8, 1, 2, 14, origin, origin, 25, 101)
            .Apply(world, rng).Applied);
        Assert.Equal(1, player.ItemStacks[4]);
    }

    [Fact]
    public void ProjectileSpawn_Mana_Check_Is_Atomic_With_Ammo()
    {
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100, Tick = 1 };
        var player = new PlayerRuntime { Id = 1, Active = true, SelectedSlot = 3, Mp = 5, MpMax = 20 };
        player.Items[3] = 127; // Space Gun
        player.ItemStacks[3] = 1;
        lock (world.PlayersLock)
            world.Players[1] = player;

        var rng = new XoshiroRng(1);
        var origin = new Vector2(0, 0);
        Assert.True(new MoveCommand(1, 1, origin)
        {
            SelectedItem = 3,
            ControlBits = PlayerRuntime.ControlUseItem,
        }.Apply(world, rng).Applied);

        Assert.Equal(CommandFailures.ProjectileManaInsufficient,
            new SpawnProjectileCommand(1, 1, 1, 72, origin, origin, 20, 200)
                .Apply(world, rng).Reason);
        Assert.Equal(5, player.Mp);
        Assert.Empty(world.Projectiles);

        player.Mp = 12;
        Assert.True(new SpawnProjectileCommand(2, 1, 1, 72, origin, origin, 20, 200)
            .Apply(world, rng).Applied);
        Assert.Equal(6, player.Mp);
        Assert.Single(world.Projectiles);
        Assert.Contains(1, world.DrainPlayerManaChanged(16));
        Assert.Empty(world.DrainPlayerManaChanged(16));
    }

    [Fact]
    public void ProjectileSpawn_Captures_Weapon_Source_Before_Resource_Commit()
    {
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100, Tick = 1 };
        var player = new PlayerRuntime { Id = 1, Active = true, SelectedSlot = 3 };
        player.Items[3] = 533; // Megashark
        player.ItemStacks[3] = 1;
        player.ItemPrefixes[3] = 81;
        player.Items[4] = 97; // Musket Ball
        player.ItemStacks[4] = 1;
        lock (world.PlayersLock)
            world.Players[1] = player;

        var rng = new XoshiroRng(1);
        var origin = new Vector2(0, 0);
        Assert.True(new MoveCommand(1, 1, origin)
        {
            SelectedItem = 3,
            ControlBits = PlayerRuntime.ControlUseItem,
        }.Apply(world, rng).Applied);

        Assert.True(new SpawnProjectileCommand(1, 1, 1, 14, origin, origin, 25, 100)
            .Apply(world, rng).Applied);

        var projectile = Assert.Single(world.Projectiles);
        Assert.Equal(533, projectile.SourceWeaponItem);
        Assert.Equal(81, projectile.SourceWeaponPrefix);
        Assert.Equal(0, player.Items[4]);
        Assert.Equal(0, player.ItemStacks[4]);
    }

    [Fact]
    public void Fire_Transaction_History_Keeps_Recent_Window()
    {
        var player = new PlayerRuntime();
        for (long transactionId = 1;
             transactionId <= PlayerRuntime.MaxAppliedFireTransactions + 1;
             transactionId++)
        {
            player.RecordAppliedFireTransaction(transactionId);
        }

        Assert.Equal(PlayerRuntime.MaxAppliedFireTransactions, player.AppliedFireTransactions.Count);
        Assert.DoesNotContain(1L, player.AppliedFireTransactions);
        Assert.Contains(2L, player.AppliedFireTransactions);
        Assert.Contains(PlayerRuntime.MaxAppliedFireTransactions + 1L,
            player.AppliedFireTransactions);
    }

    [Fact]
    public void Client_Mana_Report_Cannot_Restore_Server_Mana()
    {
        var world = new WorldState();
        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Mp = 6,
            MpMax = 20,
            HasReceivedManaSync = true,
        };
        lock (world.PlayersLock)
            world.Players[1] = player;

        var rng = new XoshiroRng(1);
        Assert.False(new SetManaCommand(1, 1, 12, 20).Apply(world, rng).Applied);
        Assert.Equal(6, player.Mp);
        Assert.False(new SetManaCommand(2, 1, 6, 40).Apply(world, rng).Applied);
        Assert.Equal(20, player.MpMax);
        Assert.True(new SetManaCommand(3, 1, 4, 20).Apply(world, rng).Applied);
        Assert.Equal(4, player.Mp);
    }

    [Fact]
    public void SessionResume_Reopens_Mana_Baseline_Window()
    {
        var world = new WorldState();
        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            SessionId = 11,
            Mp = 6,
            MpMax = 20,
            HasReceivedManaSync = true,
        };
        lock (world.PlayersLock)
            world.Players[1] = player;

        world.MarkPlayerOffline(1, 11, "Alice", graceTicks: 60);
        Assert.True(world.TryResumePlayer(1, 22, "Alice"));
        Assert.False(player.HasReceivedManaSync);

        var result = new SetManaCommand(1, 1, 12, 40) { SessionId = 22 }
            .Apply(world, new XoshiroRng(1));
        Assert.True(result.Applied);
        Assert.Equal(12, player.Mp);
        Assert.Equal(40, player.MpMax);
    }

    /// <summary>包 40 命令：对话目标写入服务端状态，仅在真正变化时标记中继；无效目标回落为「未对话」。</summary>
    [Fact]
    public void SetTalkNpc_Tracks_State_And_Marks_Only_On_Change()
    {
        var world = new WorldState();
        var player = new PlayerRuntime { Id = 1, Active = true };
        lock (world.PlayersLock)
            world.Players[1] = player;

        int townIndex;
        int monsterIndex;
        lock (world.NpcsLock)
        {
            world.Npcs.Add(new WorldNpc { Type = 22, NetId = 22, Active = true, IsTownNpc = true });
            world.Npcs.Add(new WorldNpc { Type = 1, NetId = 1, Active = true, IsTownNpc = false });
            townIndex = 0;
            monsterIndex = 1;
        }
        var rng = new XoshiroRng(1);

        // 合法城镇 NPC → 记录并标记一次中继
        Assert.True(new SetTalkNpcCommand(1, 1, townIndex).Apply(world, rng).Applied);
        Assert.Equal(townIndex, player.TalkNpc);
        Assert.Contains(1, world.DrainPlayerTalkNpcChanged(8));

        // 重复同一目标 → 幂等，不重复广播
        Assert.True(new SetTalkNpcCommand(2, 1, townIndex).Apply(world, rng).Applied);
        Assert.Empty(world.DrainPlayerTalkNpcChanged(8));

        // 非城镇 NPC → 视为未对话（不把无效目标中继给其他客户端）
        Assert.True(new SetTalkNpcCommand(3, 1, monsterIndex).Apply(world, rng).Applied);
        Assert.Equal(-1, player.TalkNpc);
        Assert.Contains(1, world.DrainPlayerTalkNpcChanged(8));

        // 越界索引 → 同样回落为未对话；已是 -1 故无变化标记
        Assert.True(new SetTalkNpcCommand(4, 1, 99).Apply(world, rng).Applied);
        Assert.Equal(-1, player.TalkNpc);
        Assert.Empty(world.DrainPlayerTalkNpcChanged(8));
    }

    /// <summary>
    /// SSC 背包守恒事务：窗口内聚合的合法整理（拆分 / 移动）在守恒时整体提交。
    /// 原版拖拽 = 源槽减少 + 目标槽增加，逐包校验会全部回正，必须窗口聚合后统一判。
    /// </summary>
    [Fact]
    public void InventoryTransaction_Commits_Conserved_Move_And_Split()
    {
        var world = new WorldState { Tick = 100 };
        var player = new PlayerRuntime { Id = 1, Active = true };
        player.Items[50] = 40;      // 泥土
        player.ItemStacks[50] = 10;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        // 拆分 4 个到槽 51：源槽 6 + 目标槽 4
        Assert.True(new StageInventorySlotCommand(100, 1, 50, 40, 6).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 51, 40, 4).Apply(world, rng).Applied);

        // 窗口未到期：不结算，权威背包保持不变
        Assert.Equal(InventoryTransactionOutcome.None, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(10, player.ItemStacks[50]);
        Assert.Equal(0, player.Items[51]);

        // 窗口到期：总量 10 不变 → 守恒 → 提交
        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.Committed, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(40, player.Items[50]);
        Assert.Equal(6, player.ItemStacks[50]);
        Assert.Equal(40, player.Items[51]);
        Assert.Equal(4, player.ItemStacks[51]);
    }

    /// <summary>SSC 背包守恒事务：凭空造物破坏守恒 → 回滚，且把权威槽位标记回写以纠正客户端。</summary>
    [Fact]
    public void InventoryTransaction_RollsBack_On_Forgery()
    {
        var world = new WorldState { Tick = 100 };
        var player = new PlayerRuntime { Id = 1, Active = true };
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        // 权威背包为空，客户端却声称槽 3 有泰拉刃
        Assert.True(new StageInventorySlotCommand(100, 1, 3, 757, 1).Apply(world, rng).Applied);
        world.Tick = 120;

        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(0, player.Items[3]);
        // 回滚后仍要回写权威值（客户端被纠正回空槽）
        Assert.Contains((1, player.SessionId, 3), world.DrainInventoryUpdates(8));
    }

    /// <summary>
    /// SSC 背包守恒事务：与外部变更（/give、拾取、开袋、箱子转移）冲突的暂存意图
    /// 因权威总量对不上而自然回滚，无需额外版本号 / 冲突表。
    /// </summary>
    [Fact]
    public void InventoryTransaction_Conflicting_With_External_Change_RollsBack()
    {
        var world = new WorldState { Tick = 100 };
        var player = new PlayerRuntime { Id = 1, Active = true };
        player.Items[10] = 40;
        player.ItemStacks[10] = 1;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        // 客户端把槽 10 的泥土移到槽 11
        Assert.True(new StageInventorySlotCommand(100, 1, 10, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 11, 40, 1).Apply(world, rng).Applied);

        // 窗口内服务端外部变更：/give 往槽 10 塞入另一件物品（拾取 / 开袋同理）
        player.Items[10] = 757;
        player.ItemStacks[10] = 1;

        // 暂存的「清空槽 10」会销毁外部塞入的物品 → 守恒失败 → 回滚
        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(757, player.Items[10]);
        Assert.Equal(0, player.Items[11]);
    }

    /// <summary>SSC 背包守恒事务：会话切换后丢弃上一会话的暂存意图，避免重连复用槽位时串号。</summary>
    [Fact]
    public void InventoryTransaction_Drops_Staged_Values_After_Session_Switch()
    {
        var world = new WorldState { Tick = 100 };
        var player = new PlayerRuntime { Id = 1, Active = true, SessionId = 7 };
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        Assert.True(new StageInventorySlotCommand(100, 1, 3, 757, 1).Apply(world, rng).Applied);
        Assert.Single(player.PendingInventoryChanges);

        // 重连拿到同一槽位（新会话）→ 旧暂存意图必须丢弃
        player.SessionId = 8;
        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.None, world.TryCommitInventoryTransaction(1, 15));
        Assert.Empty(player.PendingInventoryChanges);
        Assert.Equal(0, player.Items[3]);
    }

    /// <summary>
    /// SSC 背包守恒事务：原版合成（3 铜矿 → 1 铜锭）改变背包总量，但净增量可由原版配方解释
    /// → 必须提交（此前判据只看「总量不变」，把合成当成凭空造物整窗回滚，表现为合成后物品被吃掉）。
    /// 铜锭需要熔炉（图格 17）→ 用例先在玩家可达区域内放一块熔炉。
    /// </summary>
    [Fact]
    public void InventoryTransaction_Commits_Recipe_Explained_Craft()
    {
        var world = new WorldState { Tick = 100, Tiles = new TileMap(32, 32) };
        var player = new PlayerRuntime { Id = 1, Active = true };
        PlaceCraftingStation(world, player, tileType: 17);
        player.Items[50] = 12;   // 铜矿
        player.ItemStacks[50] = 3;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        // 客户端合成：槽 50 的 3 个铜矿清空，槽 51 出现 1 个铜锭
        Assert.True(new StageInventorySlotCommand(100, 1, 50, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 51, 20, 1).Apply(world, rng).Applied);

        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.Committed, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(0, player.Items[50]);
        Assert.Equal(20, player.Items[51]);
        Assert.Equal(1, player.ItemStacks[51]);
    }

    /// <summary>
    /// SSC 背包守恒事务：材料齐备但**不在合成站旁** → 配方前置条件不满足 → 回滚
    /// （否则客户端只要凑齐材料就能在任意位置合成高阶物品）。
    /// </summary>
    [Fact]
    public void InventoryTransaction_RollsBack_Craft_Without_Station()
    {
        var world = new WorldState { Tick = 100, Tiles = new TileMap(32, 32) };
        var player = new PlayerRuntime { Id = 1, Active = true };
        player.Items[50] = 12;
        player.ItemStacks[50] = 3;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        Assert.True(new StageInventorySlotCommand(100, 1, 50, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 51, 20, 1).Apply(world, rng).Applied);

        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(12, player.Items[50]);
        Assert.Equal(0, player.Items[51]);
    }

    /// <summary>
    /// SSC 背包守恒事务：合成站**超出可达区域**（原版 Simple 判定为 X ±5 / Y ±3 图格）同样不满足前置条件。
    /// </summary>
    [Fact]
    public void InventoryTransaction_RollsBack_Craft_Station_Out_Of_Reach()
    {
        var world = new WorldState { Tick = 100, Tiles = new TileMap(32, 32) };
        var player = new PlayerRuntime { Id = 1, Active = true };
        var (sx, sy) = PlaceCraftingStation(world, player, tileType: 17);
        world.Tiles[sx + 6, sy] = new Tile { Active = true, Type = 17 };   // 熔炉挪到可达区域外
        world.Tiles[sx, sy] = Tile.Empty;
        player.Items[50] = 12;
        player.ItemStacks[50] = 3;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        Assert.True(new StageInventorySlotCommand(100, 1, 50, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 51, 20, 1).Apply(world, rng).Applied);

        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(12, player.Items[50]);
    }

    /// <summary>
    /// 合成环境快照（原版 <c>AdjTiles()</c>）：可达区域内的合成站（含 <c>TileCountsAs</c> 等价图格）
    /// 与相邻液体被正确采集，区域外的不算。
    /// </summary>
    [Fact]
    public void CraftingEnvironmentSampler_Tracks_Station_And_Liquids()
    {
        var world = new WorldState { Tiles = new TileMap(32, 32) };
        var player = new PlayerRuntime { Id = 1, Active = true };
        var environment = player.CraftingEnvironment;

        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.False(environment.Satisfies(17, CraftEnvironment.None));   // 空旷：熔炉不可用
        Assert.True(environment.Satisfies(-1, CraftEnvironment.None));    // 手工合成始终可用

        var (sx, sy) = PlaceCraftingStation(world, player, tileType: 17);
        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.True(environment.Satisfies(17, CraftEnvironment.None));

        // 等价图格：355（炼金台）算作 13（瓶子）——原版 Recipe.TileCountsAs
        world.Tiles[sx, sy] = new Tile { Active = true, Type = 355 };
        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.True(environment.Satisfies(13, CraftEnvironment.None));

        // 水源：算作水源的图格（172 水槽）与满水液体格都满足 needWater
        world.Tiles[sx, sy] = Tile.Empty;
        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.False(environment.Satisfies(-1, CraftEnvironment.Water));

        world.Tiles[sx, sy] = new Tile { Active = true, Type = 172 };
        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.True(environment.Satisfies(-1, CraftEnvironment.Water));

        world.Tiles[sx, sy] = new Tile { Active = false, Liquid = 255, LiquidType = 1 };   // 满格岩浆
        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.False(environment.Water);
        Assert.True(environment.Satisfies(-1, CraftEnvironment.Lava));
    }

    /// <summary>
    /// 合成环境快照的场景度量（原版 <c>SceneMetrics.ScanTiles</c>）：以玩家所在图格为中心的固定
    /// 169×124 图格扫描区内，雪原图格 ≥ 1500 → <c>ZoneSnow</c>；墓碑数 − 向日葵数/2 ≥ 28 → <c>ZoneGraveyard</c>。
    /// 阈值两侧都验证（含向日葵抵消墓碑的原版口径）。
    /// </summary>
    [Fact]
    public void CraftingEnvironmentSampler_Tracks_Snow_And_Graveyard_Biomes()
    {
        var world = new WorldState { Tiles = new TileMap(200, 200) };
        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Position = new Vector2(100 * 16f, 100 * 16f),
        };
        var environment = player.CraftingEnvironment;

        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.False(environment.SnowBiome);
        Assert.False(environment.GraveyardBiome);

        int centerX = ((int)(player.Position.X + NpcSizes.PlayerWidth / 2f)) >> 4;
        int centerY = ((int)(player.Position.Y + NpcSizes.PlayerHeight / 2f)) >> 4;
        int zoneLeft = centerX - CraftingEnvironmentSampler.ZoneScanWidth / 2;
        int zoneTop = centerY - CraftingEnvironmentSampler.ZoneScanHeight / 2;

        void FillRow(int row, int count, int tileType)
        {
            for (int i = 0; i < count; i++)
                world.Tiles[zoneLeft + i, zoneTop + row] = new Tile { Active = true, Type = (ushort)tileType };
        }

        // 雪原图格（雪块 147）：1499 格 < 1500 → 不算雪原；补齐到 1500 → 算雪原。
        FillRow(0, 169, 147);
        FillRow(1, 169, 147);
        FillRow(2, 169, 147);
        FillRow(3, 169, 147);
        FillRow(4, 169, 147);
        FillRow(5, 169, 147);
        FillRow(6, 169, 147);
        FillRow(7, 169, 147);
        FillRow(8, 147, 147);   // 8×169 + 147 = 1499
        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.False(environment.SnowBiome);
        Assert.False(environment.Satisfies(-1, CraftEnvironment.SnowBiome));

        FillRow(8, 148, 147);   // 1500
        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.True(environment.SnowBiome);
        Assert.True(environment.Satisfies(-1, CraftEnvironment.SnowBiome));

        // 墓地：27 墓碑 − 2 向日葵/2 = 26 < 28 → 不算墓地；29 墓碑 − 1 = 28 → 算墓地。
        FillRow(120, 27, 85);
        FillRow(121, 2, 27);
        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.False(environment.GraveyardBiome);
        Assert.False(environment.Satisfies(-1, CraftEnvironment.GraveyardBiome));

        FillRow(120, 29, 85);
        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.True(environment.GraveyardBiome);
        Assert.True(environment.Satisfies(-1, CraftEnvironment.GraveyardBiome));
    }

    /// <summary>
    /// 合成环境快照的另外两类前置条件：世界特性（原版 <c>SpecialSeedFeatures.Mechdusa</c>）与
    /// 玩家解锁状态（原版 <c>Player.unlockedBiomeTorches</c>，由包 4 上报）——两者都由服务端持为状态位。
    /// </summary>
    [Fact]
    public void CraftingEnvironmentSampler_Tracks_Mechdusa_And_TorchGodsFavor()
    {
        var world = new WorldState { Tiles = new TileMap(32, 32) };
        var player = new PlayerRuntime { Id = 1, Active = true };
        var environment = player.CraftingEnvironment;

        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.False(environment.Mechdusa);
        Assert.False(environment.TorchGodsFavor);
        Assert.False(environment.Satisfies(-1, CraftEnvironment.Mechdusa));
        Assert.False(environment.Satisfies(-1, CraftEnvironment.TorchGodsFavor));

        world.MechdusaFeature = true;
        player.UnlockedBiomeTorches = true;
        CraftingEnvironmentSampler.Refresh(world, player);
        Assert.True(environment.Mechdusa);
        Assert.True(environment.TorchGodsFavor);
        Assert.True(environment.Satisfies(-1, CraftEnvironment.Mechdusa | CraftEnvironment.TorchGodsFavor));
    }

    /// <summary>
    /// SSC 背包守恒事务：纯消耗（喝药水 / 投掷物 / 一次性道具）只减不增，不可能借此凭空造物
    /// → 放行；原版「丢弃物品」（包 21 生成世界掉落物 + 包 5 清空槽位）也依赖这一条才不会回滚。
    /// </summary>
    [Fact]
    public void InventoryTransaction_Commits_Pure_Consumption()
    {
        var world = new WorldState { Tick = 100 };
        var player = new PlayerRuntime { Id = 1, Active = true };
        player.Items[50] = 28;   // 弱效治疗药水
        player.ItemStacks[50] = 1;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        Assert.True(new StageInventorySlotCommand(100, 1, 50, 0, 0).Apply(world, rng).Applied);

        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.Committed, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(0, player.Items[50]);
        Assert.Equal(0, player.ItemStacks[50]);
    }

    /// <summary>
    /// SSC 背包守恒事务：站位齐备但**材料不足**的「合成」（只有 2 铜矿却报出 1 铜锭）无法被配方解释 → 回滚。
    /// </summary>
    [Fact]
    public void InventoryTransaction_RollsBack_Craft_Short_Of_Materials()
    {
        var world = new WorldState { Tick = 100, Tiles = new TileMap(32, 32) };
        var player = new PlayerRuntime { Id = 1, Active = true };
        PlaceCraftingStation(world, player, tileType: 17);
        player.Items[50] = 12;
        player.ItemStacks[50] = 2;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        Assert.True(new StageInventorySlotCommand(100, 1, 50, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 51, 20, 1).Apply(world, rng).Applied);

        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(12, player.Items[50]);
        Assert.Equal(2, player.ItemStacks[50]);
        Assert.Equal(0, player.Items[51]);
    }

    /// <summary>
    /// SSC 背包守恒事务：合成产物恒为 0 前缀 —— 原材料被消耗、却凭空出现带前缀的同名物品
    /// （把普通产物「洗」成传奇等）一律判不守恒。
    /// </summary>
    [Fact]
    public void InventoryTransaction_RollsBack_Forged_Prefixed_Item()
    {
        var world = new WorldState { Tick = 100 };
        var player = new PlayerRuntime { Id = 1, Active = true };
        player.Items[50] = 12;
        player.ItemStacks[50] = 3;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        Assert.True(new StageInventorySlotCommand(100, 1, 50, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 51, 20, 1, 5).Apply(world, rng).Applied);

        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(12, player.Items[50]);
        Assert.Equal(0, player.Items[51]);
    }

    /// <summary>合成 / 消耗判据的纯函数用例：总量完全一致走快路径。</summary>
    [Fact]
    public void CraftingConservation_Fast_Path_Requires_Exact_Totals()
    {
        var authoritative = new Dictionary<(int ItemId, byte Prefix), int> { [(12, 0)] = 3 };
        var same = new Dictionary<(int ItemId, byte Prefix), int> { [(12, 0)] = 3 };
        var moved = new Dictionary<(int ItemId, byte Prefix), int> { [(12, (byte)5)] = 3 };

        Assert.True(CraftingConservation.IsConserved(authoritative, same, allowConsumption: false));
        Assert.False(CraftingConservation.IsConserved(authoritative, moved, allowConsumption: true));
    }

    /// <summary>
    /// 合成 / 消耗判据：配方组按「组内任意成员合计」扣减 —— 火把是
    /// 1 木材（Wood 组任意木种）+ 1 凝胶 → 3 火把，用黑檀木也应能解释通。
    /// </summary>
    [Fact]
    public void CraftingConservation_Explains_Recipe_With_Group_Requirement()
    {
        var authoritative = new Dictionary<(int ItemId, byte Prefix), int> { [(619, 0)] = 2, [(23, 0)] = 1 };
        var proposed = new Dictionary<(int ItemId, byte Prefix), int> { [(619, 0)] = 1, [(8, 0)] = 3 };

        Assert.True(CraftingConservation.IsConserved(authoritative, proposed, allowConsumption: false));
    }

    /// <summary>合成 / 消耗判据：带非 0 前缀的「增加」一律拒绝。</summary>
    [Fact]
    public void CraftingConservation_Rejects_NonZero_Prefix_Creation()
    {
        var authoritative = new Dictionary<(int ItemId, byte Prefix), int> { [(12, 0)] = 3 };
        var proposed = new Dictionary<(int ItemId, byte Prefix), int> { [(20, (byte)5)] = 1 };

        Assert.False(CraftingConservation.IsConserved(authoritative, proposed, allowConsumption: true));
    }

    /// <summary>合成 / 消耗判据：纯消耗仅在 <c>allowConsumption</c> 打开时放行（背包放行、箱子不放行）。</summary>
    [Fact]
    public void CraftingConservation_Pure_Consumption_Follows_AllowFlag()
    {
        var authoritative = new Dictionary<(int ItemId, byte Prefix), int> { [(28, 0)] = 1 };
        var empty = new Dictionary<(int ItemId, byte Prefix), int>();

        Assert.True(CraftingConservation.IsConserved(authoritative, empty, allowConsumption: true));
        Assert.False(CraftingConservation.IsConserved(authoritative, empty, allowConsumption: false));
    }

    /// <summary>合成 / 消耗判据：不放行消耗时，未被配方解释的多余「减少」也必须回滚（防顺手销毁箱内物品）。</summary>
    [Fact]
    public void CraftingConservation_Requires_All_Removals_Explained_When_Consumption_Disabled()
    {
        // 4 铜矿 → 1 铜锭：配方只吃 3 个，多出的 1 个铜矿属于未解释的减少
        var authoritative = new Dictionary<(int ItemId, byte Prefix), int> { [(12, 0)] = 4 };
        var proposed = new Dictionary<(int ItemId, byte Prefix), int> { [(20, 0)] = 1 };

        Assert.False(CraftingConservation.IsConserved(authoritative, proposed, allowConsumption: false));
        Assert.True(CraftingConservation.IsConserved(authoritative, proposed, allowConsumption: true));
    }

    /// <summary>
    /// 合成 / 消耗判据：消耗上限按窗口开始时的权威总量计 —— 窗口期内服务端外部塞入的物品
    /// （/give、拾取、开袋）不在基准内，客户端「清空该槽」的暂存意图会超出上限而被判不守恒。
    /// </summary>
    [Fact]
    public void CraftingConservation_Removal_Limit_Rejects_Externally_Added_Items()
    {
        var authoritative = new Dictionary<(int ItemId, byte Prefix), int> { [(40, 0)] = 5 };
        var empty = new Dictionary<(int ItemId, byte Prefix), int>();
        var emptyBaseline = new Dictionary<int, int>();

        Assert.False(CraftingConservation.IsConserved(authoritative, empty, allowConsumption: true, emptyBaseline));
        Assert.True(CraftingConservation.IsConserved(authoritative, empty, allowConsumption: true));
    }

    /// <summary>配方表抽取自检：关键配方（铜锭 / 天顶剑）与派生反向配方（墙 → 块 / 平台 → 材料）必须与原始材料一致。</summary>
    [Fact]
    public void RecipeTable_Extracts_Key_Vanilla_Recipes()
    {
        Assert.True(RecipeTable.All.Length > 3300, "配方表条目过少，抽取可能失真");
        Assert.Equal(34, RecipeTable.Groups.Length);

        Assert.True(RecipeTable.TryGetRecipes(20, out var bar));   // 铜锭
        Assert.Contains(bar, r => r.ProductStack == 1 &&
            r.Requirements.Length == 1 && r.Requirements[0] is { ItemId: 12, GroupId: -1, Stack: 3 });

        Assert.True(RecipeTable.TryGetRecipes(4956, out var zenith));   // 天顶剑
        Assert.Contains(zenith, r => r.Requirements.Length == 10);

        // 反向墙（CreateReverseWallRecipes 派生）：4 个土墙（30）→ 1 个土块（2），沿用工作台（18）
        Assert.True(RecipeTable.TryGetRecipes(2, out var dirtBlock));
        Assert.Contains(dirtBlock, r => r.RequiredTile == 18 && r.Requirements.Length == 1 &&
            r.Requirements[0] is { ItemId: 30, GroupId: -1, Stack: 4 });

        // 反向平台（CreateReversePlatformRecipes 派生）：2 个木平台（94）→ 1 个木材（9）
        Assert.True(RecipeTable.TryGetRecipes(9, out var wood));
        Assert.Contains(wood, r => r.Requirements.Length == 1 &&
            r.Requirements[0] is { ItemId: 94, GroupId: -1, Stack: 2 });
    }

    /// <summary>
    /// SSC 背包守恒事务：原版**派生反向配方**（4 土墙 → 1 土块，工作台旁）必须被提交 ——
    /// 这类「墙拆回块 / 平台拆回材料」的配方由原版 CreateReverseWallRecipes / CreateReversePlatformRecipes 派生，
    /// 未收录时玩家的合法拆解会被整窗回滚。
    /// </summary>
    [Fact]
    public void InventoryTransaction_Commits_Derived_Reverse_Wall_Recipe()
    {
        var world = new WorldState { Tick = 100, Tiles = new TileMap(32, 32) };
        var player = new PlayerRuntime { Id = 1, Active = true };
        PlaceCraftingStation(world, player, tileType: 18);   // 工作台
        player.Items[50] = 30;   // 土墙
        player.ItemStacks[50] = 4;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        Assert.True(new StageInventorySlotCommand(100, 1, 50, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 51, 2, 1).Apply(world, rng).Applied);

        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.Committed, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(0, player.Items[50]);
        Assert.Equal(2, player.Items[51]);
    }

    /// <summary>
    /// SSC 背包守恒事务：墓地配方（木架 1389 ← 木材 9，骨焊机 300 旁）——材料与站位都满足，
    /// 但**不在墓地**时（原版 <c>needGraveyardBiome</c>）必须回滚；放下足够墓碑（≥ 28，向日葵按半抵消）
    /// 后才提交。这是「只在材料与站位上校验」会漏掉的作弊面。
    /// </summary>
    [Fact]
    public void InventoryTransaction_Graveyard_Recipe_Follows_Biome()
    {
        var world = new WorldState { Tick = 100, Tiles = new TileMap(200, 200) };
        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Position = new Vector2(100 * 16f, 100 * 16f),
        };
        PlaceCraftingStation(world, player, tileType: 300);   // 骨焊机
        player.Items[50] = 9;   // 木材
        player.ItemStacks[50] = 1;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        Assert.True(new StageInventorySlotCommand(100, 1, 50, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 51, 1389, 2).Apply(world, rng).Applied);
        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(9, player.Items[50]);

        // 场景扫描区内放下 28 个墓碑 → 满足墓地前置条件 → 同一操作提交
        int centerY = ((int)(player.Position.Y + NpcSizes.PlayerHeight / 2f)) >> 4;
        int centerX = ((int)(player.Position.X + NpcSizes.PlayerWidth / 2f)) >> 4;
        int zoneLeft = centerX - CraftingEnvironmentSampler.ZoneScanWidth / 2;
        int zoneTop = centerY - CraftingEnvironmentSampler.ZoneScanHeight / 2;
        for (int i = 0; i < 28; i++)
            world.Tiles[zoneLeft + i, zoneTop + 100] = new Tile { Active = true, Type = 85 };

        Assert.True(new StageInventorySlotCommand(130, 1, 50, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(130, 1, 51, 1389, 2).Apply(world, rng).Applied);
        world.Tick = 150;
        Assert.Equal(InventoryTransactionOutcome.Committed, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(0, player.Items[50]);
        Assert.Equal(1389, player.Items[51]);
        Assert.Equal(2, player.ItemStacks[51]);
    }

    /// <summary>
    /// SSC 背包守恒事务：雪原配方（雪云块 3756 ← 冰块 751，工作台 305 旁）需要身处雪原
    /// （原版 <c>needSnowBiome</c>：场景扫描区内雪原图格 ≥ 1500）——不满足即回滚，满足才提交。
    /// </summary>
    [Fact]
    public void InventoryTransaction_Snow_Biome_Recipe_Follows_SceneMetrics()
    {
        var world = new WorldState { Tick = 100, Tiles = new TileMap(200, 200) };
        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Position = new Vector2(100 * 16f, 100 * 16f),
        };
        PlaceCraftingStation(world, player, tileType: 305);
        player.Items[50] = 751;   // 冰块
        player.ItemStacks[50] = 1;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        Assert.True(new StageInventorySlotCommand(100, 1, 50, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 51, 3756, 1).Apply(world, rng).Applied);
        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(751, player.Items[50]);

        int centerX = ((int)(player.Position.X + NpcSizes.PlayerWidth / 2f)) >> 4;
        int centerY = ((int)(player.Position.Y + NpcSizes.PlayerHeight / 2f)) >> 4;
        int zoneLeft = centerX - CraftingEnvironmentSampler.ZoneScanWidth / 2;
        int zoneTop = centerY - CraftingEnvironmentSampler.ZoneScanHeight / 2;
        for (int i = 0; i < 1500; i++)
            world.Tiles[zoneLeft + i % 169, zoneTop + 20 + i / 169] = new Tile { Active = true, Type = 147 };

        Assert.True(new StageInventorySlotCommand(130, 1, 50, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(130, 1, 51, 3756, 1).Apply(world, rng).Applied);
        world.Tick = 150;
        Assert.Equal(InventoryTransactionOutcome.Committed, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(3756, player.Items[51]);
    }

    /// <summary>
    /// SSC 背包守恒事务：火把药水（5573 ← 火把 / 瓶子系列，瓶子 13 旁）需要玩家已解锁火把神恩
    /// （原版 <c>needTorchGodsFavor</c> → <c>Player.unlockedBiomeTorches</c>，由包 4 上报）——
    /// 未解锁回滚，解锁后提交。
    /// </summary>
    [Fact]
    public void InventoryTransaction_TorchGodsFavor_Recipe_Follows_Player_Unlock()
    {
        var world = new WorldState { Tick = 100, Tiles = new TileMap(64, 64) };
        var player = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(32 * 16f, 32 * 16f) };
        PlaceCraftingStation(world, player, tileType: 13);   // 瓶子
        int[] materials = { 126, 8, 313, 314, 318 };
        for (int i = 0; i < materials.Length; i++)
        {
            player.Items[40 + i] = materials[i];
            player.ItemStacks[40 + i] = 1;
        }
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        for (int i = 0; i < materials.Length; i++)
            Assert.True(new StageInventorySlotCommand(100, 1, 40 + i, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 50, 5573, 1).Apply(world, rng).Applied);
        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(126, player.Items[40]);

        player.UnlockedBiomeTorches = true;   // 包 4 的 TorchFlags bit2
        for (int i = 0; i < materials.Length; i++)
            Assert.True(new StageInventorySlotCommand(130, 1, 40 + i, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(130, 1, 50, 5573, 1).Apply(world, rng).Applied);
        world.Tick = 150;
        Assert.Equal(InventoryTransactionOutcome.Committed, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(5573, player.Items[50]);
    }

    /// <summary>
    /// SSC 背包守恒事务：机械三王召唤物（5334 ← 秘银 / 精金系列，秘银砧 134 旁）需要世界具备
    /// 「机械三王」特性（原版 <c>needMechdusa</c> → <c>SpecialSeedFeatures.Mechdusa</c>）——
    /// 本服务端的程序化世界不产出该特性，故默认回滚；世界特性置位后提交。
    /// </summary>
    [Fact]
    public void InventoryTransaction_Mechdusa_Recipe_Follows_World_Feature()
    {
        var world = new WorldState { Tick = 100, Tiles = new TileMap(64, 64) };
        var player = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(32 * 16f, 32 * 16f) };
        PlaceCraftingStation(world, player, tileType: 134);
        player.Items[40] = 544;
        player.ItemStacks[40] = 1;
        player.Items[41] = 557;
        player.ItemStacks[41] = 1;
        player.Items[42] = 556;
        player.ItemStacks[42] = 1;
        lock (world.PlayersLock)
            world.Players[1] = player;
        var rng = new XoshiroRng(1);

        for (int slot = 40; slot <= 42; slot++)
            Assert.True(new StageInventorySlotCommand(100, 1, slot, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 50, 5334, 1).Apply(world, rng).Applied);
        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(544, player.Items[40]);

        world.MechdusaFeature = true;
        for (int slot = 40; slot <= 42; slot++)
            Assert.True(new StageInventorySlotCommand(130, 1, slot, 0, 0).Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(130, 1, 50, 5334, 1).Apply(world, rng).Applied);
        world.Tick = 150;
        Assert.Equal(InventoryTransactionOutcome.Committed, world.TryCommitInventoryTransaction(1, 15));
        Assert.Equal(5334, player.Items[50]);
    }

    /// <summary>
    /// SSC 箱子守恒事务：原版合成可取用已打开箱子的材料（箱内铜矿 → 背包铜锭），
    /// 材料的减少与产物的增加都可被配方解释 → 提交（背包侧与箱子侧一起落盘）。
    /// </summary>
    [Fact]
    public void ChestTransaction_Commits_Craft_From_Chest()
    {
        var (world, player, chest) = CreateTransferWorld();
        PlaceCraftingStation(world, player, tileType: 17);   // 铜锭需要熔炉
        chest.Items[0] = new ChestItem { Type = 12, Stack = 3 };
        world.Tick = 100;
        var rng = new XoshiroRng(1);

        Assert.True(new StageChestItemCommand(100, 1, 0, 0, 0, 0, 0) { SessionId = 22 }.Apply(world, rng).Applied);
        Assert.True(new StageInventorySlotCommand(100, 1, 9, 20, 1).Apply(world, rng).Applied);

        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.Committed, world.TryCommitChestTransaction(1, 15));
        Assert.Equal(0, chest.Items[0].Stack);
        Assert.Equal(20, player.Items[9]);
        Assert.Equal(1, player.ItemStacks[9]);
    }

    /// <summary>
    /// SSC 箱子守恒事务：箱子侧**不放行纯消耗** —— 客户端用包 32 清空箱内物品而不产生任何产物，
    /// 「减少」无法被配方解释，必须回滚（否则可静默清空箱子内容）。
    /// </summary>
    [Fact]
    public void ChestTransaction_RollsBack_Pure_Consumption()
    {
        var (world, _, chest) = CreateTransferWorld();
        chest.Items[0] = new ChestItem { Type = 12, Stack = 3 };
        world.Tick = 100;

        Assert.True(new StageChestItemCommand(100, 1, 0, 0, 0, 0, 0) { SessionId = 22 }
            .Apply(world, new XoshiroRng(1)).Applied);

        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitChestTransaction(1, 15));
        Assert.Equal(3, chest.Items[0].Stack);
    }

    /// <summary>
    /// SSC 箱子守恒事务（C1）：窗口内聚合的箱内整理（槽位移动）在守恒时整体提交，
    /// 服务端接受客户端箱子改动（总量「玩家背包 ∪ 该箱子」不变）。
    /// </summary>
    [Fact]
    public void ChestItem_Snapshot_Commits_When_Conserved()
    {
        var (world, player, chest) = CreateTransferWorld();
        chest.Items[0] = new ChestItem { Type = 50, Stack = 10, Prefix = 3 };
        world.Tick = 100;
        var rng = new XoshiroRng(1);

        // 箱内把槽 0 的 10 个移到槽 1：源槽清空 + 目标槽填入（总量不变）
        Assert.True(new StageChestItemCommand(100, 1, 0, 0, 0, 0, 0) { SessionId = 22 }.Apply(world, rng).Applied);
        Assert.True(new StageChestItemCommand(100, 1, 0, 1, 10, 3, 50) { SessionId = 22 }.Apply(world, rng).Applied);

        // 窗口未到期：不结算，权威箱子保持不变
        Assert.Equal(InventoryTransactionOutcome.None, world.TryCommitChestTransaction(1, 15));
        Assert.Equal(10, chest.Items[0].Stack);
        Assert.Equal(0, chest.Items[1].Stack);

        // 窗口到期：总量 10 不变 → 守恒 → 提交
        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.Committed, world.TryCommitChestTransaction(1, 15));
        Assert.Equal(0, chest.Items[0].Stack);
        Assert.Equal(50, chest.Items[1].Type);
        Assert.Equal(10, chest.Items[1].Stack);
        Assert.Equal(3, chest.Items[1].Prefix);
        // 提交也要回写权威槽位（幂等确认）
        var updates = world.DrainChestUpdates(8);
        Assert.Contains((0, 0), updates);
        Assert.Contains((0, 1), updates);
        _ = player;
    }

    /// <summary>
    /// SSC 箱子守恒事务（C1）：凭空造物破坏「玩家背包 ∪ 该箱子」总量守恒 → 回滚，
    /// 且通过 MarkChestChanged 把权威箱子槽位回写以纠正客户端。
    /// </summary>
    [Fact]
    public void ChestItem_Staged_Forgery_RollsBack_And_MarksChestChanged()
    {
        var (world, _, chest) = CreateTransferWorld();
        world.Tick = 100;
        var rng = new XoshiroRng(1);

        // 权威箱子为空，客户端却声称槽 5 有泰拉刃
        Assert.True(new StageChestItemCommand(100, 1, 0, 5, 1, 0, 757) { SessionId = 22 }.Apply(world, rng).Applied);
        world.Tick = 120;

        Assert.Equal(InventoryTransactionOutcome.RolledBack, world.TryCommitChestTransaction(1, 15));
        Assert.Equal(0, chest.Items[5].Stack);
        // 回滚后仍要回写权威值（客户端被纠正回空槽）
        Assert.Contains((0, 5), world.DrainChestUpdates(8));
    }

    /// <summary>SSC 箱子守恒事务：未打开该箱子（无会话）时暂存命令被拒绝，权威箱子不变。</summary>
    [Fact]
    public void ChestItem_Staged_Change_Without_Open_Session_Is_Rejected()
    {
        var (world, _, chest) = CreateTransferWorld(openSession: false);

        var result = new StageChestItemCommand(100, 1, 0, 2, 4, 0, 50) { SessionId = 22 }
            .Apply(world, new XoshiroRng(1));

        Assert.False(result.Applied);
        Assert.Equal(CommandFailures.ChestNotOpen, result.Reason);
        Assert.Equal(0, chest.Items[2].Stack);
        Assert.Empty(world.DrainChestUpdates(8));
    }

    /// <summary>SSC 箱子守恒事务：会话切换后丢弃上一会话的暂存意图，避免重连复用槽位时串号。</summary>
    [Fact]
    public void ChestItem_Staged_Drops_After_Session_Switch()
    {
        var (world, player, chest) = CreateTransferWorld();
        world.Tick = 100;
        Assert.True(new StageChestItemCommand(100, 1, 0, 5, 1, 0, 757) { SessionId = 22 }
            .Apply(world, new XoshiroRng(1)).Applied);
        Assert.Single(player.PendingChestChanges);

        player.SessionId = 23;
        world.Tick = 120;
        Assert.Equal(InventoryTransactionOutcome.None, world.TryCommitChestTransaction(1, 15));
        Assert.Empty(player.PendingChestChanges);
        Assert.Equal(0, chest.Items[5].Stack);
    }

    /// <summary>
    /// C3：包 85 QuickStackChests 只处理客户端指定的来源槽位（未指定的槽位保持不动），
    /// smartStack=false → DepositAll 语义（可填入空槽）。
    /// </summary>
    [Fact]
    public void QuickStackChests_Command_Deposits_Only_Source_Slots()
    {
        var (world, player, chest) = CreateTransferWorld();
        player.Items[9] = 50; player.ItemStacks[9] = 5; player.ItemPrefixes[9] = 3;
        player.Items[10] = 50; player.ItemStacks[10] = 7; player.ItemPrefixes[10] = 3;
        chest.Items[0] = new ChestItem { Type = 50, Stack = 1, Prefix = 3 };

        var command = new BulkInventoryChestCommand(1, 1, 0, ChestBulkOperation.DepositAll, 300)
        {
            SessionId = 22,
            SourceSlots = new[] { 9 },
        };

        Assert.True(command.Apply(world, new XoshiroRng(1)).Applied);
        Assert.Equal(0, player.ItemStacks[9]);    // 指定来源槽已入库
        Assert.Equal(6, chest.Items[0].Stack);    // 1 + 5
        Assert.Equal(7, player.ItemStacks[10]);   // 未指定的来源槽保持不动
    }

    /// <summary>C3：包 85 真实线格式解码（Int32 数量 + Int16 槽位 + Boolean smartStack）。</summary>
    [Fact]
    public void QuickStackChests_Decode_Reads_Slots_And_SmartStack()
    {
        var payload = new byte[4 + 2 + 2 + 1];
        BitConverter.GetBytes(2).CopyTo(payload, 0);
        BitConverter.GetBytes((short)9).CopyTo(payload, 4);
        BitConverter.GetBytes((short)10).CopyTo(payload, 6);
        payload[8] = 1;

        var decoder = new PacketDecoder();
        var packet = Assert.IsType<QuickStackChestsPacket>(
            decoder.Decode(PacketId.QuickStackChests, payload, new DecodeContext()));

        Assert.Equal(new[] { 9, 10 }, packet.Slots);
        Assert.True(packet.SmartStack);

        // payload 长度 0 → 空列表（无 smartStack 字节）
        var empty = Assert.IsType<QuickStackChestsPacket>(
            decoder.Decode(PacketId.QuickStackChests, ReadOnlySpan<byte>.Empty, new DecodeContext()));
        Assert.Empty(empty.Slots);
        Assert.False(empty.SmartStack);
    }

    [Fact]
    public void Existing_Projectile_Update_Preserves_Server_Authoritative_State()
    {
        var world = new WorldState();
        var projectile = new ProjectileEntity
        {
            Key = 7,
            Owner = 1,
            Type = 1,
            Position = new Vector2(10, 20),
            Velocity = new Vector2(1, 2),
            Active = true,
        };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true };
        lock (world.ProjectilesLock)
            world.Projectiles.Add(projectile);

        var result = new SpawnProjectileCommand(1, 1, 7, 1,
            new Vector2(1000, 2000), new Vector2(100, 200), 10).Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        Assert.Equal(new Vector2(10, 20), projectile.Position);
        Assert.Equal(new Vector2(1, 2), projectile.Velocity);
        Assert.True(projectile.Active);
    }

    [Fact]
    public void ProjectileKeys_Are_Isolated_By_Owner()
    {
        var world = new WorldState { MaxTilesX = 100, MaxTilesY = 100 };
        lock (world.PlayersLock)
        {
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true };
            world.Players[2] = new PlayerRuntime { Id = 2, Active = true };
        }

        var rng = new XoshiroRng(1);
        var firstPosition = new Vector2(10, 20);
        var secondPosition = new Vector2(30, 40);

        Assert.True(new SpawnProjectileCommand(1, 1, 7, 1, firstPosition, new Vector2(1, 0), 10)
            .Apply(world, rng).Applied);
        Assert.True(new SpawnProjectileCommand(2, 2, 7, 1, secondPosition, new Vector2(0, 1), 10)
            .Apply(world, rng).Applied);

        lock (world.ProjectilesLock)
        {
            Assert.Equal(2, world.Projectiles.Count);
            Assert.Equal(firstPosition, world.Projectiles.Single(p => p.Owner == 1 && p.Key == 7).Position);
            Assert.Equal(secondPosition, world.Projectiles.Single(p => p.Owner == 2 && p.Key == 7).Position);
        }

        var updatedPosition = new Vector2(50, 60);
        Assert.True(new SpawnProjectileCommand(3, 2, 7, 1, updatedPosition, new Vector2(0, 2), 10)
            .Apply(world, rng).Applied);
        Assert.True(new KillProjectileCommand(4, 2, 7, updatedPosition).Apply(world, rng).Applied);

        lock (world.ProjectilesLock)
        {
            var first = world.Projectiles.Single(p => p.Owner == 1 && p.Key == 7);
            var second = world.Projectiles.Single(p => p.Owner == 2 && p.Key == 7);
            Assert.True(first.Active);
            Assert.Equal(firstPosition, first.Position);
            Assert.False(second.Active);
            // 包 27 重试不会覆盖服务端推进的位置；销毁包的位置字段也不是权威状态。
            Assert.Equal(secondPosition, second.Position);
        }
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
    public void PersistChests_CanBeRequeued_AfterFlushFailure()
    {
        var world = new WorldState();
        world.MarkPersistChest(3);
        world.MarkPersistChest(7);

        var drained = world.DrainPersistChests(10);
        Assert.Equal(new[] { 3, 7 }, drained.Order());
        Assert.False(world.HasPendingPersist);

        foreach (int index in drained) world.MarkPersistChest(index);

        Assert.Equal(new[] { 3, 7 }, world.DrainPersistChests(10).Order());
        Assert.False(world.HasPendingPersist);
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
    public void TransferInventoryChest_ChestToInventory_PartialTransfer_ConservesItems()
    {
        var (world, player, chest) = CreateTransferWorld();
        chest.Items[2] = new ChestItem { Type = 50, Stack = 10, Prefix = 3 };

        var result = new TransferInventoryChestItemCommand(1, 1, true, 0, 4, 2, 4, 100, 50, 3, 10)
        { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        Assert.Equal(6, chest.Items[2].Stack);
        Assert.Equal(50, player.Items[4]);
        Assert.Equal(4, player.ItemStacks[4]);
        Assert.Equal(3, player.ItemPrefixes[4]);
        Assert.Contains((0, 2), world.DrainChestUpdates(10));
        Assert.Contains((1, 22L, 4), world.DrainInventoryUpdates(10));
    }

    [Fact]
    public void TransferInventoryChest_InventoryToChest_MergesAndConservesItems()
    {
        var (world, player, chest) = CreateTransferWorld();
        player.Items[4] = 50;
        player.ItemStacks[4] = 7;
        player.ItemPrefixes[4] = 3;
        chest.Items[2] = new ChestItem { Type = 50, Stack = 4, Prefix = 3 };

        var result = new TransferInventoryChestItemCommand(1, 1, false, 0, 4, 2, 3, 101, 50, 3, 7)
        { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        Assert.Equal(4, player.ItemStacks[4]);
        Assert.Equal(7, chest.Items[2].Stack);
        Assert.Equal(11, player.ItemStacks[4] + chest.Items[2].Stack);
    }

    [Fact]
    public void TransferInventoryChest_ClosedSession_IsRejectedWithoutChanges()
    {
        var (world, player, chest) = CreateTransferWorld(openSession: false);
        chest.Items[2] = new ChestItem { Type = 50, Stack = 10, Prefix = 3 };

        var result = new TransferInventoryChestItemCommand(1, 1, true, 0, 4, 2, 4, 102, 50, 3, 10)
        { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.False(result.Applied);
        Assert.Equal(CommandFailures.ChestNotOpen, result.Reason);
        Assert.Equal(10, chest.Items[2].Stack);
        Assert.Equal(0, player.ItemStacks[4]);
    }

    [Fact]
    public void TransferInventoryChest_ReplayedOperation_IsRejectedWithoutChanges()
    {
        var (world, player, chest) = CreateTransferWorld();
        chest.Items[2] = new ChestItem { Type = 50, Stack = 10, Prefix = 3 };
        var command = new TransferInventoryChestItemCommand(1, 1, true, 0, 4, 2, 4, 103, 50, 3, 10)
        { SessionId = 22 };

        Assert.True(command.Apply(world, new XoshiroRng(1)).Applied);
        var replay = command.Apply(world, new XoshiroRng(1));

        Assert.False(replay.Applied);
        Assert.Equal("replayed_operation", replay.Reason);
        Assert.Equal(6, chest.Items[2].Stack);
        Assert.Equal(4, player.ItemStacks[4]);
    }

    [Fact]
    public void TransferInventoryChest_StaleSession_IsRejectedWithoutChanges()
    {
        var (world, player, chest) = CreateTransferWorld();
        chest.Items[2] = new ChestItem { Type = 50, Stack = 10, Prefix = 3 };

        var result = new TransferInventoryChestItemCommand(1, 1, true, 0, 4, 2, 4, 104, 50, 3, 10)
        { SessionId = 11 }.Apply(world, new XoshiroRng(1));

        Assert.False(result.Applied);
        Assert.Equal(CommandFailures.StaleSession, result.Reason);
        Assert.Equal(10, chest.Items[2].Stack);
        Assert.Equal(0, player.ItemStacks[4]);
    }

    [Fact]
    public void BulkChest_LootAll_IsAtomicAndReplaySafe()
    {
        var (world, player, chest) = CreateTransferWorld();
        chest.Items[0] = new ChestItem { Type = 50, Stack = 10, Prefix = 3 };
        chest.Items[1] = new ChestItem { Type = 51, Stack = 4, Prefix = 0 };

        var command = new BulkInventoryChestCommand(
            1, 1, 0, ChestBulkOperation.LootAll, 200)
        { SessionId = 22 };

        Assert.True(command.Apply(world, new XoshiroRng(1)).Applied);
        Assert.Equal(10, player.ItemStacks[9]);
        Assert.Equal(50, player.Items[9]);
        Assert.Equal(4, player.ItemStacks[10]);
        Assert.Equal(0, chest.Items[0].Stack);
        Assert.Equal(0, chest.Items[1].Stack);

        var replay = command.Apply(world, new XoshiroRng(1));
        Assert.False(replay.Applied);
        Assert.Equal("replayed_operation", replay.Reason);
    }

    [Fact]
    public void BulkChest_LootAll_WithFullInventory_PreservesUnmovedItems()
    {
        var (world, player, chest) = CreateTransferWorld();
        for (int slot = 10; slot <= 49; slot++)
        {
            player.Items[slot] = 1000 + slot;
            player.ItemStacks[slot] = 999;
        }
        player.Items[9] = 50;
        player.ItemStacks[9] = 995;
        player.ItemPrefixes[9] = 3;
        chest.Items[0] = new ChestItem { Type = 50, Stack = 10, Prefix = 3 };

        var result = new BulkInventoryChestCommand(
            1, 1, 0, ChestBulkOperation.LootAll, 208)
        { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        Assert.Equal(999, player.ItemStacks[9]);
        Assert.Equal(6, chest.Items[0].Stack);
        Assert.Equal(3, chest.Items[0].Prefix);
        Assert.Equal(1005, player.ItemStacks[9] + chest.Items[0].Stack);
    }

    [Fact]
    public void BulkChest_DepositAll_MergesAndPreservesUnmovedItems()
    {
        var (world, player, chest) = CreateTransferWorld();
        player.Items[9] = 50;
        player.ItemStacks[9] = 7;
        player.ItemPrefixes[9] = 3;
        player.Items[10] = 51;
        player.ItemStacks[10] = 4;
        chest.Items[2] = new ChestItem { Type = 50, Stack = 4, Prefix = 3 };

        var result = new BulkInventoryChestCommand(
            1, 1, 0, ChestBulkOperation.DepositAll, 201)
        { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        Assert.Equal(11, chest.Items[2].Stack);
        Assert.Equal(0, player.ItemStacks[9]);
        Assert.Equal(0, player.ItemStacks[10]);
        Assert.Equal(51, chest.Items[0].Type);
        Assert.Equal(4, chest.Items[0].Stack);
    }

    [Fact]
    public void BulkChest_QuickStack_OnlyMovesIntoExistingStacks()
    {
        var (world, player, chest) = CreateTransferWorld();
        player.Items[9] = 50;
        player.ItemStacks[9] = 7;
        player.Items[10] = 51;
        player.ItemStacks[10] = 4;
        chest.Items[2] = new ChestItem { Type = 50, Stack = 4 };

        var result = new BulkInventoryChestCommand(
            1, 1, 0, ChestBulkOperation.QuickStack, 202)
        { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        Assert.Equal(11, chest.Items[2].Stack);
        Assert.Equal(0, player.ItemStacks[9]);
        Assert.Equal(4, player.ItemStacks[10]);
        Assert.Equal(51, player.Items[10]);
    }

    [Theory]
    [InlineData(ChestBulkOperation.DepositAll)]
    [InlineData(ChestBulkOperation.QuickStack)]
    public void BulkChest_DoesNotMergeDifferentPrefixes(ChestBulkOperation operation)
    {
        var (world, player, chest) = CreateTransferWorld();
        player.Items[9] = 50;
        player.ItemStacks[9] = 7;
        player.ItemPrefixes[9] = 3;
        chest.Items[0] = new ChestItem { Type = 50, Stack = 4, Prefix = 4 };
        if (operation == ChestBulkOperation.QuickStack)
            chest.Items[1] = new ChestItem { Type = 50, Stack = 1, Prefix = 3 };

        var result = new BulkInventoryChestCommand(
            1, 1, 0, operation, 209)
        { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        Assert.Equal(4, chest.Items[0].Stack);
        Assert.Equal(4, chest.Items[0].Prefix);
        if (operation == ChestBulkOperation.DepositAll)
        {
            Assert.Equal(7, chest.Items[1].Stack);
            Assert.Equal(3, chest.Items[1].Prefix);
            Assert.Equal(0, player.ItemStacks[9]);
        }
        else
        {
            Assert.Equal(8, chest.Items[1].Stack);
            Assert.Equal(3, chest.Items[1].Prefix);
            Assert.Equal(0, player.ItemStacks[9]);
        }
    }

    [Fact]
    public void BulkChest_DepositAll_RespectsStackLimitAndConservesItems()
    {
        var (world, player, chest) = CreateTransferWorld();
        player.Items[9] = 50;
        player.ItemStacks[9] = 10;
        player.ItemPrefixes[9] = 3;
        chest.Items[0] = new ChestItem { Type = 50, Stack = 995, Prefix = 3 };
        for (int slot = 1; slot < chest.Items.Length; slot++)
            chest.Items[slot] = new ChestItem { Type = 1000 + slot, Stack = 999 };

        var result = new BulkInventoryChestCommand(
            1, 1, 0, ChestBulkOperation.DepositAll, 210)
        { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        Assert.Equal(999, chest.Items[0].Stack);
        Assert.Equal(6, player.ItemStacks[9]);
        Assert.Equal(1005, chest.Items[0].Stack + player.ItemStacks[9]);
    }

    [Fact]
    public void BulkChest_NoChange_DoesNotConsumeOperationOrQueueUpdates()
    {
        var (world, player, chest) = CreateTransferWorld();
        player.Items[9] = 50;
        player.ItemStacks[9] = 7;
        player.ItemPrefixes[9] = 3;
        chest.Items[0] = new ChestItem { Type = 50, Stack = 4, Prefix = 4 };
        var command = new BulkInventoryChestCommand(
            1, 1, 0, ChestBulkOperation.QuickStack, 211)
        { SessionId = 22 };

        var noChange = command.Apply(world, new XoshiroRng(1));

        Assert.False(noChange.Applied);
        Assert.Equal(CommandFailures.NoChange, noChange.Reason);
        Assert.False(world.HasAppliedInventoryChestOperation(1, 22, 211));
        Assert.Empty(world.DrainPersistChests(10));
        Assert.Empty(world.DrainChestUpdates(10));
        Assert.Empty(world.DrainInventoryUpdates(10));

        chest.Items[0].Prefix = 3;
        Assert.True(command.Apply(world, new XoshiroRng(1)).Applied);
    }

    [Fact]
    public void BulkChest_DepositAll_DoesNotMoveEquipmentOrAmmoSlots()
    {
        var (world, player, chest) = CreateTransferWorld();
        player.Items[0] = 50;
        player.ItemStacks[0] = 1;
        player.Items[50] = 51;
        player.ItemStacks[50] = 2;
        player.Items[9] = 52;
        player.ItemStacks[9] = 3;

        var result = new BulkInventoryChestCommand(
            1, 1, 0, ChestBulkOperation.DepositAll, 204)
        { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        Assert.Equal(50, player.Items[0]);
        Assert.Equal(1, player.ItemStacks[0]);
        Assert.Equal(51, player.Items[50]);
        Assert.Equal(2, player.ItemStacks[50]);
        Assert.Equal(0, player.ItemStacks[9]);
        Assert.Equal(52, chest.Items[0].Type);
        Assert.Equal(3, chest.Items[0].Stack);
    }

    [Fact]
    public async Task BulkChest_ConcurrentPlayers_SerializeWithoutItemLoss()
    {
        var (world, player, chest) = CreateTransferWorld();
        lock (world.PlayersLock)
            world.Players[2] = new PlayerRuntime { Id = 2, Active = true, SessionId = 33, Position = player.Position };
        world.OpenChestSession(2, 33, 0);
        player.Items[9] = 50;
        player.ItemStacks[9] = 10;
        world.Players[2].Items[9] = 50;
        world.Players[2].ItemStacks[9] = 10;

        var results = await Task.WhenAll(
            Task.Run(() => new BulkInventoryChestCommand(1, 1, 0, ChestBulkOperation.DepositAll, 205)
                { SessionId = 22 }.Apply(world, new XoshiroRng(1))),
            Task.Run(() => new BulkInventoryChestCommand(1, 2, 0, ChestBulkOperation.DepositAll, 206)
                { SessionId = 33 }.Apply(world, new XoshiroRng(1))));

        Assert.All(results, result => Assert.True(result.Applied));
        Assert.Equal(20, chest.Items[0].Stack);
        Assert.Equal(0, player.ItemStacks[9]);
        Assert.Equal(0, world.Players[2].ItemStacks[9]);
    }

    [Fact]
    public void BulkChest_Disconnect_InvalidatesOldSession()
    {
        var (world, player, chest) = CreateTransferWorld();
        player.Items[9] = 50;
        player.ItemStacks[9] = 5;

        world.MarkPlayerOffline(1, 22, "resume-key", 20);

        var result = new BulkInventoryChestCommand(
            1, 1, 0, ChestBulkOperation.DepositAll, 207)
        { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.False(result.Applied);
        Assert.Equal(CommandFailures.PlayerNotActive, result.Reason);
        Assert.Equal(0, chest.Items.Sum(item => item.Stack));
    }

    [Fact]
    public void BulkChest_ClosedSession_IsRejectedWithoutChanges()
    {
        var (world, player, chest) = CreateTransferWorld(openSession: false);
        player.Items[9] = 50;
        player.ItemStacks[9] = 7;
        var result = new BulkInventoryChestCommand(
            1, 1, 0, ChestBulkOperation.DepositAll, 203)
        { SessionId = 22 }.Apply(world, new XoshiroRng(1));

        Assert.False(result.Applied);
        Assert.Equal(CommandFailures.ChestNotOpen, result.Reason);
        Assert.Equal(7, player.ItemStacks[9]);
        Assert.Equal(0, chest.Items.Sum(item => item.Stack));
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

    /// <summary>拾取成功 → 排队一条聊天提示（「获取 木材 ×N」，名称取中文显示名）。</summary>
    [Fact]
    public void PickupItem_Queues_Chat_Notice()
    {
        var world = new WorldState { InventoryLedger = new AcceptingInventoryLedger() };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(8, 8) };
        lock (world.ItemsLock)
            world.Items.Add(new WorldItemEntity
            {
                Slot = 0,
                ItemId = 9,
                Stack = 3,
                Position = new Vector2(8, 8),
            });

        var result = new PickupItemCommand(1, 1, 0).Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        var notice = Assert.Single(world.DrainPlayerNotices(8));
        Assert.Equal(1, notice.PlayerId);
        Assert.Equal("获取 木材 ×3", notice.Text);
        Assert.Empty(world.DrainPlayerNotices(8));   // 取走即清空
    }

    [Fact]
    public void PickupItem_Uses_ItemDisplayName_Fallback()
    {
        var world = new WorldState { InventoryLedger = new AcceptingInventoryLedger() };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(8, 8) };
        lock (world.ItemsLock)
            world.Items.Add(new WorldItemEntity
            {
                Slot = 0,
                ItemId = 99999,
                Stack = 1,
                Position = new Vector2(8, 8),
            });

        Assert.True(new PickupItemCommand(1, 1, 0).Apply(world, new XoshiroRng(1)).Applied);

        Assert.Equal("获取 Item#99999 ×1", Assert.Single(world.DrainPlayerNotices(8)).Text);
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
    public void CommandQueue_Bounded_RejectsBeyondCapacity()
    {
        var q = new CommandQueue(maxCount: 2);
        Assert.True(q.Enqueue(new TestCommand(1, 1, "a")));
        Assert.True(q.Enqueue(new TestCommand(2, 2, "b")));

        // 已满 → 拒绝入队，不增长
        Assert.False(q.Enqueue(new TestCommand(3, 3, "c")));
        Assert.Equal(2, q.Count);

        // 消费后可重新入队
        Assert.Equal(2, q.DrainThrough(long.MaxValue).Count);
        Assert.True(q.Enqueue(new TestCommand(1, 4, "d")));
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void CommandQueue_DefaultUnbounded_AlwaysAccepts()
    {
        var q = new CommandQueue();
        for (int i = 0; i < 10000; i++)
            Assert.True(q.Enqueue(new TestCommand(1, i, "x")));
        Assert.Equal(10000, q.Count);
    }

    [Fact]
    public void OpenEyeOfCthulhuTreasureBag_ConsumesBagAndAddsRewards()
    {
        var world = new WorldState();
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger(), world);
        world.InventoryLedger = enforcers.Inventory as IInventoryLedger;
        lock (world.PlayersLock)
        {
            var player = new PlayerRuntime { Id = 1, SessionId = 7, Active = true };
            player.Items[3] = 3319;
            player.ItemStacks[3] = 1;
            world.Players[1] = player;
        }

        var result = new OpenEyeOfCthulhuTreasureBagCommand(1, 1, 3) { SessionId = 7 }
            .Apply(world, new FixedRng(0, 0, 0, 0, 0));

        Assert.True(result.Applied);
        var playerAfter = world.Players[1];
        Assert.DoesNotContain(playerAfter.Items.Zip(playerAfter.ItemStacks), item => item.First == 3319 && item.Second > 0);
        Assert.Contains(56, playerAfter.Items);
        Assert.Contains(47, playerAfter.Items);
        Assert.Contains(59, playerAfter.Items);
        Assert.Contains(2112, playerAfter.Items);
        Assert.Contains(1299, playerAfter.Items);
        Assert.Contains((1, 7L, 3), world.DrainInventoryUpdates(59));
    }

    [Fact]
    public void OpenEyeOfCthulhuTreasureBag_FullInventory_DoesNotConsumeBag()
    {
        var world = new WorldState();
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger(), world);
        world.InventoryLedger = enforcers.Inventory as IInventoryLedger;
        lock (world.PlayersLock)
        {
            var player = new PlayerRuntime { Id = 1, Active = true };
            for (var slot = 0; slot < PlayerRuntime.InventorySlotCount; slot++)
            {
                player.Items[slot] = 1;
                player.ItemStacks[slot] = 999;
            }
            player.Items[3] = 3319;
            player.ItemStacks[3] = 1;
            world.Players[1] = player;
        }

        var result = new OpenEyeOfCthulhuTreasureBagCommand(1, 1, 3)
            .Apply(world, new FixedRng(0, 0, 0, 1, 1));

        Assert.False(result.Applied);
        Assert.Equal(CommandFailures.InventoryFull, result.Reason);
        Assert.Equal(3319, world.Players[1].Items[3]);
        Assert.Equal(1, world.Players[1].ItemStacks[3]);
    }

    [Fact]
    public void OpenEyeOfCthulhuTreasureBag_CannotBeOpenedTwice()
    {
        var world = new WorldState();
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger(), world);
        world.InventoryLedger = enforcers.Inventory as IInventoryLedger;
        lock (world.PlayersLock)
        {
            var player = new PlayerRuntime { Id = 1, Active = true };
            player.Items[3] = 3319;
            player.ItemStacks[3] = 1;
            world.Players[1] = player;
        }

        var command = new OpenEyeOfCthulhuTreasureBagCommand(1, 1, 3);
        Assert.True(command.Apply(world, new FixedRng(0, 0, 0, 1, 1)).Applied);
        var repeat = command.Apply(world, new FixedRng(0, 0, 0, 1, 1));

        Assert.False(repeat.Applied);
        Assert.Equal(CommandFailures.NotApplied, repeat.Reason);
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

    private static (WorldState World, PlayerRuntime Player, Chest Chest) CreateTransferWorld(bool openSession = true)
    {
        var world = new WorldState { Tiles = new TileMap(16, 16) };
        var player = new PlayerRuntime { Id = 1, SessionId = 22, Active = true, Position = new Vector2(8, 8) };
        var chest = new Chest { Index = 0, X = 0, Y = 0, Items = new ChestItem[40] };
        lock (world.PlayersLock) world.Players[1] = player;
        lock (world.ChestsLock) world.Chests.Add(chest);
        if (openSession) world.OpenChestSession(1, 22, 0);
        return (world, player, chest);
    }

    /// <summary>
    /// 在玩家可达区域内放一块合成站图格（默认熔炉 17），供「合成守恒」用例满足站位前置条件。
    /// 返回放置点，便于用例做「够不着」的对照。
    /// </summary>
    private static (int X, int Y) PlaceCraftingStation(WorldState world, PlayerRuntime player, int tileType = 17)
    {
        int x = (int)(player.Position.X / 16f) + 1;
        int y = (int)(player.Position.Y / 16f) + 1;
        if (world.Tiles.Width <= x || world.Tiles.Height <= y)
            world.Tiles = new TileMap(Math.Max(16, x + 2), Math.Max(16, y + 2));
        world.Tiles[x, y] = new Tile { Active = true, Type = (ushort)tileType };
        return (x, y);
    }

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
        public InventoryBagOpenResult TryOpenEyeOfCthulhuTreasureBag(int playerId, int slot, IReadOnlyList<InventoryReward> rewards)
            => InventoryBagOpenResult.InvalidBag;
    }

    /// <summary>背包一律接受（用于并发拾取等只关心原子性的用例）。</summary>
    private sealed class AcceptingInventoryLedger : IInventoryLedger
    {
        public bool ConsumeItem(int playerId, int itemId) => true;
        public bool TryAddItem(int playerId, int itemId, int stack) => true;
        public bool TryAddItemExactly(int playerId, int itemId, int stack) => true;
        public InventoryBagOpenResult TryOpenEyeOfCthulhuTreasureBag(int playerId, int slot, IReadOnlyList<InventoryReward> rewards)
            => InventoryBagOpenResult.Success;
    }
}

sealed class FixedRng(params int[] values) : IRng
{
    private readonly Queue<int> _values = new(values);

    public uint NextUInt32() => (uint)NextInt32(int.MaxValue);
    public int NextInt32(int maxExclusive) => _values.Count > 0 ? _values.Dequeue() : 0;
    public double NextDouble() => 0;
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

        // 3) 超上界（上报 10 > 上界 ceil(7×1.15)=9）→ 拒绝，生命不变
        var above = new DamagePlayerCommand(2, 1, 10).Apply(world, rng);
        Assert.False(above.Applied);
        Assert.Equal(CommandFailures.HurtDamageAboveLimit, above.Reason);
        Assert.Equal(100, player.Hp);

        // 4) 区间内（上报 7）→ 按上报值扣血 + 置免伤帧
        Assert.True(new DamagePlayerCommand(3, 1, 7).Apply(world, rng).Applied);
        Assert.Equal(93, player.Hp);
        Assert.Equal(PlayerRuntime.GeneralImmunityTicks(7), player.HurtCooldown);
        Assert.False(player.Dead);

        // 5) 免伤帧内再次上报（区间内 7）→ 忽略（包 117 与服务端接触兜底共享统一免伤帧，
        //    同一次接触只扣一次，杜绝客户端本地 Hurt + 服务端兜底双扣导致血条掉速翻倍）
        var inImmune = new DamagePlayerCommand(4, 1, 7).Apply(world, rng);
        Assert.False(inImmune.Applied);
        Assert.Equal(CommandFailures.NotApplied, inImmune.Reason);
        Assert.Equal(93, player.Hp);
        Assert.Equal(PlayerRuntime.GeneralImmunityTicks(7), player.HurtCooldown);
    }

    /// <summary>
    /// 原版 <c>Main.CalculateDamagePlayersTake</c> 按难度取**减防系数**（阶段 D）：
    /// 经典 <c>dmg − def×0.5</c>、专家 <c>dmg − def×0.75</c>、大师 <c>dmg − def</c>，最低 1。
    /// 伤害值本身不随难度变化 —— 放大发生在 NPC 生成时（见难度缩放用例）。
    /// </summary>
    [Theory]
    [InlineData(GameMode.Classic, 7, 2, 6)]     // 7 − round(2×0.5)=1 → 6
    [InlineData(GameMode.Expert, 7, 2, 5)]      // 7 − round(2×0.75)=2 → 5
    [InlineData(GameMode.Master, 7, 2, 5)]      // 7 − 2 → 5
    [InlineData(GameMode.Expert, 14, 0, 14)]    // 专家下史莱姆的伤害已放大到 14（生成时 ×2）
    [InlineData(GameMode.Master, 21, 0, 21)]    // 大师下史莱姆的伤害已放大到 21（生成时 ×3）
    [InlineData(GameMode.Classic, 1, 10, 1)]    // 下限 1
    [InlineData(GameMode.Expert, 1, 10, 1)]     // 1 − round(7.5)=8 → 下限 1
    [InlineData(GameMode.Master, 0, 0, 1)]      // 0 → 下限 1
    public void CalculateDamagePlayersTake_Applies_Mode_Formula(GameMode mode, int damage, int defense, int expected)
        => Assert.Equal(expected, CombatResolver.CalculateDamagePlayersTake(damage, defense, mode));

    /// <summary>
    /// 原版 <c>NPC.ScaleStats_ByDifficulty</c>（NPC.cs L18341）：**专家 ×2 / 大师 ×3** 的 NPC 生命上限与伤害
    /// （<c>EnemyMaxLifeMultiplier</c> / <c>EnemyDamageMultiplier</c> 曲线在经典~大师区间即难度值本身），
    /// 走 <c>SpawnBoss → AddNpc</c> 这条生成路径（刷怪 / 变体 / 换型共用同一入口）。
    /// 绿史莱姆（type 1）基础：生命 25 / 伤害 7。
    /// </summary>
    [Theory]
    [InlineData(0, 25, 7)]
    [InlineData(1, 50, 14)]
    [InlineData(2, 75, 21)]
    public void SpawnedNpc_Scales_Life_And_Damage_By_Difficulty(int worldGameMode, int expectedLifeMax, int expectedDamage)
    {
        var world = new WorldState();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        world.GameMode = worldGameMode;

        var npc = sim.SpawnBoss(1, 320f, 460f);

        Assert.Equal(expectedLifeMax, npc.LifeMax);
        Assert.Equal(expectedLifeMax, npc.Life);      // 生成即满血
        Assert.Equal(expectedDamage, npc.Damage);
    }

    /// <summary>
    /// 难度缩放只应用一次：专家下史莱姆接触伤害 = 7×2 = 14（不是 7），
    /// 包 117 区间上界 = ceil(14×1.15) = 17（不再乘第二次倍率）。
    /// </summary>
    [Fact]
    public void ExpertMode_Contact_Damage_Is_Scaled_Once()
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

        // 未走 AddNpc 的裸实体：接触伤害按「表值 × 难度倍率」兜底，口径与生成路径一致
        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 50,
            LifeMax = 50,
            X = player.Position.X,
            Y = player.Position.Y + 21f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);

        Assert.Equal(14, CombatResolver.FindContactDamage(world, player, out _, out _));

        // 超上界（18 > 17）→ 拒绝；区间内（17）→ 接受并按上报值扣血：100 − 17 = 83
        var above = new DamagePlayerCommand(2, 1, 18).Apply(world, rng);
        Assert.False(above.Applied);
        Assert.Equal(CommandFailures.HurtDamageAboveLimit, above.Reason);
        Assert.Equal(100, player.Hp);

        Assert.True(new DamagePlayerCommand(3, 1, 17).Apply(world, rng).Applied);
        Assert.Equal(83, player.Hp);
    }

    /// <summary>
    /// 原版 <c>NPC.ScaleStats_ForExpertHardmode</c>（NPC.cs L18683，专家及以上**且**困难模式）：把强度不足的
    /// 普通怪补到最低强度线（<c>damage + defense + lifeMax/4</c> 低于 80，击败世纪之花后 100）。
    /// 蓝史莱姆（7 / 2 / 25）→ 强度 = 7+2+6 = 15 → factor = 80/15 = **5**（原版**整数除法**）→
    /// 伤害 7×5×0.9 = 31、防御 2×5 = 10、生命 25×5×1.1 = 137。
    /// </summary>
    [Theory]
    [InlineData(false, 31, 10, 137)]    // 目标线 80 → factor 5
    [InlineData(true, 37, 12, 165)]     // 击败世纪之花 → 目标线 100 → factor 6
    public void NpcHardmodeScaling_Tops_Up_Weak_Mobs(bool downedPlantBoss, int expectedDamage, int expectedDefense, int expectedLifeMax)
    {
        var npc = new WorldNpc { Type = 1, Damage = 7, Defense = 2, LifeMax = 25, Life = 25 };

        NpcHardmodeScaling.Apply(npc, downedPlantBoss);

        Assert.Equal(expectedDamage, npc.Damage);
        Assert.Equal(expectedDefense, npc.Defense);
        Assert.Equal(expectedLifeMax, npc.LifeMax);
    }

    /// <summary>
    /// 困难模式补强的三类豁免：Boss / <c>lifeMax ≥ 1000</c>、<c>DontDoHardmodeScaling</c> 类型、
    /// 以及投射物型 NPC（只放大伤害，不动防御与生命上限）。
    /// </summary>
    [Fact]
    public void NpcHardmodeScaling_Skips_Bosses_Excluded_Types_And_Projectile_Npcs()
    {
        var boss = new WorldNpc { Type = 4, Damage = 15, Defense = 12, LifeMax = 2800, IsBoss = true };
        NpcHardmodeScaling.Apply(boss, false);
        Assert.Equal(15, boss.Damage);
        Assert.Equal(2800, boss.LifeMax);

        var wormHead = new WorldNpc { Type = 13, Damage = 22, Defense = 2, LifeMax = 150 };   // NPCID.Sets.DontDoHardmodeScaling
        NpcHardmodeScaling.Apply(wormHead, false);
        Assert.Equal(22, wormHead.Damage);
        Assert.Equal(150, wormHead.LifeMax);

        // 投射物型（25 克苏鲁之仆）：强度 5+0+2 = 7 → factor 80/7 = 11 → 伤害 5×11×0.9 = 49，防御 / 生命不动
        var servant = new WorldNpc { Type = 25, Damage = 5, Defense = 0, LifeMax = 10 };
        NpcHardmodeScaling.Apply(servant, false);
        Assert.Equal(49, servant.Damage);
        Assert.Equal(0, servant.Defense);
        Assert.Equal(10, servant.LifeMax);
    }

    /// <summary>
    /// 原版 <c>NPC.ScaleStats_ByPlayerCount</c> + <c>GetStatScalingFactors</c>（NPC.cs L18733/L18895）：
    /// 专家及以上按在线人数放大**生命上限**（伤害不随人数变化）。2 人 balance = 1.35、3 人 = 1.9167…；
    /// 未列入类型表的怪倍率为 1，但「按几人缩放」仍要记录（包 23 的玩家数段用它）。
    /// </summary>
    [Fact]
    public void NpcPlayerCountScaling_Follows_Vanilla_Curve()
    {
        NpcPlayerCountScaling.GetStatScalingFactors(2, out double balance2, out _);
        NpcPlayerCountScaling.GetStatScalingFactors(3, out double balance3, out _);
        Assert.Equal(1.35, balance2, 6);
        Assert.Equal(1.0 + 0.35 + (0.35 + (1.0 - 0.35) / 3.0), balance3, 6);

        var eye = new WorldNpc { Type = 4, LifeMax = 2800 };                     // 克苏鲁之眼
        NpcPlayerCountScaling.Apply(eye, 2);
        Assert.Equal(3780, eye.LifeMax);                                          // 2800 × 1.35
        Assert.Equal(2, eye.StatsScaledForPlayers);

        var slime = new WorldNpc { Type = 1, LifeMax = 25 };                      // 表外类型：倍率 1
        NpcPlayerCountScaling.Apply(slime, 3);
        Assert.Equal(25, slime.LifeMax);
        Assert.Equal(3, slime.StatsScaledForPlayers);

        var martian = new WorldNpc { Type = 338, LifeMax = 100 };                 // 火星暴乱（入侵组 -1）
        NpcPlayerCountScaling.Apply(martian, 3);
        Assert.Equal(140, martian.LifeMax);                                       // 1 + (3-1)×0.2 = 1.4
    }

    /// <summary>
    /// 生成链路整体（难度 × 人数 × 客户端口径）：专家世界 + 2 名在线玩家 →
    /// 克苏鲁之眼生命上限 = 2800 × 2（难度）→ 5600 × 1.35（人数）= 7560，
    /// 并把「按 2 人缩放」记进实体（包 23 玩家数段的来源）。
    /// </summary>
    [Fact]
    public void SpawnedBoss_Scales_By_Difficulty_And_PlayerCount()
    {
        var world = WorldGenerator.GenerateSmall();
        world.GameMode = (int)GameMode.Expert;
        lock (world.PlayersLock)
        {
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true };
            world.Players[2] = new PlayerRuntime { Id = 2, Active = true };
        }

        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        var boss = sim.SpawnBoss(4, 320f, 460f);

        Assert.Equal(7560, boss.LifeMax);
        Assert.Equal(7560, boss.Life);
        Assert.Equal(2, boss.StatsScaledForPlayers);
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

        // 穿铜套：头盔 89(1) + 链甲 80(2) + 护腿 76(1) = 4，穿齐铜套套装加成 +2 → 防御 6
        Assert.True(new SetInventorySlotCommand(1, 1, 0, 89, 1).Apply(world, rng).Applied);
        Assert.True(new SetInventorySlotCommand(2, 1, 1, 80, 1).Apply(world, rng).Applied);
        Assert.True(new SetInventorySlotCommand(3, 1, 2, 76, 1).Apply(world, rng).Applied);
        Assert.Equal(6, player.Defense);
        Assert.Equal(89, player.Items[0]);
        Assert.Equal(80, player.Items[1]);
        Assert.Equal(76, player.Items[2]);

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

        // 超上界（7 > 6，裸装上界为 9）→ 拒绝（防伪造伤害），生命不变
        var above = new DamagePlayerCommand(4, 1, 7).Apply(world, rng);
        Assert.False(above.Applied);
        Assert.Equal(CommandFailures.HurtDamageAboveLimit, above.Reason);
        Assert.Equal(100, player.Hp);

        // 区间内（上报 6 = 上界）→ 接受并按上报值扣血：100 − 6 = 94
        Assert.True(new DamagePlayerCommand(5, 1, 6).Apply(world, rng).Applied);
        Assert.Equal(94, player.Hp);

        // 脱头盔（空槽清空语义）→ 防御降为 3（80 的 2 + 76 的 1，铜套三件不齐套装 +2 失效）
        Assert.True(new SetInventorySlotCommand(6, 1, 0, 0, 0).Apply(world, rng).Applied);
        Assert.Equal(3, player.Defense);
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
    /// 阶段 E / E-5：包 28 近战武器校验上界随 **Buff / 手持武器 / 套装 / 饰品** 实时变化（1.4.5.8 权威 ID）。
    /// 金阔剑（3520，15 伤）：无 buff 上界 ceil(15×1.15)=18；酒醉（25，近战+10%）→ 16.5→16，上界 19；
    /// 穿熔岩套（231/232/233，近战+10%）→ 总修饰 20% → 18，上界 21；
    /// 戴战士徽章（490，近战+15%）→ 总修饰 35% → 20.25→20，上界 23。
    /// E-5 起远程（弓/枪弹幕伤害含弹药）与召唤（仆从伤害≠手持武器）不再参与武器上界校验（失败放行）。
    /// </summary>
    [Fact]
    public void StrikeBound_Follows_Buffs_And_Held_Weapon_RealTime()
    {
        var world = WorldGenerator.GenerateSmall();
        world.StrikeWeaponCheck = true;

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

        // 手持金阔剑（槽 3）：先经 MoveCommand 权威写入 SelectedSlot，验证包 13 → SelectedItem 落库
        Assert.True(new SetInventorySlotCommand(1, 1, 3, 3520, 1).Apply(world, rng).Applied);
        Assert.True(new MoveCommand(2, 1, player.Position) { SelectedItem = 3, ControlBits = 0 }.Apply(world, rng).Applied);
        Assert.Equal(3, player.SelectedSlot);

        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 100,
            LifeMax = 100,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);
        int index = world.Npcs.IndexOf(slime);

        // 无 buff：上界 18，报 19 拒、报 18 收 → 82
        Assert.Equal(18, CombatResolver.WeaponDamageBound(player, 3520, false));
        Assert.False(new NpcStrikeCommand(3, 1, index, 19, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(100, slime.Life);
        Assert.True(new NpcStrikeCommand(4, 1, index, 18, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(82, slime.Life);

        // 酒醉 Buff（包 50 权威，近战 +10%）→ 上界实时升到 19，报 19 收 → 63，报 20 拒
        Assert.True(new SetBuffsCommand(5, 1, new[] { 25 }).Apply(world, rng).Applied);
        Assert.Equal(19, CombatResolver.WeaponDamageBound(player, 3520, false));
        Assert.True(new NpcStrikeCommand(6, 1, index, 19, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(63, slime.Life);
        Assert.False(new NpcStrikeCommand(7, 1, index, 20, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(63, slime.Life);

        // 穿熔岩套（近战 +10%）+ 熔岩胸甲单件（近战 +7%）→ 总修饰 27%，上界实时升到 22，报 21 收 → 42，报 23 拒
        Assert.True(new SetInventorySlotCommand(8, 1, 0, 231, 1).Apply(world, rng).Applied);
        Assert.True(new SetInventorySlotCommand(9, 1, 1, 232, 1).Apply(world, rng).Applied);
        Assert.True(new SetInventorySlotCommand(10, 1, 2, 233, 1).Apply(world, rng).Applied);
        Assert.Equal(22, CombatResolver.WeaponDamageBound(player, 3520, false));
        Assert.True(new NpcStrikeCommand(11, 1, index, 21, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(42, slime.Life);
        Assert.False(new NpcStrikeCommand(12, 1, index, 23, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(42, slime.Life);

        // 戴战士徽章（490，近战 +15%，槽 4 饰品）→ 总修饰 = 10（酒醉）+ 10（熔岩套）+ 7（熔岩胸甲）+ 15（徽章）= 42% →
        // 权威伤害 15×1.42=21.3 → 21，上界 ceil(21×1.15)=25，报 23 收 → 19，报 26 拒
        Assert.True(new SetInventorySlotCommand(13, 1, 4, 490, 1).Apply(world, rng).Applied);
        Assert.Equal(25, CombatResolver.WeaponDamageBound(player, 3520, false));
        Assert.True(new NpcStrikeCommand(14, 1, index, 23, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(19, slime.Life);
        Assert.False(new NpcStrikeCommand(15, 1, index, 26, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(19, slime.Life);
    }

    /// <summary>
    /// 阶段 G：召唤 / 哨兵命中按**玩家存活召唤弹幕最高伤害**上界校验，且不因手持弱武器误拒。
    /// 星尘细胞法杖召唤 191 号弹幕（<see cref="SummonProjectileTable"/>），客户端包 27 上报 Damage=60
    /// （已含 minionDamage 修饰）→ 上界 = ceil(60×1.15)=69，暴击 138。
    /// 手持木剑（24，7 伤）武器上界仅 ceil(7×1.15)=9：召唤物命中 46 不得被武器通道误拒；
    /// 报 999（超两通道上界）必须拒绝；召唤后切空手仍被通道 2 约束（999 拒 / 46 收）；
    /// 移除弹幕 + 空手（两通道皆无上界）→ 失败放行；无弹幕时武器通道独立生效。
    /// </summary>
    [Fact]
    public void StrikeBound_Summon_Uses_Minion_Projectile_Damage()
    {
        var world = WorldGenerator.GenerateSmall();
        world.StrikeWeaponCheck = true;

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

        // 手持木剑（槽 3，7 伤近战）：武器通道上界仅 9。
        Assert.True(new SetInventorySlotCommand(1, 1, 3, 24, 1).Apply(world, rng).Applied);
        Assert.True(new MoveCommand(2, 1, player.Position) { SelectedItem = 3, ControlBits = 0 }.Apply(world, rng).Applied);
        Assert.Equal(9, CombatResolver.WeaponDamageBound(player, 24, false));

        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 500,
            LifeMax = 500,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);
        int index = world.Npcs.IndexOf(slime);

        // 玩家拥有存活召唤弹幕：type 191（星尘细胞），Damage 60。
        lock (world.ProjectilesLock)
            world.Projectiles.Add(new ProjectileEntity
            {
                Key = 7,
                Owner = 1,
                Type = 191,
                Position = new Vector2(320f, 398f),
                Velocity = new Vector2(0f, 0f),
                Damage = 60,
                Active = true,
            });

        Assert.Equal(69, CombatResolver.SummonDamageBound(world, 1, false));
        Assert.Equal(138, CombatResolver.SummonDamageBound(world, 1, true));

        // 召唤物合法命中 46（60 经 DamageVar ±15% 内）→ 虽超武器通道 9，但召唤通道 69 放行。
        Assert.True(new NpcStrikeCommand(3, 1, index, 46, Generation: 3).Apply(world, rng).Applied);
        // 暴击上界内 138 → 接受。
        Assert.True(new NpcStrikeCommand(4, 1, index, 138, Generation: 3, Crit: true).Apply(world, rng).Applied);
        // 超暴击上界 139 → 拒绝。
        Assert.False(new NpcStrikeCommand(5, 1, index, 139, Generation: 3, Crit: true).Apply(world, rng).Applied);
        // 999 远超两通道上界 → 拒绝。
        Assert.False(new NpcStrikeCommand(6, 1, index, 999, Generation: 3).Apply(world, rng).Applied);

        // 召唤后切换空手：召唤弹幕仍在 → 通道 2 不依赖手持武器，999 拒绝、合法 46 接受。
        player.Items[3] = 0;
        player.ItemPrefixes[3] = 0;
        Assert.False(new NpcStrikeCommand(7, 1, index, 999, Generation: 3).Apply(world, rng).Applied);
        Assert.True(new NpcStrikeCommand(8, 1, index, 46, Generation: 3).Apply(world, rng).Applied);

        // 移除召唤弹幕 + 空手 → 两通道皆无上界 → 失败放行，绝不误拒。
        // 注：报 1 而非 999——「放行」后服务端仍按上报值结算伤害，报 999 会秒杀 NPC 使后续命令失效。
        lock (world.ProjectilesLock)
            world.Projectiles[0].Active = false;
        Assert.Null(CombatResolver.SummonDamageBound(world, 1, false));
        Assert.True(new NpcStrikeCommand(9, 1, index, 1, Generation: 3).Apply(world, rng).Applied);

        // 无弹幕时武器通道独立生效：重新持木剑 → 999 超上界 9 → 拒绝。
        Assert.True(new SetInventorySlotCommand(10, 1, 3, 24, 1).Apply(world, rng).Applied);
        Assert.True(new MoveCommand(11, 1, player.Position) { SelectedItem = 3, ControlBits = 0 }.Apply(world, rng).Applied);
        Assert.False(new NpcStrikeCommand(12, 1, index, 999, Generation: 3).Apply(world, rng).Applied);

        // 无弹幕 + 空手 + 背包含召唤武器时，背包物品不能替代服务端召唤实体：命中必须拒绝。
        player.Items[3] = 0;
        player.ItemPrefixes[3] = 0;
        Assert.True(new SetInventorySlotCommand(13, 1, 4, 3474, 1).Apply(world, rng).Applied);
        Assert.Null(CombatResolver.SummonDamageBound(world, 1, false));
        Assert.False(new NpcStrikeCommand(14, 1, index, 999, Generation: 3).Apply(world, rng).Applied);
        Assert.False(new NpcStrikeCommand(15, 1, index, 46, Generation: 3).Apply(world, rng).Applied);
    }

    /// <summary>
    /// W-2 第二步 <c>ServerDamage</c> 的**前置守卫**（对应已回退尝试的 P0-1）：
    /// 玩家**带着存活召唤物**、手持普通武器命中时，绝不能被"场上有 owned summon"挟持去走召唤校验路径
    /// （旧实现只要场上有自有召唤弹幕就要求包 28 必须由召唤弹幕背书，导致普通近战 / 远程命中被拒）。
    /// 关键点：本用例让**召唤上界远低于武器上界**——若哪天把召唤通道改回"强制"，合法武器命中会被拒而失败。
    /// </summary>
    [Fact]
    public void Summon_P0_1_Guard_NormalWeapon_Hit_Not_Hijacked_By_Owned_Summons()
    {
        var world = WorldGenerator.GenerateSmall();
        world.StrikeWeaponCheck = true;

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

        // 手持炽焰巨剑（121，40 伤害近战）→ 武器上界 46；召唤物很弱（191，5 伤害 → 上界 6）。
        Assert.True(new SetInventorySlotCommand(1, 1, 3, 121, 1).Apply(world, rng).Applied);
        Assert.True(new MoveCommand(2, 1, player.Position) { SelectedItem = 3, ControlBits = 0 }.Apply(world, rng).Applied);
        Assert.Equal(46, CombatResolver.WeaponDamageBound(player, 121, false));

        var npc = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 500,
            LifeMax = 500,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(npc);
        int index = world.Npcs.IndexOf(npc);

        lock (world.ProjectilesLock)
            world.Projectiles.Add(new ProjectileEntity
            {
                Key = 7,
                Owner = 1,
                Type = 191,
                IsSummon = true,
                Position = new Vector2(320f, 398f),
                Damage = 5,
                Active = true,
            });

        Assert.Equal(6, CombatResolver.SummonDamageBound(world, 1, false));

        // 核心断言：合法近战 40（≤ 武器上界 46，但**远超**召唤上界 6）→ 必须 Applied。
        var legal = new NpcStrikeCommand(3, 1, index, 40, Generation: 3).Apply(world, rng);
        Assert.True(legal.Applied, $"带仆从时手持武器的合法命中被拒：{legal.Reason}");
        Assert.Equal(460, npc.Life);

        // 贴边 46 仍在武器上界内 → Applied；47 超上界 → 拒绝（防上界被意外放大成"全放行"）。
        Assert.True(new NpcStrikeCommand(4, 1, index, 46, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(CommandFailures.StrikeDamageMismatch,
            new NpcStrikeCommand(5, 1, index, 47, Generation: 3).Apply(world, rng).Reason);

        // 暴击：武器通道 92、召唤通道 12 → 合法暴击 80 仍凭**武器**通道放行（不被召唤通道压住）。
        Assert.True(new NpcStrikeCommand(6, 1, index, 80, Generation: 3, Crit: true).Apply(world, rng).Applied);

        // 对照（预期语义，非缺陷）：**空手** + 场上有自有召唤弹幕时，包 28 视作召唤物命中，
        // 须由召唤弹幕背书 → 40 超召唤上界 6，按召唤口径拒绝。这正是"手持武器"与"空手"的分界。
        player.Items[3] = 0;
        player.ItemStacks[3] = 0;
        player.ItemPrefixes[3] = 0;
        Assert.Equal(CommandFailures.StrikeDamageMismatch,
            new NpcStrikeCommand(7, 1, index, 40, Generation: 3).Apply(world, rng).Reason);
    }

    /// <summary>
    /// W-2 第二步 `ServerDamage`：默认档必须是 <see cref="SummonAuthorityMode.ClientDriven"/>（零风险），
    /// 且 <c>ServerSettlesSummonDamage</c> 只在 `ServerDamage` 及以上为真。
    /// </summary>
    [Fact]
    public void SummonAuthority_Defaults_To_ClientDriven()
    {
        var world = WorldGenerator.GenerateSmall();
        Assert.Equal(SummonAuthorityMode.ClientDriven, world.SummonAuthority);
        Assert.False(world.ServerSettlesSummonDamage);

        world.SummonAuthority = SummonAuthorityMode.ServerDamage;
        Assert.True(world.ServerSettlesSummonDamage);
        world.SummonAuthority = SummonAuthorityMode.ServerAi;
        Assert.True(world.ServerSettlesSummonDamage);
    }

    /// <summary>
    /// `ServerDamage` 档：**本体**命中的伤害数值由服务端裁定——包 28 上报的 Damage 只作命中触发，不参与结算。
    /// 用例让客户端上报 1（合法但极小），断言服务端按本体登记伤害 60 掷 ±15% 浮动后结算，
    /// 从而证明"上报值被忽略"；并验证本体命中的冷却（现为 10 tick，待 W-2 第二步换成表驱动三档）。
    /// </summary>
    [Fact]
    public void ServerDamage_Settles_Body_Hit_With_Server_Damage()
    {
        var world = WorldGenerator.GenerateSmall();
        world.StrikeWeaponCheck = true;
        world.SummonAuthority = SummonAuthorityMode.ServerDamage;

        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Hp = 100,
            HpMax = 100,
            Position = new Vector2(320f, 460f),
            AimPosition = new Vector2(320f, 460f),
            SelectedSlot = 3,
            DeathNotified = true,
        };
        player.Items[3] = 3474; // StardustCellStaff（Summon 职业）→ summonAttackExpected
        player.ItemStacks[3] = 1;
        lock (world.PlayersLock) world.Players[1] = player;

        var rng = new XoshiroRng(1);

        var npc = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 500,
            LifeMax = 500,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(npc);
        int index = world.Npcs.IndexOf(npc);

        // 本体 613 StardustCellMinion（本体表在册 → IsSummonBody 为真），登记伤害 60。
        lock (world.ProjectilesLock)
            world.Projectiles.Add(new ProjectileEntity
            {
                Key = 7,
                Owner = 1,
                Type = 613,
                IsSummon = true,
                SummonEntityId = 1,
                SummonKind = SummonKind.Minion,
                Position = new Vector2(320f, 400f),
                Damage = 60,
                Penetrate = -1, // 召唤本体由包 27 创建时即 -1（不因命中消耗）
                Active = true,
            });

        // 上报 1：服务端应忽略它，按 60 掷浮动（[51, 69]）结算。
        Assert.True(new NpcStrikeCommand(3, 1, index, 1, Generation: 3).Apply(world, rng).Applied);
        Assert.InRange(npc.Life, 500 - 69, 500 - 51);

        // 同一本体的命中冷却内（10 tick）再次上报 → 拒绝。
        Assert.Equal(CommandFailures.ProjectileHitCooldown,
            new NpcStrikeCommand(4, 1, index, 1, Generation: 3).Apply(world, rng).Reason);

        // 冷却过后（3 + 10 = 13 < 14）可再次命中；暴击倍率仍沿用上报 Crit 位 → 结算翻倍。
        int before = npc.Life;
        Assert.True(new NpcStrikeCommand(14, 1, index, 1, Generation: 3, Crit: true).Apply(world, rng).Applied);
        Assert.InRange(before - npc.Life, 51 * 2, 69 * 2);
    }

    /// <summary>
    /// `ServerDamage` 档：**本体不再作为包 28 的伤害凭据**——否则玩家可手持任意武器、拿高伤本体当"上界"。
    /// 对照组：同一场景在 ClientDriven 档下 60 因本体上界（69）被放行，在 ServerDamage 档下只剩武器上界 9 → 拒绝；
    /// 同时确认 P0-1 不变量在该档下仍成立（合法武器命中 9 照常 `Applied`）。
    /// </summary>
    [Fact]
    public void ServerDamage_Excludes_Body_From_Packet28_Bound()
    {
        static (WorldState World, WorldNpc Npc, int Index) CreateScenario()
        {
            var world = WorldGenerator.GenerateSmall();
            world.StrikeWeaponCheck = true;

            var player = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Hp = 100,
                HpMax = 100,
                Position = new Vector2(320f, 460f),
                AimPosition = new Vector2(320f, 460f),
                SelectedSlot = 3,
                DeathNotified = true,
            };
            player.Items[3] = 24; // 木剑（近战 7 伤 → 上界 9）
            player.ItemStacks[3] = 1;
            lock (world.PlayersLock) world.Players[1] = player;

            var npc = new WorldNpc
            {
                Type = 1,
                NetId = 1,
                Active = true,
                Life = 500,
                LifeMax = 500,
                Generation = 3,
                X = 320f,
                Y = 400f,
            };
            lock (world.NpcsLock) world.Npcs.Add(npc);
            int index = world.Npcs.IndexOf(npc);

            lock (world.ProjectilesLock)
                world.Projectiles.Add(new ProjectileEntity
                {
                    Key = 7,
                    Owner = 1,
                    Type = 613,
                    IsSummon = true,
                    SummonEntityId = 1,
                    SummonKind = SummonKind.Minion,
                    Position = new Vector2(320f, 400f),
                    Damage = 60,
                    Active = true,
                });

            return (world, npc, index);
        }

        var rng = new XoshiroRng(1);

        // ClientDriven（默认档）：本体 60 → 上界 69 ≥ 60 → 放行。
        var client = CreateScenario();
        Assert.Equal(69, CombatResolver.SummonDamageBound(client.World, 1, false));
        Assert.True(new NpcStrikeCommand(3, 1, client.Index, 60, Generation: 3).Apply(client.World, rng).Applied);
        Assert.Equal(440, client.Npc.Life);

        // ServerDamage：本体被排除 → 上界只剩武器 9 → 同一上报 60 被拒。
        var server = CreateScenario();
        server.World.SummonAuthority = SummonAuthorityMode.ServerDamage;
        Assert.Null(CombatResolver.SummonDamageBound(server.World, 1, false, excludeBodies: true));
        Assert.Equal(CommandFailures.StrikeDamageMismatch,
            new NpcStrikeCommand(3, 1, server.Index, 60, Generation: 3).Apply(server.World, rng).Reason);
        Assert.Equal(500, server.Npc.Life);

        // P0-1 不变量在该档下仍成立：合法近战 9（≤ 武器上界）照常结算。
        Assert.True(new NpcStrikeCommand(4, 1, server.Index, 9, Generation: 3).Apply(server.World, rng).Applied);
        Assert.Equal(491, server.Npc.Life);
    }

    [Fact]
    public void Summon_Attack_Selects_Overlapping_Entity_Deterministically()
    {
        var world = WorldGenerator.GenerateSmall();
        var player = new PlayerRuntime { Id = 1, Active = true, SelectedSlot = 3 };
        player.Items[3] = 3474;
        player.ItemStacks[3] = 1;
        lock (world.PlayersLock) world.Players[1] = player;

        var npc = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 500,
            LifeMax = 500,
            Generation = 1,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(npc);
        int index = world.Npcs.IndexOf(npc);

        // 用 LocalPerTarget 档（317 Raven）——冷却记在**被选中的那枚实例**上，据此可观测归因结果；
        // 默认档的冷却记在 (玩家, NPC) 上，反而看不出选的是哪一枚。
        var nearer = new ProjectileEntity
        {
            Key = 8, Owner = 1, Type = 317, IsSummon = true,
            SummonEntityId = 2, SummonKind = SummonKind.Minion,
            Position = new Vector2(320f, 400f), Damage = 60, Active = true, Penetrate = -1,
        };
        var farther = new ProjectileEntity
        {
            Key = 7, Owner = 1, Type = 317, IsSummon = true,
            SummonEntityId = 1, SummonKind = SummonKind.Minion,
            Position = new Vector2(320f, 398f), Damage = 100, Active = true, Penetrate = -1,
        };
        lock (world.ProjectilesLock)
        {
            world.Projectiles.Add(farther);
            world.Projectiles.Add(nearer);
        }

        Assert.True(new NpcStrikeCommand(10, 1, index, 60, Generation: 1)
            .Apply(world, new XoshiroRng(1)).Applied);
        Assert.Equal(20, farther.SummonNpcHitCooldownUntil[index]);
        Assert.False(nearer.SummonNpcHitCooldownUntil.ContainsKey(index));
    }

    [Fact]
    public void Summon_Attack_Uses_ServerRegisteredEntity_Without_CurrentPositionOverlap()
    {
        var world = WorldGenerator.GenerateSmall();
        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Hp = 100,
            HpMax = 100,
            SelectedSlot = 3,
        };
        player.Items[3] = 3474;
        player.ItemStacks[3] = 1;
        lock (world.PlayersLock) world.Players[1] = player;

        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 100,
            LifeMax = 100,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);
        int index = world.Npcs.IndexOf(slime);

        lock (world.ProjectilesLock)
            world.Projectiles.Add(new ProjectileEntity
            {
                Key = 7,
                Owner = 1,
                Type = 266,
                Position = new Vector2(1000f, 1000f),
                Damage = 8,
                Active = true,
                IsSummon = true,
                SummonKind = SummonKind.Minion,
                SummonEntityId = 1,
                Penetrate = -1,
            });

        var result = new NpcStrikeCommand(10, 1, index, 8, Generation: 3)
            .Apply(world, new XoshiroRng(1));

        Assert.True(result.Applied);
        Assert.Equal(92, slime.Life);
    }

    [Fact]
    public void Summon_Attack_Uses_Separate_Npc_Hit_Cooldown()
    {
        var world = WorldGenerator.GenerateSmall();
        world.StrikeProjectileMatch = false;

        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Hp = 100,
            HpMax = 100,
            Position = new Vector2(320f, 460f),
            AimPosition = new Vector2(320f, 460f),
            DeathNotified = true,
            SelectedSlot = 3,
        };
        player.Items[3] = 3474;
        player.ItemStacks[3] = 1;
        lock (world.PlayersLock) world.Players[1] = player;

        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 500,
            LifeMax = 500,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);
        int index = world.Npcs.IndexOf(slime);

        var projectile = new ProjectileEntity
        {
            Key = 7,
            Owner = 1,
            Type = 191,
            Position = new Vector2(320f, 398f),
            Velocity = new Vector2(0f, 0f),
            Damage = 60,
            Active = true,
            IsSummon = true,
            Penetrate = -1,
        };
        projectile.NpcHitCooldownUntil[index] = 0;
        lock (world.ProjectilesLock) world.Projectiles.Add(projectile);

        var rng = new XoshiroRng(1);
        Assert.True(new NpcStrikeCommand(10, 1, index, 60, Generation: 3).Apply(world, rng).Applied);
        // 191 Pygmy 属**默认档**（原版 targetNPC.immune[owner] = 10）→ 冷却按 (玩家, NPC) 记，不在弹幕实例上。
        Assert.Equal(20, world.SummonPlayerHitCooldownUntil[(1, index)]);
        Assert.Empty(projectile.SummonNpcHitCooldownUntil);
        Assert.Equal(0, projectile.NpcHitCooldownUntil[index]);
        Assert.False(new NpcStrikeCommand(11, 1, index, 60, Generation: 3).Apply(world, rng).Applied);
        Assert.True(new NpcStrikeCommand(20, 1, index, 60, Generation: 3).Apply(world, rng).Applied);
    }

    /// <summary>
    /// 本体命中免疫按 <see cref="SummonEntityTable"/> 的**三档**建模（原版 <c>Projectile.Damage</c>）：
    /// 默认档 = 每（玩家, NPC）10 tick；<c>LocalPerTarget</c> = 每**弹幕实例**各目标；
    /// <c>IdStaticShared</c> = 同 **type** 所有实例共享；冷却 <c>-1</c> = 同一弹幕对同一目标终身一次。
    /// 用例通过「命中后停用旧本体、换一枚同档新本体」区分三档：默认档与 IdStaticShared 会**拦住新本体**，
    /// LocalPerTarget **不会**。
    /// </summary>
    [Fact]
    public void Summon_Hit_Immunity_Follows_Table_Tiers()
    {
        static (WorldState World, int Index) CreateScenario()
        {
            var world = WorldGenerator.GenerateSmall();
            var player = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Hp = 100,
                HpMax = 100,
                SelectedSlot = 3,
                DeathNotified = true,
            };
            player.Items[3] = 3474; // 召唤法杖 → summonAttackExpected 走本体路径
            player.ItemStacks[3] = 1;
            lock (world.PlayersLock) world.Players[1] = player;

            var npc = new WorldNpc
            {
                Type = 1,
                NetId = 1,
                Active = true,
                Life = 1_000_000,
                LifeMax = 1_000_000,
                Generation = 3,
                X = 320f,
                Y = 400f,
            };
            lock (world.NpcsLock) world.Npcs.Add(npc);

            return (world, world.Npcs.IndexOf(npc));
        }

        static ProjectileEntity Body(int type, long entityId) => new()
        {
            Key = (int)entityId,
            Owner = 1,
            Type = type,
            IsSummon = true,
            SummonEntityId = entityId,
            SummonKind = SummonKind.Minion,
            Position = new Vector2(320f, 400f),
            Damage = 5,
            Penetrate = -1,
            Active = true,
        };

        static void Add(WorldState world, ProjectileEntity p)
        {
            lock (world.ProjectilesLock) world.Projectiles.Add(p);
        }

        var rng = new XoshiroRng(1);

        // ---- 默认档（191 Pygmy，10 tick）：换一枚**不同实例**的本体也拦 —— 冷却记在 (玩家, NPC) 上 ----
        var def = CreateScenario();
        var pygmy = Body(191, 1);
        Add(def.World, pygmy);
        Assert.True(new NpcStrikeCommand(10, 1, def.Index, 5, Generation: 3).Apply(def.World, rng).Applied);
        pygmy.Active = false;
        Add(def.World, Body(613, 2)); // 换 613 StardustCellMinion（同属默认档）
        Assert.Equal(CommandFailures.ProjectileHitCooldown,
            new NpcStrikeCommand(11, 1, def.Index, 5, Generation: 3).Apply(def.World, rng).Reason);
        Assert.True(new NpcStrikeCommand(21, 1, def.Index, 5, Generation: 3).Apply(def.World, rng).Applied);

        // ---- LocalPerTarget（317 Raven，10 tick）：换实例即各算各的 ----
        var local = CreateScenario();
        var raven1 = Body(317, 1);
        Add(local.World, raven1);
        Assert.True(new NpcStrikeCommand(10, 1, local.Index, 5, Generation: 3).Apply(local.World, rng).Applied);
        Assert.Equal(20, raven1.SummonNpcHitCooldownUntil[local.Index]);
        raven1.Active = false;
        Add(local.World, Body(317, 2));
        Assert.True(new NpcStrikeCommand(11, 1, local.Index, 5, Generation: 3).Apply(local.World, rng).Applied);
        // 但**同一实例**在冷却内仍被拦
        Assert.Equal(CommandFailures.ProjectileHitCooldown,
            new NpcStrikeCommand(12, 1, local.Index, 5, Generation: 3).Apply(local.World, rng).Reason);

        // ---- IdStaticShared（387 Retanimini，16 tick）：同 type 换实例共享冷却 ----
        var shared = CreateScenario();
        var ret1 = Body(387, 1);
        Add(shared.World, ret1);
        Assert.True(new NpcStrikeCommand(10, 1, shared.Index, 5, Generation: 3).Apply(shared.World, rng).Applied);
        Assert.Equal(26, shared.World.SummonTypeHitCooldownUntil[(387, shared.Index)]);
        ret1.Active = false;
        Add(shared.World, Body(387, 2));
        Assert.Equal(CommandFailures.ProjectileHitCooldown,
            new NpcStrikeCommand(11, 1, shared.Index, 5, Generation: 3).Apply(shared.World, rng).Reason);
        Assert.True(new NpcStrikeCommand(26, 1, shared.Index, 5, Generation: 3).Apply(shared.World, rng).Applied);

        // ---- 冷却 -1（755 BatOfLight）：同一弹幕对同一目标终身一次 ----
        var once = CreateScenario();
        var bat = Body(755, 1);
        Add(once.World, bat);
        Assert.True(new NpcStrikeCommand(10, 1, once.Index, 5, Generation: 3).Apply(once.World, rng).Applied);
        Assert.Equal(long.MaxValue, bat.SummonNpcHitCooldownUntil[once.Index]);
        Assert.Equal(CommandFailures.ProjectileHitCooldown,
            new NpcStrikeCommand(10_000, 1, once.Index, 5, Generation: 3).Apply(once.World, rng).Reason);

        // ---- 星尘龙：节段 626 的冷却必须落在**头节 625** 上（原版共用 localNPCImmunity 数组）----
        var dragon = CreateScenario();
        var head = Body(625, 2);   // 头节 entity id 更大
        var seg = Body(626, 1);    // 节段 entity id 更小 → 命中归因到 626
        Add(dragon.World, head);
        Add(dragon.World, seg);
        Assert.True(new NpcStrikeCommand(10, 1, dragon.Index, 5, Generation: 3).Apply(dragon.World, rng).Applied);
        Assert.Equal(17, head.SummonNpcHitCooldownUntil[dragon.Index]); // 头节 625 的冷却 7 → 写入头节数组
        Assert.Empty(seg.SummonNpcHitCooldownUntil);                    // 节段自己不留冷却
        Assert.Equal(CommandFailures.ProjectileHitCooldown,
            new NpcStrikeCommand(11, 1, dragon.Index, 5, Generation: 3).Apply(dragon.World, rng).Reason);
    }

    /// <summary>
    /// W-2 本体生命周期：三条存活判据里 ①属主离线 与 ②召唤 Buff 消失都是**事件驱动**的
    /// （<see cref="WorldState.KillSummonedProjectiles"/> 由断线清场触发、
    /// <see cref="WorldState.KillSummonedProjectilesForBuff"/> 由包 50 触发），故本轮补的是
    /// 唯一一条**时间驱动**的：③哨兵 <c>timeLeft = 36000</c> 到点自毁（仆从表值为 0，不吃时间销毁）。
    /// </summary>
    [Fact]
    public void Sentry_Lifetime_Seeded_From_Table_And_Expires()
    {
        var world = WorldGenerator.GenerateSmall();
        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            SelectedSlot = 3,
            Position = new Vector2(320f, 460f),
        };
        player.Items[3] = 1572; // StaffoftheFrostHydra（Summon 职业）
        player.ItemStacks[3] = 1;
        lock (world.PlayersLock) world.Players[1] = player;

        var rng = new XoshiroRng(1);

        // 哨兵 308 FrostHydra：spawn 时按本体表起算 timeLeft = 36000（原版 10 分钟）
        Assert.True(new SpawnProjectileCommand(1, 1, 1, 308, new Vector2(320f, 460f), new Vector2(0f, 0f), 100)
            .Apply(world, rng).Applied);
        var sentry = world.Projectiles.First(p => p.Type == 308);
        Assert.Equal(36000, sentry.TimeLeft);

        // 仆从 613 StardustCellMinion：表值 0 = 由召唤 Buff 驱动 → 保留默认 timeLeft、不参与递减
        Assert.True(new SpawnProjectileCommand(2, 1, 2, 613, new Vector2(320f, 460f), new Vector2(0f, 0f), 60)
            .Apply(world, rng).Applied);
        var minion = world.Projectiles.First(p => p.Type == 613);
        Assert.Equal(300, minion.TimeLeft);

        // 把哨兵剩余时间压到 2 tick 推进仿真：到点自毁（Active=false + Destroyed=true → 拒绝被包 27 复活）
        sentry.TimeLeft = 2;
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        sim.Tick();
        Assert.True(sentry.Active);
        Assert.Equal(1, sentry.TimeLeft);
        sim.Tick();
        Assert.False(sentry.Active);
        Assert.True(sentry.Destroyed);

        // 仆从不受时间驱动影响（生命周期只由 Buff / 断线决定）
        Assert.True(minion.Active);
    }

    /// <summary>
    /// W-2 拆表回归：**本体发射的派生弹幕**（如 374 HornetStinger）在服务端必须被登记为召唤体系弹幕，
    /// 否则玩家手持召唤法杖时包 28 找不到凭据 → `ProjectileRequired` → **派生弹幕伤害全部丢失**。
    /// 旧版把 374 这类类型排除在身份集合之外、`KindOf` 又返回 `None`，正是这个缺陷。
    /// </summary>
    [Fact]
    public void Summon_Derived_Shot_Is_Registered_And_Backs_Packet28()
    {
        var world = WorldGenerator.GenerateSmall();
        world.StrikeWeaponCheck = true;

        var player = new PlayerRuntime
        {
            Id = 1,
            Active = true,
            Hp = 100,
            HpMax = 100,
            SelectedSlot = 3,
            Position = new Vector2(320f, 460f),
            AimPosition = new Vector2(320f, 460f),
            DeathNotified = true,
        };
        player.Items[3] = 3474; // StardustCellStaff（Summon 职业）→ 手持召唤法杖
        player.ItemStacks[3] = 1;
        lock (world.PlayersLock) world.Players[1] = player;

        var npc = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 500,
            LifeMax = 500,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(npc);
        int index = world.Npcs.IndexOf(npc);

        var rng = new XoshiroRng(1);

        // 本体 613 → 派生弹幕 374 的登记：kind 由发射者决定，IsSummon 为真（旧版是 false）
        Assert.True(new SpawnProjectileCommand(1, 1, 7, 374, new Vector2(320f, 400f), new Vector2(0f, 0f), 60)
            .Apply(world, rng).Applied);
        var shot = world.Projectiles.First(p => p.Type == 374);
        Assert.True(shot.IsSummon, "派生弹幕必须被登记为召唤体系弹幕（旧版为 false → 包 28 被拒）");
        Assert.NotEqual(SummonKind.None, SummonProjectileTable.KindOf(374));
        Assert.True(shot.SummonEntityId > 0);
        // 召唤 Buff 归属只挂本体：派生弹幕不该被「Buff 消失」连带销毁
        Assert.Equal(0, shot.SourceSummonBuffId);

        // 包 28 由这枚派生弹幕背书 → 结算（旧版此处为 ProjectileRequired）
        var strike = new NpcStrikeCommand(2, 1, index, 60, Generation: 3).Apply(world, rng);
        Assert.True(strike.Applied, $"派生弹幕的合法命中被拒：{strike.Reason}");
        Assert.Equal(440, npc.Life);
    }

    /// <summary>
    /// 本体移动表（backlog W-2 第三档 `ServerAi` 的数据侧）：62 个本体的移动模式 / 召回阈值 / 超远归位 / 速度上限。
    /// 三条通用事实必须被这张表锁住：①出世界边界时 `minion` 传回主人、`sentry` **直接消亡**（故哨兵都没有超远归位）；
    /// ②`MinionRestTargetPoint` 只有 623 用；③**并非所有哨兵都静止**（只有 641/643 每帧清零 velocity）。
    /// </summary>
    [Fact]
    public void SummonMovementTable_Matches_Vanilla_Ai()
    {
        // 与本体表逐一对应
        Assert.Equal(62, SummonMovementTable.Of.Count);
        Assert.Equal(SummonEntityTable.Of.Keys.OrderBy(x => x), SummonMovementTable.Of.Keys.OrderBy(x => x));

        // 哨兵：**没有任何**召回 / 超远归位机制（越界即消亡，不是归位）
        var sentries = SummonEntityTable.Of.Where(kv => kv.Value.Kind == SummonKind.Sentry).Select(kv => kv.Key);
        Assert.All(sentries, t =>
        {
            Assert.Null(SummonMovementTable.Of[t].RecallDistance);
            Assert.Null(SummonMovementTable.Of[t].RecallWithTargetDistance);
            Assert.Null(SummonMovementTable.Of[t].TeleportDistance);
        });
        Assert.DoesNotContain(SummonMovementTable.Of.Values.Where(v => v.TeleportDistance is not null),
            v => v.TeleportDistance == 0);

        // 真静止的哨兵只有 641 / 643
        Assert.Equal(new[] { 641, 643 },
            SummonMovementTable.Of.Where(kv => kv.Value.Mode == SummonMoveMode.Static).Select(kv => kv.Key).OrderBy(x => x));
        // 位置硬绑定：831/970（家点）+ 星尘龙节段 626/627/628（绑父节）
        Assert.Equal(new[] { 626, 627, 628, 831, 970 },
            SummonMovementTable.Of.Where(kv => kv.Value.Mode == SummonMoveMode.PositionBound)
                .Select(kv => kv.Key).OrderBy(x => x));

        // 飞行系代表值
        Assert.Equal(SummonMoveMode.Fly, SummonMovementTable.Of[373].Mode);
        Assert.Equal((500, 1000), (SummonMovementTable.Of[373].RecallDistance, SummonMovementTable.Of[373].RecallWithTargetDistance));
        Assert.Equal((800, 1200), (SummonMovementTable.Of[387].RecallDistance, SummonMovementTable.Of[387].RecallWithTargetDistance));
        Assert.Equal((900, 1500), (SummonMovementTable.Of[533].RecallDistance, SummonMovementTable.Of[533].RecallWithTargetDistance));
        // 623 是唯一用 MinionRestTargetPoint 的本体 → 本表没有可记的召回/归位阈值
        Assert.Equal(SummonMoveMode.Fly, SummonMovementTable.Of[623].Mode);
        Assert.Null(SummonMovementTable.Of[623].RecallDistance);
        Assert.Null(SummonMovementTable.Of[623].TeleportDistance);
        // 864 的超远阈值是 3000（全表唯一不是 2000 的）
        Assert.Equal(3000, SummonMovementTable.Of[864].TeleportDistance);
        Assert.All(SummonMovementTable.Of.Where(kv => kv.Value.TeleportDistance is not null && kv.Key != 864),
            kv => Assert.Equal(2000, kv.Value.TeleportDistance));

        // 贴地系（AI_026 / AI_067）与贴顶系
        Assert.Equal(SummonMoveMode.Ground, SummonMovementTable.Of[191].Mode);
        Assert.Equal(SummonMoveMode.Ground, SummonMovementTable.Of[1118].Mode);
        Assert.Equal(SummonMoveMode.Ceiling, SummonMovementTable.Of[1025].Mode);
    }

    /// <summary>
    /// W-2 第三档 `ServerAi` 第一刀：**位置固定**的本体（`SummonMovementTable.Mode == Static`，641 / 643）
    /// 坐标改由服务端持有——客户端后续包 27 的坐标 / 速度被忽略，只确认登记。
    /// 本批**只**接管 Static 族：其余移动模式与派生弹幕仍接受客户端坐标（逐族接管中）。
    /// </summary>
    [Fact]
    public void ServerAi_Static_Body_Position_Is_Server_Owned()
    {
        static (WorldState World, int Index) CreateScenario(int type)
        {
            var world = WorldGenerator.GenerateSmall();
            var player = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Hp = 100,
                HpMax = 100,
                Position = new Vector2(320f, 460f),
                DeathNotified = true,
            };
            lock (world.PlayersLock) world.Players[1] = player;

            lock (world.ProjectilesLock)
                world.Projectiles.Add(new ProjectileEntity
                {
                    Key = 7,
                    Owner = 1,
                    Type = type,
                    IsSummon = true,
                    SummonEntityId = 1,
                    SummonKind = SummonProjectileTable.KindOf(type),
                    Position = new Vector2(320f, 400f),
                    Velocity = new Vector2(0f, 0f),
                    Damage = 100,
                    Penetrate = -1,
                    Active = true,
                });

            return (world, world.Projectiles.Count - 1);
        }

        static CommandApplyResult Move(WorldState world, int index, float x, float y) =>
            new SpawnProjectileCommand(10, 1, 7, world.Projectiles[index].Type,
                new Vector2(x, y), new Vector2(1f, 1f), 100).Apply(world, new XoshiroRng(1));

        // ClientDriven（默认）：Static 本体也照旧接受客户端坐标 —— 锁定现状，证明本档确实改变了什么
        var client = CreateScenario(641);
        Assert.False(client.World.ServerOwnsSummonPositions);
        Assert.True(Move(client.World, client.Index, 500f, 400f).Applied);
        Assert.Equal(new Vector2(500f, 400f), client.World.Projectiles[client.Index].Position);

        // ServerAi：Static（641）坐标由服务端持有 → 更新被忽略，位置与速度都不变；包 27 仍确认登记
        var stat = CreateScenario(641);
        stat.World.SummonAuthority = SummonAuthorityMode.ServerAi;
        Assert.True(stat.World.ServerOwnsSummonPositions);
        Assert.True(Move(stat.World, stat.Index, 500f, 400f).Applied);
        Assert.Equal(new Vector2(320f, 400f), stat.World.Projectiles[stat.Index].Position);
        Assert.Equal(new Vector2(0f, 0f), stat.World.Projectiles[stat.Index].Velocity);

        // 643 RainbowCrystal 同为 Static → 同样被接管
        var crystal = CreateScenario(643);
        crystal.World.SummonAuthority = SummonAuthorityMode.ServerAi;
        Assert.True(Move(crystal.World, crystal.Index, 500f, 400f).Applied);
        Assert.Equal(new Vector2(320f, 400f), crystal.World.Projectiles[crystal.Index].Position);

        // 本批**不**接管其它移动模式：Fly（387）仍接受客户端坐标
        var fly = CreateScenario(387);
        fly.World.SummonAuthority = SummonAuthorityMode.ServerAi;
        Assert.True(Move(fly.World, fly.Index, 500f, 400f).Applied);
        Assert.Equal(new Vector2(500f, 400f), fly.World.Projectiles[fly.Index].Position);

        // 派生弹幕（374）由客户端模拟 → 同样不受影响
        var shot = CreateScenario(374);
        shot.World.SummonAuthority = SummonAuthorityMode.ServerAi;
        Assert.True(Move(shot.World, shot.Index, 500f, 400f).Applied);
        Assert.Equal(new Vector2(500f, 400f), shot.World.Projectiles[shot.Index].Position);
    }

    /// <summary>
    /// W-2 第三档 `ServerAi` 第二刀：**位置硬绑定**族（`Mode == PositionBound`，831 / 970 的家点、626-628 的父节）。
    /// 原版精确位置依赖**客户端视觉状态**（公转相位 / 头饰偏移 / gfxOffY）或客户端驱动的父节，服务端无法逐帧复刻，
    /// 故只做**锚点约束**：超出 200px 容忍圈的坐标判为越权 → 吸附回锚点；圈内照常接受（不误拒合法位置）。
    /// </summary>
    [Fact]
    public void ServerAi_PositionBound_Body_Snaps_Back_To_Anchor()
    {
        static (WorldState World, PlayerRuntime Player) CreateScenario()
        {
            var world = WorldGenerator.GenerateSmall();
            var player = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Hp = 100,
                HpMax = 100,
                Position = new Vector2(320f, 460f),
                DeathNotified = true,
            };
            lock (world.PlayersLock) world.Players[1] = player;
            return (world, player);
        }

        static ProjectileEntity Add(WorldState world, int type, int key, Vector2 pos)
        {
            var p = new ProjectileEntity
            {
                Key = key,
                Owner = 1,
                Type = type,
                IsSummon = true,
                SummonEntityId = key,
                SummonKind = SummonProjectileTable.KindOf(type),
                Position = pos,
                Damage = 100,
                Penetrate = -1,
                Active = true,
            };
            lock (world.ProjectilesLock) world.Projectiles.Add(p);
            return p;
        }

        static CommandApplyResult Move(WorldState world, int type, int key, Vector2 pos) =>
            new SpawnProjectileCommand(10, 1, key, type, pos, new Vector2(0f, 0f), 100)
                .Apply(world, new XoshiroRng(1));

        // 锚点 = 主人中心 (330, 481) 上方 61 → (330, 420)
        var expectedAnchor = new Vector2(330f, 420f);

        // 831：远离主人 → 吸附回锚点
        var gem = CreateScenario();
        gem.World.SummonAuthority = SummonAuthorityMode.ServerAi;
        var gemBody = Add(gem.World, 831, 7, expectedAnchor);
        Assert.True(Move(gem.World, 831, 7, new Vector2(5000f, 400f)).Applied);
        Assert.Equal(expectedAnchor, gemBody.Position);
        Assert.Equal(new Vector2(0f, 0f), gemBody.Velocity);

        // 831：容忍圈内的合法偏移（公转环）→ 原样接受，不吸附
        // 注：项目自带的 Vector2 是纯 record（无运算符），一切向量运算按标量写
        var orbit = CreateScenario();
        orbit.World.SummonAuthority = SummonAuthorityMode.ServerAi;
        var orbitBody = Add(orbit.World, 831, 7, expectedAnchor);
        var legal = new Vector2(expectedAnchor.X + 30f, expectedAnchor.Y - 20f);
        Assert.True(Move(orbit.World, 831, 7, legal).Applied);
        Assert.Equal(legal, orbitBody.Position);

        // 970 AbigailCounter 同族 → 同样约束
        var counter = CreateScenario();
        counter.World.SummonAuthority = SummonAuthorityMode.ServerAi;
        var counterBody = Add(counter.World, 970, 7, expectedAnchor);
        Assert.True(Move(counter.World, 970, 7, new Vector2(5000f, 400f)).Applied);
        Assert.Equal(expectedAnchor, counterBody.Position);

        // 626 星尘龙节段：锚点 = 同属主头节 625 的位置 → 瞬移被拉回
        // （注意用**世界内**的远点：越界坐标会在更早的 ProjectileSpawnOutOfWorld 校验就被拒）
        var dragon = CreateScenario();
        dragon.World.SummonAuthority = SummonAuthorityMode.ServerAi;
        var head = Add(dragon.World, 625, 1, new Vector2(600f, 500f));
        var seg = Add(dragon.World, 626, 2, new Vector2(600f, 520f));
        Assert.True(Move(dragon.World, 626, 2, new Vector2(5000f, 400f)).Applied);
        Assert.Equal(head.Position, seg.Position);

        // 没有存活头节 → 锚点未知 → 不接管（失败放行，绝不误拒）
        var orphan = CreateScenario();
        orphan.World.SummonAuthority = SummonAuthorityMode.ServerAi;
        var orphanSeg = Add(orphan.World, 627, 2, new Vector2(600f, 520f));
        var orphanMove = new Vector2(5000f, 400f);
        Assert.True(Move(orphan.World, 627, 2, orphanMove).Applied);
        Assert.Equal(orphanMove, orphanSeg.Position);

        // ClientDriven（默认档）：一切照旧，不做任何吸附 —— 锁定现状
        var client = CreateScenario();
        var clientBody = Add(client.World, 831, 7, expectedAnchor);
        Assert.True(Move(client.World, 831, 7, new Vector2(5000f, 400f)).Applied);
        Assert.Equal(new Vector2(5000f, 400f), clientBody.Position);
    }

    /// <summary>
    /// W-2 第三档 `ServerAi` 第三刀（混合模型）：**AI_062 族**（373 / 375 / 407 / 423 / 613 / 963）的
    /// 服务端自算位置只写 <c>ServerPosition</c>（判定用）——表现用的 <c>Position</c> 仍是客户端上报值、不被改写。
    /// 复刻「跟随主人 + 惯性 + 速度上限 + 召回 + 超远传送」，不复刻索敌 / 冲刺。
    /// </summary>
    [Fact]
    public void ServerAi_Ai062_Body_Server_Position_Follows_Owner()
    {
        static (WorldState World, PlayerRuntime Player) CreateScenario()
        {
            var world = WorldGenerator.GenerateSmall();
            var player = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Hp = 100,
                HpMax = 100,
                Position = new Vector2(320f, 460f),
                Direction = 1,
                DeathNotified = true,
            };
            lock (world.PlayersLock) world.Players[1] = player;
            return (world, player);
        }

        // 本体 373 停在主人待命点（主人中心 (330,481) 上方 60 → (330,421)）
        var world0 = CreateScenario();
        var body0 = new ProjectileEntity
        {
            Key = 7, Owner = 1, Type = 373, IsSummon = true, SummonEntityId = 1,
            SummonKind = SummonKind.Minion, Position = new Vector2(330f, 421f),
            Damage = 30, Penetrate = -1, Active = true,
        };
        lock (world0.World.ProjectilesLock) world0.World.Projectiles.Add(body0);

        // ClientDriven：服务端完全不碰位置（ServerPosition 保持 null）
        var idleSim = new WorldSimulator(world0.World, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        idleSim.Tick();
        Assert.Null(body0.ServerPosition);

        // ServerAi：自算位置；已停在待命点时基本不动
        var world = CreateScenario();
        world.World.SummonAuthority = SummonAuthorityMode.ServerAi;
        var body = new ProjectileEntity
        {
            Key = 7, Owner = 1, Type = 373, IsSummon = true, SummonEntityId = 1,
            SummonKind = SummonKind.Minion, Position = new Vector2(330f, 421f),
            Damage = 30, Penetrate = -1, Active = true,
        };
        lock (world.World.ProjectilesLock) world.World.Projectiles.Add(body);

        var sim = new WorldSimulator(world.World, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        sim.Tick();
        Assert.NotNull(body.ServerPosition);
        Assert.True(Math.Abs(body.ServerPosition!.Value.X - 330f) < 1f);
        Assert.True(Math.Abs(body.ServerPosition!.Value.Y - 421f) < 1f);

        // 主人瞬移到 900px 外 → 服务端位置朝主人靠拢，但**表现坐标（Position）不被改写**
        float presentationX = body.Position.X;
        world.Player.Position = new Vector2(1220f, 460f);
        for (int i = 0; i < 30; i++) sim.Tick();
        float before = Math.Abs(body.ServerPosition!.Value.X - 1230f);
        for (int i = 0; i < 30; i++) sim.Tick();
        float after = Math.Abs(body.ServerPosition!.Value.X - 1230f);
        Assert.True(after < before, $"服务端位置应持续朝主人靠拢：{before} → {after}");
        Assert.Equal(presentationX, body.Position.X);   // 混合模型：表现坐标始终是客户端上报值

        // 超过召回阈值（1000）→ 提速到 15（每 tick 位移明显大于基准 6）
        Assert.True(body.ServerVelocity!.Value.X > 6f);

        // 超过超远传送阈值（2000）→ 直接吸到主人中心
        world.Player.Position = new Vector2(4000f, 460f);
        sim.Tick();
        Assert.True(Math.Abs(body.ServerPosition!.Value.X - 4010f) < 0.01f);
        Assert.Equal(0f, body.ServerVelocity!.Value.X);
    }

    /// <summary>
    /// 配对收口：对**服务端自算位置**的本体（AI_062 族），包 28 命中必须落在服务端位置的可达圈内 ——
    /// 堵住「把本体挪到全图任意 NPC 旁再报命中」。未自算位置的族（ServerPosition 为 null）不判，保持失败放行。
    /// </summary>
    [Fact]
    public void ServerAi_Ai062_Hit_Requires_Npc_Within_Server_Reach()
    {
        static (WorldState World, PlayerRuntime Player, int Index) CreateScenario(float npcX)
        {
            var world = WorldGenerator.GenerateSmall();
            world.SummonAuthority = SummonAuthorityMode.ServerAi;
            world.StrikeWeaponCheck = true;

            var player = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Hp = 100,
                HpMax = 100,
                Position = new Vector2(320f, 460f),
                Direction = 1,
                SelectedSlot = 3,
                DeathNotified = true,
            };
            player.Items[3] = 3474; // 召唤法杖 → summonAttackExpected
            player.ItemStacks[3] = 1;
            lock (world.PlayersLock) world.Players[1] = player;

            var npc = new WorldNpc
            {
                Type = 1, NetId = 1, Active = true, Life = 50_000, LifeMax = 50_000,
                Generation = 3, X = npcX, Y = 400f,
            };
            lock (world.NpcsLock) world.Npcs.Add(npc);
            return (world, player, world.Npcs.IndexOf(npc));
        }

        static void AddBody(WorldState world, int type)
        {
            lock (world.ProjectilesLock)
                world.Projectiles.Add(new ProjectileEntity
                {
                    Key = 7, Owner = 1, Type = type, IsSummon = true, SummonEntityId = 1,
                    SummonKind = SummonKind.Minion, Position = new Vector2(330f, 421f),
                    Damage = 30, Penetrate = -1, Active = true,
                });
        }

        // 目标在 5000px 外：373 的服务端位置在主人身边（约 330,421）→ 超出可达圈 → 拒绝
        var far = CreateScenario(5000f);
        AddBody(far.World, 373);
        var farSim = new WorldSimulator(far.World, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        farSim.Tick();
        Assert.NotNull(far.World.Projectiles[0].ServerPosition);
        Assert.Equal(CommandFailures.ProjectileNotColliding,
            new NpcStrikeCommand(10, 1, far.Index, 30, Generation: 3)
                .Apply(far.World, new XoshiroRng(2)).Reason);

        // 目标在可达圈内 → 照常结算
        var near = CreateScenario(600f);
        AddBody(near.World, 373);
        var nearSim = new WorldSimulator(near.World, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        nearSim.Tick();
        Assert.True(new NpcStrikeCommand(10, 1, near.Index, 30, Generation: 3)
            .Apply(near.World, new XoshiroRng(2)).Applied);

        // 飞行族（387）现在同样被接管 → 远处目标一样拒绝
        var other = CreateScenario(5000f);
        AddBody(other.World, 387);
        var otherSim = new WorldSimulator(other.World, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        otherSim.Tick();
        Assert.NotNull(other.World.Projectiles[0].ServerPosition);
        Assert.Equal(CommandFailures.ProjectileNotColliding,
            new NpcStrikeCommand(10, 1, other.Index, 30, Generation: 3)
                .Apply(other.World, new XoshiroRng(2)).Reason);

        // **派生弹幕**（374）不是本体、没有服务端位置 → 几何判定不适用（失败放行）
        var shot = CreateScenario(5000f);
        AddBody(shot.World, 374);
        var shotSim = new WorldSimulator(shot.World, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        shotSim.Tick();
        Assert.Null(shot.World.Projectiles[0].ServerPosition);
        Assert.True(new NpcStrikeCommand(10, 1, shot.Index, 30, Generation: 3)
            .Apply(shot.World, new XoshiroRng(2)).Applied);
    }

    /// <summary>
    /// W-2 第三档 `ServerAi` 第四刀：**贴地族** AI_026（Pygmy / BabySlime / 蜘蛛 / Foxsparks）与
    /// AI_067（海盗 / 蛙 / 老虎 / Flinx / 蘑菇小子 / Cattiva / 陶罐 / 禁咒）纳入服务端自算位置。
    /// 这些族按 `StanceBase + StanceStep × 同类序号` 在主人**面朝方向**横向站位；竖直取主人脚下。
    /// 本用例同时验证第二代同类型本体按步长排队，以及几何收紧对该族生效。
    /// </summary>
    [Fact]
    public void ServerAi_Ground_Families_Follow_Owner_Stance()
    {
        var world = WorldGenerator.GenerateSmall();
        world.SummonAuthority = SummonAuthorityMode.ServerAi;
        world.StrikeWeaponCheck = true;

        var player = new PlayerRuntime
        {
            Id = 1, Active = true, Hp = 100, HpMax = 100,
            Position = new Vector2(320f, 460f), Direction = 1,
            SelectedSlot = 3, DeathNotified = true,
        };
        player.Items[3] = 3474;
        player.ItemStacks[3] = 1;
        lock (world.PlayersLock) world.Players[1] = player;

        // 主人中心 (330,481)、脚下 (330,502)
        static ProjectileEntity Body(int type, int key, Vector2 pos) => new()
        {
            Key = key, Owner = 1, Type = type, IsSummon = true, SummonEntityId = key,
            SummonKind = SummonKind.Minion, Position = pos, Damage = 30, Penetrate = -1, Active = true,
        };

        var slime1 = Body(266, 1, new Vector2(370f, 502f));   // BabySlime：站位 40 + 40×序号
        var slime2 = Body(266, 2, new Vector2(410f, 502f));   // 第二只 → 40 + 40×1 = 80
        var flinx = Body(951, 3, new Vector2(385f, 502f));    // FlinxMinion：站位 55 + 30×序号
        lock (world.ProjectilesLock)
        {
            world.Projectiles.Add(slime1);
            world.Projectiles.Add(slime2);
            world.Projectiles.Add(flinx);
        }

        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        sim.Tick();

        // 三枚本体都拿到了服务端自算位置，且落在各自的站位上（已停在站位点时几乎不动）
        Assert.NotNull(slime1.ServerPosition);
        Assert.True(Math.Abs(slime1.ServerPosition!.Value.X - 370f) < 1f, $"第一只史莱姆应在 370：{slime1.ServerPosition!.Value.X}");
        Assert.True(Math.Abs(slime2.ServerPosition!.Value.X - 410f) < 1f, $"第二只史莱姆应按步长排到 410：{slime2.ServerPosition!.Value.X}");
        Assert.True(Math.Abs(flinx.ServerPosition!.Value.X - 385f) < 1f, $"Flinx 应在 385：{flinx.ServerPosition!.Value.X}");
        Assert.True(Math.Abs(slime1.ServerPosition!.Value.Y - 502f) < 1f);
        // 表现坐标不被改写（混合模型）
        Assert.Equal(370f, slime1.Position.X);

        // 主人掉头（direction = -1）→ 站位翻到另一侧
        player.Direction = -1;
        for (int i = 0; i < 60; i++) sim.Tick();
        Assert.True(slime1.ServerPosition!.Value.X < 330f, $"掉头后应站到主人左侧：{slime1.ServerPosition!.Value.X}");

        // 几何收紧对该族生效：目标在 5000px 外 → 拒绝；拉近到可达圈内 → 结算
        var npc = new WorldNpc
        {
            Type = 1, NetId = 1, Active = true, Life = 50_000, LifeMax = 50_000,
            Generation = 3, X = 5000f, Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(npc);
        int index = world.Npcs.IndexOf(npc);
        Assert.Equal(CommandFailures.ProjectileNotColliding,
            new NpcStrikeCommand(10, 1, index, 30, Generation: 3).Apply(world, new XoshiroRng(2)).Reason);

        npc.X = 400f;
        Assert.True(new NpcStrikeCommand(11, 1, index, 30, Generation: 3).Apply(world, new XoshiroRng(2)).Applied);
    }

    /// <summary>
    /// 覆盖完整性不变量：`ServerAi` 档下**全部 62 个本体**都必须拿到服务端自算位置
    /// （这是「命中几何对该族生效」的前提）；而**派生弹幕**不是本体、不应被接管。
    /// 新增本体类型时这张表会自动扩大，忘接管的类型会被这条用例抓住。
    /// </summary>
    [Fact]
    public void ServerAi_Covers_All_Body_Types()
    {
        var world = WorldGenerator.GenerateSmall();
        world.SummonAuthority = SummonAuthorityMode.ServerAi;

        var player = new PlayerRuntime
        {
            Id = 1, Active = true, Hp = 100, HpMax = 100,
            Position = new Vector2(320f, 460f), Direction = 1, DeathNotified = true,
        };
        lock (world.PlayersLock) world.Players[1] = player;

        lock (world.ProjectilesLock)
        {
            int key = 0;
            foreach (int type in SummonEntityTable.Of.Keys)
                world.Projectiles.Add(new ProjectileEntity
                {
                    Key = ++key, Owner = 1, Type = type, IsSummon = true, SummonEntityId = key,
                    SummonKind = SummonProjectileTable.KindOf(type),
                    Position = new Vector2(320f, 460f), Damage = 10, Penetrate = -1, Active = true,
                });

            // 派生弹幕（取 SummonShotTable 的第一个）——不应被接管
            int derived = SummonShotTable.Of.Values.SelectMany(v => v).First().ProjectileType;
            world.Projectiles.Add(new ProjectileEntity
            {
                Key = ++key, Owner = 1, Type = derived, IsSummon = true, SummonEntityId = key,
                Position = new Vector2(320f, 460f), Damage = 10, Penetrate = -1, Active = true,
            });
        }

        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        sim.Tick();

        lock (world.ProjectilesLock)
        {
            foreach (var p in world.Projectiles)
            {
                if (SummonEntityTable.Of.ContainsKey(p.Type))
                    Assert.True(p.ServerPosition is not null, $"本体 {p.Type} 未被服务端接管位置");
                else
                    Assert.True(p.ServerPosition is null, $"派生弹幕 {p.Type} 不应被服务端接管位置");
            }
        }
    }

    /// <summary>
    /// 阶段 G：召唤弹幕**不因背包武器移除而销毁**（原版仆从不随武器移动消失）——
    /// 武器移出背包后弹幕基准仍生效：999 拒绝、合法 46 接受。
    /// 若此处销毁弹幕，「召唤 → 移除武器 → 报 999」即无任何上界而被放行。
    /// </summary>
    [Fact]
    public void Summon_Bound_Survives_Weapon_Removed_From_Backpack()
    {
        var world = WorldGenerator.GenerateSmall();
        world.StrikeWeaponCheck = true;

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
            Life = 500,
            LifeMax = 500,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);
        int index = world.Npcs.IndexOf(slime);

        lock (world.ProjectilesLock)
            world.Projectiles.Add(new ProjectileEntity
            {
                Key = 7,
                Owner = 1,
                Type = 191,
                Position = new Vector2(320f, 398f),
                Velocity = new Vector2(0f, 0f),
                Damage = 60,
                Active = true,
            });

        // 背包槽 4 先放星尘细胞法杖（3474，60 伤）再移出背包 → 背包兜底消失、弹幕基准仍在。
        Assert.True(new SetInventorySlotCommand(1, 1, 4, 3474, 1).Apply(world, rng).Applied);
        Assert.True(new SetInventorySlotCommand(2, 1, 4, 0, 0).Apply(world, rng).Applied);
        Assert.Equal(69, CombatResolver.SummonDamageBound(world, 1, false));

        // 空手（两通道：背包 null + 弹幕 69）→ 999 拒绝、46 接受。
        Assert.False(new NpcStrikeCommand(3, 1, index, 999, Generation: 3).Apply(world, rng).Applied);
        Assert.True(new NpcStrikeCommand(4, 1, index, 46, Generation: 3).Apply(world, rng).Applied);
    }

    /// <summary>
    /// 可配置「移除召唤武器即销毁」（<see cref="WorldState.DestroySummonsOnWeaponRemoval"/>）：
    /// 开启时，召唤武器移出背包（清空 / 换出）立即销毁该玩家全部存活召唤弹幕；
    /// 空槽放入 / 换成非召唤武器不触发；默认关闭保持原版行为（见上一个用例）。
    /// </summary>
    [Fact]
    public void Summon_Projectiles_Destroyed_On_Weapon_Removal_When_Configured()
    {
        var world = WorldGenerator.GenerateSmall();
        world.DestroySummonsOnWeaponRemoval = true;

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

        void AddSummonProjectile(int key)
        {
            lock (world.ProjectilesLock)
            {
                world.Projectiles.Add(new ProjectileEntity
                {
                    Key = key,
                    Owner = 1,
                    Type = 191,
                    Position = new Vector2(320f, 380f),
                    Velocity = new Vector2(0f, 0f),
                    Damage = 60,
                    Active = true,
                });
            }
        }

        // 槽 4 放入星尘细胞法杖（3474）→ 弹幕存活；清空该槽 → 弹幕立即销毁（Active=false）。
        AddSummonProjectile(7);
        Assert.True(new SetInventorySlotCommand(1, 1, 4, 3474, 1).Apply(world, rng).Applied);
        Assert.True(world.Projectiles[0].Active);
        Assert.True(new SetInventorySlotCommand(2, 1, 4, 0, 0).Apply(world, rng).Applied);
        Assert.False(world.Projectiles[0].Active, "配置开启时移除召唤武器应立即销毁召唤弹幕");

        // 换成非召唤武器同样销毁；空槽放入召唤武器 / 换成其它非召唤武器不触发。
        AddSummonProjectile(8);
        Assert.True(new SetInventorySlotCommand(3, 1, 4, 757, 1).Apply(world, rng).Applied); // TerraBlade 放入
        Assert.True(world.Projectiles[1].Active, "非召唤武器放入空槽不销毁");
        Assert.True(new SetInventorySlotCommand(4, 1, 4, 757, 1).Apply(world, rng).Applied); // 同物品重写
        Assert.True(world.Projectiles[1].Active);
        Assert.True(new SetInventorySlotCommand(5, 1, 4, 3484, 1).Apply(world, rng).Applied); // 换出为另一非召唤武器
        Assert.True(world.Projectiles[1].Active);

        AddSummonProjectile(9);
        Assert.True(new SetInventorySlotCommand(6, 1, 5, 3474, 1).Apply(world, rng).Applied); // 召唤武器放入空槽 5
        Assert.True(world.Projectiles[2].Active, "放入召唤武器（空槽→有物）不销毁");
        Assert.True(new SetInventorySlotCommand(7, 1, 5, 0, 0).Apply(world, rng).Applied);    // 召唤武器清空
        Assert.False(world.Projectiles[2].Active, "召唤武器清空立即销毁");
    }

    /// <summary>
    /// 销毁的召唤弹幕不可被后续包 27 更新「复活」：客户端在召唤物存活期间持续上报弹幕状态，
    /// 若更新路径把 Active 置回 true，销毁即失效且包 29 永不广播。
    /// </summary>
    [Fact]
    public void Destroyed_Summon_Projectile_Is_Not_Revived_By_Update()
    {
        var world = WorldGenerator.GenerateSmall();
        world.DestroySummonsOnWeaponRemoval = true;

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

        ProjectileEntity AddSummonProjectile(int key)
        {
            var p = new ProjectileEntity
            {
                Key = key,
                Owner = 1,
                Type = 191,
                Position = new Vector2(320f, 380f),
                Velocity = new Vector2(0f, 0f),
                Damage = 60,
                Active = true,
            };
            lock (world.ProjectilesLock) world.Projectiles.Add(p);
            return p;
        }

        // 服务端销毁 → Destroyed 标记置位。
        var p1 = AddSummonProjectile(5);
        world.KillSummonedProjectiles(1);
        Assert.False(p1.Active);
        Assert.True(p1.Destroyed);

        // 客户端继续上报包 27（同 Key 更新）→ 忽略，不复活。
        var update = new SpawnProjectileCommand(6, 1, 5, 191,
            new Vector2(320f, 300f), new Vector2(0f, 0f), 60);
        Assert.True(update.Apply(world, rng).Applied);
        Assert.False(p1.Active, "已销毁弹幕不可被包 27 更新复活");

        // 未销毁弹幕正常更新。
        var p2 = AddSummonProjectile(6);
        var update2 = new SpawnProjectileCommand(7, 1, 6, 191,
            new Vector2(320f, 300f), new Vector2(0f, 0f), 60);
        Assert.True(update2.Apply(world, rng).Applied);
        Assert.True(p2.Active, "未销毁弹幕正常更新");
    }

    /// <summary>
    /// 销毁召唤弹幕的同时，服务端必须移除玩家增益列表中的召唤 Buff 并标记包 50 下发：
    /// 原版仆从由召唤 Buff 驱动存活（客户端 AI 每帧检查、Buff 消失则仆从自杀），
    /// 只销毁服务端弹幕不够——客户端 Buff 未移除时仆从不消失、仍发射弹幕造成伤害。
    /// </summary>
    [Fact]
    public void Removed_Summon_Buff_Clears_Only_Its_Minions()
    {
        var world = WorldGenerator.GenerateSmall();
        var player = new PlayerRuntime { Id = 1, Active = true };
        player.Buffs.Add(182);
        lock (world.PlayersLock) world.Players[1] = player;

        var minion = new ProjectileEntity
        {
            Key = 1, Owner = 1, Type = 191, IsSummon = true,
            SummonEntityId = 1, SummonKind = SummonKind.Minion,
            SourceSummonBuffId = 182, Active = true,
        };
        var sentry = new ProjectileEntity
        {
            Key = 2, Owner = 1, Type = 831, IsSummon = true,
            SummonEntityId = 2, SummonKind = SummonKind.Sentry,
            Active = true,
        };
        lock (world.ProjectilesLock)
        {
            world.Projectiles.Add(minion);
            world.Projectiles.Add(sentry);
        }

        Assert.True(new SetBuffsCommand(1, 1, Array.Empty<int>())
            .Apply(world, new XoshiroRng(1)).Applied);
        Assert.False(minion.Active);
        Assert.True(minion.Destroyed);
        Assert.True(sentry.Active);
    }

    [Fact]
    public void KillSummonedProjectiles_Removes_Summon_Buff_And_Marks_Buffs_Changed()
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
        player.Buffs.Add(182); // StardustMinion（星尘细胞法杖 3474）
        player.Buffs.Add(9);   // 铁皮（非召唤 Buff，应保留）
        lock (world.PlayersLock) world.Players[1] = player;

        lock (world.ProjectilesLock)
            world.Projectiles.Add(new ProjectileEntity
            {
                Key = 5,
                Owner = 1,
                Type = 191,
                Position = new Vector2(320f, 380f),
                Velocity = new Vector2(0f, 0f),
                Damage = 60,
                Active = true,
            });

        world.KillSummonedProjectiles(1);

        // 召唤 Buff 移除、非召唤 Buff 保留
        Assert.DoesNotContain(182, player.Buffs);
        Assert.Contains(9, player.Buffs);
        // 已标记待下发包 50（世界同步线程据此回写客户端）
        Assert.Contains(1, world.DrainPlayerBuffsChanged(16));
    }

    /// <summary>
    /// 客户端丢弃物品只发包 21（SyncItem）不必然发包 5 清槽，故丢弃召唤武器（包 21 路径）
    /// 在 <see cref="SpawnItemCommand"/> 中同样触发「移除召唤武器即销毁」。
    /// </summary>
    [Fact]
    public void Summon_Projectiles_Destroyed_On_Weapon_Drop_When_Configured()
    {
        var world = WorldGenerator.GenerateSmall();
        world.DestroySummonsOnWeaponRemoval = true;

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

        void AddSummonProjectile(int key)
        {
            lock (world.ProjectilesLock)
            {
                world.Projectiles.Add(new ProjectileEntity
                {
                    Key = key,
                    Owner = 1,
                    Type = 191,
                    Position = new Vector2(320f, 380f),
                    Velocity = new Vector2(0f, 0f),
                    Damage = 60,
                    Active = true,
                });
            }
        }

        // 丢弃召唤武器（3474）→ 弹幕立即销毁。
        AddSummonProjectile(7);
        var drop = new SpawnItemCommand(1, 1, 3474, 1,
            new Vector2(320f, 460f), new Vector2(0f, -2f), 0);
        Assert.True(drop.Apply(world, rng).Applied);
        Assert.False(world.Projectiles[0].Active, "配置开启时丢弃召唤武器应立即销毁召唤弹幕");

        // 丢弃非召唤武器（757 TerraBlade）→ 弹幕存活。
        AddSummonProjectile(8);
        var dropMelee = new SpawnItemCommand(2, 1, 757, 1,
            new Vector2(320f, 460f), new Vector2(0f, -2f), 0);
        Assert.True(dropMelee.Apply(world, rng).Applied);
        Assert.True(world.Projectiles[1].Active, "丢弃非召唤武器不销毁");

        // 默认配置（false）= 原版行为：丢弃召唤武器不销毁。
        var worldDefault = WorldGenerator.GenerateSmall();
        worldDefault.DestroySummonsOnWeaponRemoval = false;
        lock (worldDefault.PlayersLock) worldDefault.Players[1] = player;
        lock (worldDefault.ProjectilesLock)
        {
            worldDefault.Projectiles.Add(new ProjectileEntity
            {
                Key = 9,
                Owner = 1,
                Type = 191,
                Position = new Vector2(320f, 380f),
                Velocity = new Vector2(0f, 0f),
                Damage = 60,
                Active = true,
            });
        }
        var dropDefault = new SpawnItemCommand(3, 1, 3474, 1,
            new Vector2(320f, 460f), new Vector2(0f, -2f), 0);
        Assert.True(dropDefault.Apply(worldDefault, rng).Applied);
        Assert.True(worldDefault.Projectiles[0].Active, "默认配置保持原版行为：丢弃召唤武器不销毁");
    }

    /// <summary>
    /// 召唤本体数据表（backlog W-2 第一步，数据侧）：62 条 = 46 个物品直生本体 + 16 个本体变体。
    /// 判定本体的唯一权威是原版 <c>Projectile.SetDefaults</c> 赋 <c>minion = true</c> / <c>sentry = true</c>；
    /// 物品归属取自 <c>Item.shoot</c>（真正的数据源在 <c>Item.cs</c>，其 case 缩进不统一、
    /// 且 DD2 哨兵是 12 个物品共享 fallthrough case + 内层 switch 逐 id 精化——按 `case N:` 数会数错）。
    /// 哨兵寿命 36000（10 分钟），另有 3 个短命本体（831 / 864 / 970，timeLeft = 60）。
    /// </summary>
    [Fact]
    public void SummonEntityTable_Matches_Vanilla_SetDefaults()
    {
        Assert.Equal(62, SummonEntityTable.Of.Count);
        Assert.Equal(44, SummonEntityTable.Of.Values.Count(e => e.Kind == SummonKind.Minion));
        Assert.Equal(18, SummonEntityTable.Of.Values.Count(e => e.Kind == SummonKind.Sentry));

        // 46 条由物品直生；16 条是本体变体（由别的本体 / 增益生成，ItemId = null）
        var byItem = SummonEntityTable.Of.Where(kv => kv.Value.ItemId is not null).ToList();
        Assert.Equal(46, byItem.Count);
        Assert.Equal(46, byItem.Select(kv => kv.Value.ItemId!.Value).Distinct().Count());
        Assert.Equal(16, SummonEntityTable.Of.Values.Count(e => e.ItemId is null));
        // 哨兵全部由物品直生（没有"哨兵变体"）
        Assert.All(SummonEntityTable.Of.Where(kv => kv.Value.Kind == SummonKind.Sentry),
            kv => Assert.NotNull(kv.Value.ItemId));

        var pygmy = SummonEntityTable.Of[191];                       // PygmyStaff → Pygmy
        Assert.Equal(1157, pygmy.ItemId);
        Assert.Equal(SummonKind.Minion, pygmy.Kind);
        Assert.Equal(26, pygmy.AiStyle);
        Assert.Equal(0, pygmy.TimeLeft);

        var hydra = SummonEntityTable.Of[308];                       // FrostHydra → 哨兵，80×74，10 分钟
        Assert.Equal(1572, hydra.ItemId);
        Assert.Equal(SummonKind.Sentry, hydra.Kind);
        Assert.Equal(36000, hydra.TimeLeft);
        Assert.Equal((80, 74), (hydra.Width, hydra.Height));
        Assert.True(hydra.TileCollide, "308 未设 tileCollide → 原版默认 true（旧表记 false 是错的）");

        // 上一版漏掉的 19 条物品本体：8 条非 DD2（Hornet / FlyingImp / SpiderHiver / Retanimini /
        // VenomSpider / OneEyedPirate / Tempest / UFOMinion）+ 11 个 DD2 哨兵
        Assert.Equal(new[] { 373, 375, 377, 387, 390, 393, 407, 423 },
            SummonEntityTable.Of.Where(kv => kv.Value.ItemId is 2364 or 2365 or 2366 or 2535 or 2551 or 2584 or 2621 or 2749)
                .Select(kv => kv.Key).OrderBy(x => x));
        Assert.Equal(new[] { 663, 665, 667, 677, 678, 679, 688, 689, 690, 691, 692, 693 },
            SummonEntityTable.Of.Where(kv => new[] { 3818, 3819, 3820, 3824, 3825, 3826, 3829, 3830, 3831, 3832, 3833, 3834 }
                    .Contains(kv.Value.ItemId ?? 0))
                .Select(kv => kv.Key).OrderBy(x => x));
        // DD2 12 个物品 ↔ 12 个哨兵本体一一对应（旧表把 3834 的 shoot 记成 663，真值 693）
        Assert.Equal(3834, SummonEntityTable.Of[693].ItemId);
        Assert.Equal(3818, SummonEntityTable.Of[663].ItemId);
        Assert.Equal((130, 28, 60), (SummonEntityTable.Of[667].AiStyle, SummonEntityTable.Of[667].Width, SummonEntityTable.Of[667].Height));

        // 短命本体（timeLeft = 60）：831 StormTigerGem / 864 Smolstar / 970 AbigailCounter
        // （旧表把 759 BabyBird 也记成 60 —— 原版是 `timeLeft *= 5`，真值不是 60）
        Assert.Equal(new[] { 831, 864, 970 },
            SummonEntityTable.Of.Where(kv => kv.Value.TimeLeft == 60).Select(kv => kv.Key).OrderBy(x => x));
        Assert.Equal(0, SummonEntityTable.Of[759].TimeLeft);
        Assert.Equal(18, SummonEntityTable.Of.Values.Count(e => e.TimeLeft == 36000));

        // 命中节奏（原版 Projectile.Damage → Damage_PVE_Inner L12731-12754 / L14676-14684）
        Assert.Equal(27, SummonEntityTable.Of.Values.Count(e => e.Immunity == SummonHitImmunity.Default));
        Assert.Equal(26, SummonEntityTable.Of.Values.Count(e => e.Immunity == SummonHitImmunity.LocalPerTarget));
        Assert.Equal(9, SummonEntityTable.Of.Values.Count(e => e.Immunity == SummonHitImmunity.IdStaticShared));
        Assert.Equal(new[] { 266, 387, 388, 390, 391, 392, 407, 758, 951 },                     // 同类型共享免疫
            SummonEntityTable.Of.Where(kv => kv.Value.Immunity == SummonHitImmunity.IdStaticShared)
                .Select(kv => kv.Key).OrderBy(x => x));
        // 冷却一律取原版抽取值（服务端不得另造冷却）：-1 = 同一弹幕对同一目标终身只能命中一次
        Assert.Equal(new[] { 755, 946 },
            SummonEntityTable.Of.Where(kv => kv.Value.HitCooldownTicks == -1).Select(kv => kv.Key).OrderBy(x => x));
        var allowedCooldowns = new[] { -1, 3, 5, 7, 9, 10, 12, 15, 16, 18, 20, 30 };
        Assert.All(SummonEntityTable.Of.Values, e =>
            Assert.True(allowedCooldowns.Contains(e.HitCooldownTicks),
                $"冷却 {e.HitCooldownTicks} 不在原版抽取集合内"));
        // 穿墙本体（tileCollide = false）共 32 个
        Assert.Equal(32, SummonEntityTable.Of.Values.Count(e => !e.TileCollide));

        // 身份集合已按数据表拆开（W-2 第二步）：本体 62 ∪ 派生 25 = 87，且两者不相交。
        // 旧手写列表漏了 19 个本体、只收了 3 个派生类型（389 / 676 / 687）——那正是
        // 「派生弹幕包 28 被拒 → 伤害丢失」的根因。
        Assert.Equal(62, SummonProjectileTable.Bodies.Count);
        Assert.Equal(25, SummonProjectileTable.Shots.Count);
        Assert.Empty(SummonProjectileTable.Bodies.Intersect(SummonProjectileTable.Shots));
        Assert.Equal(87, SummonProjectileTable.Of.Count);
        Assert.All(SummonEntityTable.Of.Keys, t => Assert.Contains(t, SummonProjectileTable.Of));
        Assert.All(SummonProjectileTable.Bodies, t => Assert.True(SummonEntityTable.Of.ContainsKey(t)));
        // 档位也由数据表回答（旧 SentryTypes 把 831/946/951/970 误标为哨兵、又漏了真哨兵）
        Assert.Equal(SummonKind.Minion, SummonProjectileTable.KindOf(831));
        Assert.Equal(SummonKind.Minion, SummonProjectileTable.KindOf(970));
        Assert.Equal(SummonKind.Sentry, SummonProjectileTable.KindOf(308));
        Assert.Equal(SummonKind.Sentry, SummonProjectileTable.KindOf(1025));
        Assert.Equal(SummonKind.Minion, SummonProjectileTable.KindOf(374)); // 派生跟随发射者档位
        Assert.Equal(SummonKind.Sentry, SummonProjectileTable.KindOf(664));
        Assert.Equal(SummonKind.None, SummonProjectileTable.KindOf(3));     // 普通弹幕
    }

    /// <summary>
    /// 召唤派生弹幕表（backlog W-2 第一步，数据侧）：62 个本体全部读完原版 AI ——
    /// 32 个有派生（34 条关系、25 个派生类型），30 个确认无派生。两表互补，不存在"未解析"。
    /// 派生必须来自**本体所走的那条 AI 分支内、type 条件对本本体成立**的 NewProjectile
    /// （原版把几十个 type 塞进同一个巨型 AI，发射类型常由入口处局部变量按 type 赋值，不能按字面量就近推断）。
    /// </summary>
    [Fact]
    public void SummonShotTable_Matches_Vanilla_Ai()
    {
        static int[] Shots(int body) =>
            SummonShotTable.Of[body].Select(s => s.ProjectileType).OrderBy(x => x).ToArray();

        // 32 有派生 + 30 无派生 = 62 本体（与 SummonEntityTable 完全互补）
        Assert.Equal(32, SummonShotTable.Of.Count);
        Assert.Equal(30, SummonShotTable.NoDerivedShots.Count);
        Assert.Equal(34, SummonShotTable.Of.Values.Sum(v => v.Count));
        Assert.Equal(SummonEntityTable.Of.Keys.OrderBy(x => x),
            SummonShotTable.Of.Keys.Concat(SummonShotTable.NoDerivedShots).OrderBy(x => x));
        Assert.Empty(SummonShotTable.Of.Keys.Intersect(SummonShotTable.NoDerivedShots));

        // 派生弹幕一律不是本体（本体 62 条已定稿；若某天重叠说明分类被破坏）
        var derived = SummonShotTable.Of.Values.SelectMany(v => v).Select(s => s.ProjectileType).ToHashSet();
        Assert.Equal(25, derived.Count);
        Assert.Empty(derived.Intersect(SummonEntityTable.Of.Keys));
        // 每个派生都带 ProjectileID 名称（防"裸数字"入库）
        Assert.All(SummonShotTable.Of.Values.SelectMany(v => v), s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));

        // Pygmy 191-194 → 195（AI_026 的发射点默认 195，仅 Foxsparks flag8 改写为 1097）；
        // 同走 AI_026 的蜘蛛 390/391/392 不发射
        foreach (var pygmy in new[] { 191, 192, 193, 194 })
            Assert.Equal(new[] { 195 }, Shots(pygmy));
        foreach (var spider in new[] { 390, 391, 392 })
            Assert.DoesNotContain(spider, SummonShotTable.Of.Keys);

        // AI_062 共享块里 num48 只在 373/375/407/423/613 被赋值；963 保持 0 → 无有效派生
        Assert.Equal(new[] { 374 }, Shots(373));
        Assert.Equal(new[] { 376 }, Shots(375));
        Assert.Equal(new[] { 408 }, Shots(407));
        Assert.Equal(new[] { 433 }, Shots(423));
        Assert.Equal(new[] { 614 }, Shots(613));
        Assert.Contains(963, SummonShotTable.NoDerivedShots);

        // aiStyle 66 内联块：389 被 `if (type == 387)` 限定 → 388 / 533 不发射
        Assert.Equal(new[] { 389 }, Shots(387));
        Assert.Contains(388, SummonShotTable.NoDerivedShots);
        Assert.Contains(533, SummonShotTable.NoDerivedShots);

        // AI_067 唯一发射点被 `type == 1022` 包裹（海盗 / 蛙 / Flinx 不发射）；
        // 老虎 833/834/835 走专用 AI_067_TigerSpecialAttack → 818
        foreach (var pirateOrFrog in new[] { 393, 394, 395, 758, 951, 1093, 1112, 1118 })
            Assert.Contains(pirateOrFrog, SummonShotTable.NoDerivedShots);
        foreach (var tier in new[] { 833, 834, 835 })
            Assert.Equal(new[] { 818 }, Shots(tier));
        Assert.Equal(new[] { 1044 }, Shots(1022));
        Assert.Equal(new[] { 1120 }, Shots(1119));

        // DD2 三档由入口默认值 + switch(type) 覆写决定（不得按 ID 相邻推断）
        Assert.Equal(new[] { 664 }, Shots(663));
        Assert.Equal(new[] { 666 }, Shots(665));
        Assert.Equal(new[] { 668 }, Shots(667));
        foreach (var ballista in new[] { 677, 678, 679 }) Assert.Equal(new[] { 680 }, Shots(ballista));
        Assert.Equal(new[] { 694 }, Shots(691));
        Assert.Equal(new[] { 695 }, Shots(692));
        Assert.Equal(new[] { 696 }, Shots(693));
        // LightningAura 只有范围接触判定，不发射
        foreach (var aura in new[] { 688, 689, 690 }) Assert.Contains(aura, SummonShotTable.NoDerivedShots);

        // 哨兵派生（AI_053 按 type 覆写 num15；炮台/水晶按攻击计数阈值）
        Assert.Equal(new[] { 309 }, Shots(308));
        Assert.Equal(new[] { 378 }, Shots(377));
        Assert.Equal(new[] { 967 }, Shots(966));
        Assert.Equal(new[] { 642 }, Shots(641));
        Assert.Equal(new[] { 644 }, Shots(643));
        Assert.Equal(new[] { 1026 }, Shots(1025));

        // 星尘龙 625-628 的 AI 只做节段重连（节段由物品 3531 召唤时生成）→ 本体不发射
        foreach (var dragon in new[] { 625, 626, 627, 628 }) Assert.Contains(dragon, SummonShotTable.NoDerivedShots);

        // Foxsparks 1094/1113：普通攻击 → 1097，引导武器 → 1106（同表两条关系）
        foreach (var fox in new[] { 1094, 1113 }) Assert.Equal(new[] { 1097, 1106 }, Shots(fox));

        // 媒介（831 → 833/834/835、970 → 963）本身不发射
        Assert.Contains(831, SummonShotTable.NoDerivedShots);
        Assert.Contains(970, SummonShotTable.NoDerivedShots);
    }

    /// <summary>
    /// 召唤行为参数表（backlog W-2 第一步，数据侧）：给 32 个**会发射**的本体记录索敌射程与攻击间隔。
    /// 三条口径必须守住：①射程度量不统一（欧氏 / 曼哈顿 / owner 矩形）；②间隔多为「阈值 + 随机累加」，
    /// 表里记均值；③依赖玩家护甲套装的哨兵记无套装默认值。
    /// </summary>
    [Fact]
    public void SummonBehaviorTable_Matches_Vanilla_Ai()
    {
        // 与派生弹幕表的键集合逐一对应（不发射的本体没有"攻击间隔"）
        Assert.Equal(32, SummonBehaviorTable.Of.Count);
        Assert.Equal(SummonShotTable.Of.Keys.OrderBy(x => x), SummonBehaviorTable.Of.Keys.OrderBy(x => x));

        Assert.All(SummonBehaviorTable.Of.Values, b =>
        {
            Assert.True(b.TargetingRange > 0f, "射程必须为正");
            Assert.True(b.AttackIntervalTicks > 0, "攻击间隔必须为正");
            Assert.True(b.FirstShotDelayTicks >= 0, "首射前摇不得为负");
        });
        // 首射前摇 120 只属于 AI_053 系的三个哨兵（ai[0] 初始化为 120）
        Assert.Equal(new[] { 308, 377, 966 },
            SummonBehaviorTable.Of.Where(kv => kv.Value.FirstShotDelayTicks == 120)
                .Select(kv => kv.Key).OrderBy(x => x));

        // AI_026：射程 800（+40×minionPos），冷却 num135
        foreach (var pygmy in new[] { 191, 192, 193, 194 })
            Assert.Equal((800f, 30), (SummonBehaviorTable.Of[pygmy].TargetingRange, SummonBehaviorTable.Of[pygmy].AttackIntervalTicks));
        Assert.Equal((800f, 42), (SummonBehaviorTable.Of[1094].TargetingRange, SummonBehaviorTable.Of[1094].AttackIntervalTicks));
        Assert.Equal((1160f, 30), (SummonBehaviorTable.Of[1113].TargetingRange, SummonBehaviorTable.Of[1113].AttackIntervalTicks));

        // AI_062：索敌半径被入口处 num12 = 2000 无条件覆写（400/300 是死赋值）
        foreach (var minion in new[] { 373, 375, 407, 423, 613 })
            Assert.Equal(2000f, SummonBehaviorTable.Of[minion].TargetingRange);
        Assert.Equal(45, SummonBehaviorTable.Of[373].AttackIntervalTicks);   // 阈值 90、每帧 +1~3 → ≈45
        Assert.Equal(30, SummonBehaviorTable.Of[407].AttackIntervalTicks);   // 阈值 50 → ≈30
        Assert.Equal(27, SummonBehaviorTable.Of[423].AttackIntervalTicks);   // 阈值 45 → ≈27（且开火需 ≤400）
        Assert.Equal(36, SummonBehaviorTable.Of[613].AttackIntervalTicks);   // 阈值 60 → ≈36（且开火需 ≤500）

        // aiStyle 66 内联块：387 索敌 2000、冷却阈值 90
        Assert.Equal((2000f, 45), (SummonBehaviorTable.Of[387].TargetingRange, SummonBehaviorTable.Of[387].AttackIntervalTicks));

        // 哨兵：AI_053 系是**曼哈顿** 1000、间隔 60/90、首射前摇 120；AI_123 是**欧氏** 1000
        Assert.Equal(SummonRangeMetric.Manhattan, SummonBehaviorTable.Of[308].RangeMetric);
        Assert.Equal(SummonRangeMetric.Manhattan, SummonBehaviorTable.Of[1025].RangeMetric);
        Assert.Equal(SummonRangeMetric.Euclidean, SummonBehaviorTable.Of[641].RangeMetric);
        Assert.Equal(60, SummonBehaviorTable.Of[308].AttackIntervalTicks);
        Assert.Equal(90, SummonBehaviorTable.Of[966].AttackIntervalTicks);
        Assert.Equal(30, SummonBehaviorTable.Of[641].AttackIntervalTicks);
        Assert.Equal(25, SummonBehaviorTable.Of[643].AttackIntervalTicks);
        Assert.Equal(1240f, SummonBehaviorTable.Of[1025].TargetingRange);    // 1000 + 15×16

        // DD2 三档：射程同 900，间隔按档递减；弩车三档共用同一套数值（冷却由玩家装备决定）
        Assert.Equal(new[] { 104, 102, 92 },
            new[] { 663, 665, 667 }.Select(t => SummonBehaviorTable.Of[t].AttackIntervalTicks));
        Assert.All(new[] { 663, 665, 667 },
            t => Assert.Equal((900f, SummonRangeMetric.Euclidean), (SummonBehaviorTable.Of[t].TargetingRange, SummonBehaviorTable.Of[t].RangeMetric)));
        foreach (var ballista in new[] { 677, 678, 679 })
            Assert.Equal((900f, 185), (SummonBehaviorTable.Of[ballista].TargetingRange, SummonBehaviorTable.Of[ballista].AttackIntervalTicks));

        // 爆裂陷阱是**矩形相交**（144×144），不是距离；冷却 = 无套装 90
        foreach (var trap in new[] { 691, 692, 693 })
            Assert.Equal((144f, SummonRangeMetric.OwnerRect, 90),
                (SummonBehaviorTable.Of[trap].TargetingRange, SummonBehaviorTable.Of[trap].RangeMetric, SummonBehaviorTable.Of[trap].AttackIntervalTicks));

        // 老虎：owner 中心 1600×800 矩形，冷却按档 360 / 300 / 240
        Assert.Equal(new[] { 360, 300, 240 },
            new[] { 833, 834, 835 }.Select(t => SummonBehaviorTable.Of[t].AttackIntervalTicks));
        Assert.All(new[] { 833, 834, 835 },
            t => Assert.Equal((1600f, SummonRangeMetric.OwnerRect), (SummonBehaviorTable.Of[t].TargetingRange, SummonBehaviorTable.Of[t].RangeMetric)));

        // 蘑菇小子（800 + 60 内爆炸）与禁咒仆从（800 + CanHitLine）
        Assert.Equal((800f, 135), (SummonBehaviorTable.Of[1022].TargetingRange, SummonBehaviorTable.Of[1022].AttackIntervalTicks));
        Assert.Equal((800f, 110), (SummonBehaviorTable.Of[1119].TargetingRange, SummonBehaviorTable.Of[1119].AttackIntervalTicks));
    }

    /// <summary>
    /// 阶段 G：召唤弹幕不随默认 300 tick 超时销毁（否则合法召唤物命中会失去伤害基准），
    /// 位置由客户端包 27 权威更新而非服务端直线积分。
    /// </summary>
    [Fact]
    public void Summon_Projectiles_Skip_Timeout_And_Integration()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        lock (world.ProjectilesLock)
            world.Projectiles.Add(new ProjectileEntity
            {
                Key = 9,
                Owner = 1,
                Type = 191,
                Position = new Vector2(200f, 200f),
                Velocity = new Vector2(0f, 0f),
                Damage = 60,
                TimeLeft = 1, // 即使只剩 1 tick，召唤弹幕也不因超时失效
                Active = true,
            });

        sim.Tick();
        lock (world.ProjectilesLock)
        {
            var p = Assert.Single(world.Projectiles);
            Assert.True(p.Active, "召唤弹幕不应因默认超时被销毁");
            Assert.Equal(200f, p.Position.X); // 未被直线积分推进
        }
    }

    /// <summary>
    /// 阶段 E-4：武器前缀伤害倍率参与近战武器校验。
    /// 木剑（24，7 伤）无前缀上界 = ceil(7×1.15)=9；前缀 57（凶残 +18%）→ base = ceil(7×1.18)=9，
    /// 上界 = ceil(9×1.15)=11 —— 不带前缀倍率会把合法命中 11 误拒。前缀数据经 SetInventorySlotCommand 落库。
    /// </summary>
    [Fact]
    public void StrikeBound_Includes_WeaponPrefix_Multiplier()
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
        Assert.True(new SetInventorySlotCommand(1, 1, 3, 24, 1, Prefix: 57).Apply(world, rng).Applied);
        Assert.True(new MoveCommand(2, 1, player.Position) { SelectedItem = 3, ControlBits = 0 }.Apply(world, rng).Applied);
        Assert.Equal(57, player.ItemPrefixes[3]);

        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 100,
            LifeMax = 100,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);
        int index = world.Npcs.IndexOf(slime);

        // 无前缀上界 9（7×1.15 上取整）；前缀 57（+18%）→ base ceil(7×1.18)=9 → 上界 11
        Assert.Equal(9, CombatResolver.WeaponDamageBound(player, 24, false));
        Assert.Equal(9, CombatResolver.GetWeaponDamage(player, 24, 57));
        Assert.Equal(11, CombatResolver.WeaponDamageBound(player, 24, false, 57));

        // 带前缀的合法命中 11 必须被接受（StrikeWeaponCheck 默认开启），12 超界拒绝
        Assert.True(new NpcStrikeCommand(3, 1, index, 11, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(89, slime.Life);
        Assert.False(new NpcStrikeCommand(4, 1, index, 12, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(89, slime.Life);
    }

    /// <summary>
    /// 阶段 F：远程武器上界校验（含弹药合并，原版 ItemCheck_Shoot 口径）。
    /// 木弓（39，4 伤，箭 40）：无弹药上界 ceil(4×1.15)=5；放木箭（40，5 伤）→ 合 9 → 上界 11；
    /// 换烈焰箭（41，7 伤）→ 合 11 → 上界 13；暴击 ×2 → 26。
    /// 火枪（95，13 伤，子弹 97）+ 陨星弹（234，8 伤）→ 合 21 → 上界 25；
    /// 吹管（281，9 伤，镖 283）+ 毒镖（1310，10 伤）→ 合 19 → 上界 22。
    /// 酒醉（25，远程+10%）同时放大武器与弹药：木弓+木箭 → 武器 (int)(4×1.1)=4、弹药 ceil(5×1.1)=6 → 合 10 → 上界 12。
    /// 非远程 / 未收录武器（木剑 24、99999）→ null（失败放行）。
    /// 端到端：手持木弓 + 木箭，上报 12（>11）被拒、11 被收 → 服务端按防御减伤结算 89。
    /// </summary>
    [Fact]
    public void RangedDamageBound_Merges_Ammo_And_Modifiers()
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

        // 无弹药：上界 = 武器伤害 ×1.15（弹药合并为 0）
        Assert.Equal(5, CombatResolver.RangedDamageBound(player, 39, false));   // 木弓 4 伤
        Assert.Equal(15, CombatResolver.RangedDamageBound(player, 95, false));  // 火枪 13 伤

        // 木弓 + 木箭（40，5 伤）→ 4+5=9 → 上界 ceil(9×1.15)=11
        Assert.True(new SetInventorySlotCommand(1, 1, 50, 40, 1).Apply(world, rng).Applied);
        Assert.Equal(11, CombatResolver.RangedDamageBound(player, 39, false));

        // 烈焰箭（41，7 伤）→ 4+7=11 → 上界 13；暴击 ×2 → 26
        Assert.True(new SetInventorySlotCommand(2, 1, 50, 41, 1).Apply(world, rng).Applied);
        Assert.Equal(13, CombatResolver.RangedDamageBound(player, 39, false));
        Assert.Equal(26, CombatResolver.RangedDamageBound(player, 39, true));

        // 火枪（95）+ 陨星弹（234，8 伤）→ 13+8=21 → 上界 25
        Assert.True(new SetInventorySlotCommand(3, 1, 50, 234, 1).Apply(world, rng).Applied);
        Assert.Equal(25, CombatResolver.RangedDamageBound(player, 95, false));

        // 吹管（281）+ 毒镖（1310，10 伤）→ 9+10=19 → 上界 22
        Assert.True(new SetInventorySlotCommand(4, 1, 50, 1310, 1).Apply(world, rng).Applied);
        Assert.Equal(22, CombatResolver.RangedDamageBound(player, 281, false));

        // 酒醉（25，远程+10%）：木弓 4 伤 → 武器 (int)(4×1.1)=4、弹药 ceil(5×1.1)=6 → 合 10 → 上界 12
        Assert.True(new SetInventorySlotCommand(5, 1, 50, 40, 1).Apply(world, rng).Applied);
        Assert.True(new SetBuffsCommand(6, 1, new[] { 25 }).Apply(world, rng).Applied);
        Assert.Equal(12, CombatResolver.RangedDamageBound(player, 39, false));
        Assert.True(new SetBuffsCommand(7, 1, Array.Empty<int>()).Apply(world, rng).Applied);

        // 非远程 / 未收录 → null（失败放行）
        Assert.Null(CombatResolver.RangedDamageBound(player, 24, false));    // 木剑（近战）
        Assert.Null(CombatResolver.RangedDamageBound(player, 99999, false)); // 未收录

        // 端到端：手持木弓（槽 3）+ 木箭（槽 50），上报 12 超界被拒、11 被收 → 89
        Assert.True(new SetInventorySlotCommand(8, 1, 3, 39, 1).Apply(world, rng).Applied);
        Assert.True(new SetInventorySlotCommand(9, 1, 50, 40, 1).Apply(world, rng).Applied);
        Assert.True(new MoveCommand(10, 1, player.Position) { SelectedItem = 3, ControlBits = PlayerRuntime.ControlUseItem }.Apply(world, rng).Applied);
        Assert.Equal(3, player.SelectedSlot);

        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 100,
            LifeMax = 100,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);
        int index = world.Npcs.IndexOf(slime);

        // 已收录远程武器的包 28 必须由服务端登记且与目标碰撞的弹幕背书。
        Assert.True(new SpawnProjectileCommand(11, 1, 1, 1, new Vector2(slime.X, slime.Y), new Vector2(0, 0), 9)
            .Apply(world, rng).Applied);
        Assert.False(new NpcStrikeCommand(12, 1, index, 12, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(100, slime.Life);
        Assert.True(new NpcStrikeCommand(13, 1, index, 11, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(89, slime.Life);
    }

    [Fact]
    public void RangedStrike_Requires_Own_Active_Colliding_Projectile()
    {
        static (WorldState World, PlayerRuntime Player, WorldNpc Npc, int Index) CreateScenario()
        {
            var world = WorldGenerator.GenerateSmall();
            var player = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Position = new Vector2(320f, 460f),
                AimPosition = new Vector2(320f, 460f),
                SelectedSlot = 3,
            };
            player.Items[3] = 39;
            player.ItemStacks[3] = 1;
            player.Items[4] = 40;
            player.ItemStacks[4] = 20;
            var npc = new WorldNpc
            {
                Type = 1,
                NetId = 1,
                Active = true,
                Life = 100,
                LifeMax = 100,
                Generation = 3,
                X = 320f,
                Y = 400f,
            };
            lock (world.PlayersLock) world.Players[1] = player;
            int index;
            lock (world.NpcsLock)
            {
                world.Npcs.Add(npc);
                index = world.Npcs.IndexOf(npc);
            }
            return (world, player, npc, index);
        }

        var rng = new XoshiroRng(1);

        // 包 27 被类型校验拒绝后，不能再直接用包 28 扣血。
        var rejectedSpawn = CreateScenario();
        Assert.Equal(CommandFailures.ProjectileTypeNotAllowed,
            new SpawnProjectileCommand(1, 1, 1, 2, new Vector2(320f, 400f), new Vector2(0, 0), 9)
                .Apply(rejectedSpawn.World, rng).Reason);
        var noProjectile = new NpcStrikeCommand(2, 1, rejectedSpawn.Index, 9, Generation: 3)
            .Apply(rejectedSpawn.World, rng);
        Assert.Equal(CommandFailures.ProjectileRequired, noProjectile.Reason);
        Assert.Equal(100, rejectedSpawn.Npc.Life);

        // 服务端登记的自有弹幕与 NPC AABB 接触时，合理伤害允许结算。
        var colliding = CreateScenario();
        Assert.True(new SpawnProjectileCommand(3, 1, 1, 1, new Vector2(320f, 400f), new Vector2(0, 0), 9)
            .Apply(colliding.World, rng).Applied);
        Assert.True(new NpcStrikeCommand(4, 1, colliding.Index, 9, Generation: 3)
            .Apply(colliding.World, rng).Applied);
        Assert.Equal(91, colliding.Npc.Life);

        // 自有存活弹幕远离 NPC，不能作为包 28 的背书。
        var distant = CreateScenario();
        lock (distant.World.ProjectilesLock)
            distant.World.Projectiles.Add(new ProjectileEntity
            {
                Key = 1, Owner = 1, Type = 1, Position = new Vector2(1000f, 1000f), Damage = 9, Active = true,
            });
        var notColliding = new NpcStrikeCommand(5, 1, distant.Index, 9, Generation: 3).Apply(distant.World, rng);
        Assert.Equal(CommandFailures.ProjectileNotColliding, notColliding.Reason);
        Assert.Equal(100, distant.Npc.Life);

        // 其他玩家的碰撞弹幕不能为当前玩家背书。
        var otherOwner = CreateScenario();
        lock (otherOwner.World.ProjectilesLock)
            otherOwner.World.Projectiles.Add(new ProjectileEntity
            {
                Key = 1, Owner = 2, Type = 1, Position = new Vector2(320f, 400f), Damage = 9, Active = true,
            });
        var foreignProjectile = new NpcStrikeCommand(6, 1, otherOwner.Index, 9, Generation: 3).Apply(otherOwner.World, rng);
        Assert.Equal(CommandFailures.ProjectileRequired, foreignProjectile.Reason);
        Assert.Equal(100, otherOwner.Npc.Life);

        // 已销毁的自有弹幕不再能为包 28 背书。
        var destroyed = CreateScenario();
        lock (destroyed.World.ProjectilesLock)
            destroyed.World.Projectiles.Add(new ProjectileEntity
            {
                Key = 1, Owner = 1, Type = 1, Position = new Vector2(320f, 400f), Damage = 9, Active = false,
            });
        var destroyedProjectile = new NpcStrikeCommand(7, 1, destroyed.Index, 9, Generation: 3).Apply(destroyed.World, rng);
        Assert.Equal(CommandFailures.ProjectileRequired, destroyedProjectile.Reason);
        Assert.Equal(100, destroyed.Npc.Life);
    }

    /// <summary>
    /// 阶段 E-4：空槽清空时前缀同步清零（避免残留前缀误放大后续武器上界）。
    /// </summary>
    [Fact]
    public void SetInventorySlot_ClearsPrefix_WhenSlotEmptied()
    {
        var world = WorldGenerator.GenerateSmall();
        var player = new PlayerRuntime { Id = 1, Active = true, Hp = 100, HpMax = 100 };
        lock (world.PlayersLock) world.Players[1] = player;
        var rng = new XoshiroRng(1);

        Assert.True(new SetInventorySlotCommand(1, 1, 3, 24, 1, Prefix: 57).Apply(world, rng).Applied);
        Assert.Equal(24, player.Items[3]);
        Assert.Equal(57, player.ItemPrefixes[3]);

        Assert.True(new SetInventorySlotCommand(2, 1, 3, 0, 0, Prefix: 57).Apply(world, rng).Applied);
        Assert.Equal(0, player.Items[3]);
        Assert.Equal(0, player.ItemPrefixes[3]);
    }

    /// <summary>
    /// 阶段 E-4：<see cref="WorldState.StrikeWeaponCheck"/> 默认开启——
    /// 未显式配置的世界里，无弹幕近战命中超出武器上界即被拒绝（此前默认关）。
    /// </summary>
    [Fact]
    public void StrikeWeaponCheck_Defaults_To_On()
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
        Assert.True(new SetInventorySlotCommand(1, 1, 3, 3520, 1).Apply(world, rng).Applied);
        Assert.True(new MoveCommand(2, 1, player.Position) { SelectedItem = 3, ControlBits = 0 }.Apply(world, rng).Applied);

        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 100,
            LifeMax = 100,
            Generation = 3,
            X = 320f,
            Y = 400f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);
        int index = world.Npcs.IndexOf(slime);

        // 金阔剑（15 伤）上界 18；未设置开关（默认 true）→ 报 19 拒、报 18 收
        Assert.True(world.StrikeWeaponCheck);
        Assert.False(new NpcStrikeCommand(3, 1, index, 19, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(100, slime.Life);
        Assert.True(new NpcStrikeCommand(4, 1, index, 18, Generation: 3).Apply(world, rng).Applied);
        Assert.Equal(82, slime.Life);
    }

    /// <summary>
    /// 阶段 E：包 117 上界随 **Buff 防御**（铁皮）实时降低：
    /// 接触史莱姆 7 伤，0 防御上界 = ceil(7×1.15)−0 = 9；铁皮（5，+8 防）→ 上界 = 9−round(8×0.5)=5。
    /// </summary>
    [Fact]
    public void HurtBound_Follows_BuffDefense_RealTime()
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

        // 放置一只史莱姆钉在玩家碰撞盒中心（接触伤害 7）
        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            Active = true,
            Life = 100,
            LifeMax = 100,
            Defense = 0,
            Generation = 3,
            X = player.Position.X,
            Y = player.Position.Y + 21f,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);

        // 无 buff：上界 9，报 9 收 → 91
        Assert.Equal(9, CombatResolver.CalculateDamagePlayersTake(
            (int)Math.Ceiling(7 * 1.15f), player.Defense, GameMode.Classic));
        Assert.True(new DamagePlayerCommand(1, 1, 9).Apply(world, rng).Applied);
        Assert.Equal(91, player.Hp);

        // 铁皮（5）→ 防御 8，上界降为 5：报 6 拒、报 5 收 → 86
        Assert.True(new SetBuffsCommand(2, 1, new[] { 5 }).Apply(world, rng).Applied);
        Assert.Equal(8, player.Defense);
        Assert.False(new DamagePlayerCommand(3, 1, 6).Apply(world, rng).Applied);
        Assert.Equal(91, player.Hp);
        player.HurtCooldown = 0; // 免伤帧到期（40 tick 免疫已过），允许下一次受击上报
        Assert.True(new DamagePlayerCommand(4, 1, 5).Apply(world, rng).Applied);
        Assert.Equal(86, player.Hp);
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

    // ---------- 3B：NPC 增益同步（包 53 入站并入 + 包 54 全量回写） ----------

    /// <summary>服务端有权威 NPC 时，包 53 上报并入增益并触发包 54 变更标记。</summary>
    [Fact]
    public void ApplyNpcBuff_StoresBuff_And_MarksBuffsChanged()
    {
        var world = new WorldState();
        int index;
        lock (world.NpcsLock)
        {
            var npc = new WorldNpc
            {
                Type = 1,
                NetId = 1,
                Active = true,
                Life = 25,
                LifeMax = 25,
            };
            world.Npcs.Add(npc);
            index = world.Npcs.IndexOf(npc);
        }
        var rng = new XoshiroRng(1);

        // 并入一条减益（包 53 语义）
        Assert.True(new ApplyNpcBuffCommand(1, 1, index, 24, Time: 480).Apply(world, rng).Applied);
        lock (world.NpcsLock)
        {
            var npc = world.Npcs[index];
            Assert.Equal(480, npc.Buffs[24]);
        }
        Assert.Contains(index, world.DrainNpcBuffsChanged(8));

        // 同类型已存在 → 应该不超 5 槽上限（覆盖更新）
        Assert.True(new ApplyNpcBuffCommand(2, 1, index, 24, Time: 900).Apply(world, rng).Applied);
        Assert.True(new ApplyNpcBuffCommand(3, 1, index, 30, Time: 300).Apply(world, rng).Applied);
        Assert.True(new ApplyNpcBuffCommand(4, 1, index, 33, Time: 600).Apply(world, rng).Applied);
        Assert.True(new ApplyNpcBuffCommand(5, 1, index, 35, Time: 120).Apply(world, rng).Applied);
        lock (world.NpcsLock)
        {
            var npc = world.Npcs[index];
            Assert.True(npc.Buffs.Count <= 5, "NPC 增益槽超过 5");
            Assert.Equal(900, npc.Buffs[24]);
            Assert.Equal(4, npc.Buffs.Count);
        }

        // 时长 ≤0 → 移除该减益
        Assert.True(new ApplyNpcBuffCommand(6, 1, index, 24, Time: 0).Apply(world, rng).Applied);
        lock (world.NpcsLock) Assert.False(world.Npcs[index].Buffs.ContainsKey(24));
    }

    /// <summary>无玩家身份 / NPC 槽越界 / 目标已死 → 拒接且不误标记。</summary>
    [Fact]
    public void ApplyNpcBuff_Rejects_InvalidTargets()
    {
        var world = new WorldState();
        lock (world.NpcsLock)
        {
            world.Npcs.Add(new WorldNpc { Type = 1, NetId = 1, Active = false });
        }
        var rng = new XoshiroRng(1);

        // 槽越界
        Assert.False(new ApplyNpcBuffCommand(1, 1, 99, 24, 300).Apply(world, rng).Applied);
        // 目标 inactive
        Assert.False(new ApplyNpcBuffCommand(2, 1, 0, 24, 300).Apply(world, rng).Applied);
        // 未标记任何变更
        Assert.Empty(world.DrainNpcBuffsChanged(8));
    }

    /// <summary>包 40（编码 → 解码）对称：SyncTalkNPC 载荷往返一致。</summary>
    [Fact]
    public void SyncTalkNpc_RoundTrips_In_Codec()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var decoder = new PacketDecoder();
        var memory = new ArrayBufferWriter<byte>();

        encoder.Encode(memory, PacketId.SyncTalkNPC, new SyncTalkNpcPacket(PlayerId: 3, TalkNpc: 17));

        var buffer = new ReadOnlySequence<byte>(memory.WrittenMemory);
        Assert.True(decoder.TryDecodeFrame(ref buffer, new DecodeContext(), out var decoded));
        var typed = Assert.IsType<SyncTalkNpcPacket>(decoded);

        Assert.Equal(3, typed.PlayerId);
        Assert.Equal(17, typed.TalkNpc);
    }

    /// <summary>
    /// 包 23（编码 → 解码）对称：**难度覆盖段**（bitsB.bit2 + float，原版 <c>NPC.difficulty</c>）与
    /// **玩家数段**（bitsB.bit0 + byte，原版 <c>statsAreScaledForThisManyPlayers</c>）
    /// 带值时往返一致，缺省（经典 1 / 单人 1）不写这两段 —— 客户端据它们自算 NPC 生命上限。
    /// </summary>
    [Fact]
    public void NpcUpdate_Difficulty_Override_RoundTrips_In_Codec()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var decoder = new PacketDecoder();

        static NpcUpdatePacket Decode(PacketEncoder encoder, PacketDecoder decoder, NpcUpdatePacket packet)
        {
            var memory = new ArrayBufferWriter<byte>();
            encoder.Encode(memory, PacketId.NpcUpdate, packet);
            var buffer = new ReadOnlySequence<byte>(memory.WrittenMemory);
            Assert.True(decoder.TryDecodeFrame(ref buffer, new DecodeContext(), out var decoded));
            return Assert.IsType<NpcUpdatePacket>(decoded);
        }

        var expert = Decode(encoder, decoder, new NpcUpdatePacket(
            3, 7, default, default, 255, 1,
            Life: 10, LifeMax: 50, Ai: new[] { 1f, 2f, 3f, 4f }, Difficulty: 2f, PlayerCount: 3));
        Assert.Equal(2f, expert.Difficulty);
        Assert.Equal(3, expert.PlayerCount);
        Assert.Equal(new[] { 1f, 2f, 3f, 4f }, expert.Ai);

        var classic = Decode(encoder, decoder, new NpcUpdatePacket(
            3, 7, default, default, 255, 1, Life: 10, LifeMax: 25));
        Assert.Equal(1f, classic.Difficulty);
        Assert.Equal(1, classic.PlayerCount);
    }

    /// <summary>包 54（编码 → 解码）对称：NpcBuffSync 载荷往返一致。</summary>
    [Fact]
    public void NpcBuffSync_RoundTrips_In_Codec()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var decoder = new PacketDecoder();
        var memory = new ArrayBufferWriter<byte>();

        var packet = new NpcBuffSyncPacket(NpcId: 7, new[] { new NpcBuffEntry(24, 480), new NpcBuffEntry(30, 300) });
        encoder.Encode(memory, PacketId.NpcBuffSync, packet);

        var buffer = new ReadOnlySequence<byte>(memory.WrittenMemory);
        Assert.True(decoder.TryDecodeFrame(ref buffer, new DecodeContext(), out var decoded));
        var typed = Assert.IsType<NpcBuffSyncPacket>(decoded);

        Assert.Equal(7, typed.NpcId);
        Assert.Collection(typed.Buffs,
            e => { Assert.Equal(24, e.Type); Assert.Equal(480, e.Time); },
            e => { Assert.Equal(30, e.Type); Assert.Equal(300, e.Time); });
    }

    // ---------- 3C(A)：服务端弹幕权威（图格碰撞 / 追踪 / 反弹） ----------

    /// <summary>
    /// 追踪弹幕（type 20 Demon Scythe，Homing=true）：向左侧飞行的弹幕，存在右侧敌怪时应逐步转向，
    /// 速度方向变为朝目标（Velocity.X 转正），且速度模长保持不变、不因追踪被图格误杀。
    /// </summary>
    [Fact]
    public void Projectile_With_Homing_SteersTowardEnemy()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        int sy = world.SpawnTileY;
        int sx = world.SpawnTileX;

        // 清出大块空域（含左侧初始漂移方向），避免追踪过程撞地形
        for (int y = sy - 20; y < sy - 2; y++)
            for (int x = sx - 40; x < sx + 70; x++)
            {
                ref var t = ref world.Tiles[x, y];
                t.Active = false;
                t.Type = 0;
            }

        var enemy = new WorldNpc
        {
            Type = 1, NetId = 1, AiStyle = 1, Active = true, Life = 25, LifeMax = 25,
            X = (sx + 40) * 16f,            // 目标在右侧
            Y = (sy - 8) * 16f,
        };
        lock (world.NpcsLock) world.Npcs.Add(enemy);

        var proj = new ProjectileEntity
        {
            Key = -1, Owner = -1, Type = 20,
            Position = new Vector2(sx * 16f, (sy - 8) * 16f),
            Velocity = new Vector2(-5f, 0f), // 初始向左飞
            Damage = 20, TimeLeft = 480, Active = true,
        };
        lock (world.ProjectilesLock) world.Projectiles.Add(proj);

        float initSpeed = MathF.Sqrt(proj.Velocity.X * proj.Velocity.X + proj.Velocity.Y * proj.Velocity.Y);

        for (int i = 0; i < 60; i++) sim.Tick();

        lock (world.ProjectilesLock)
        {
            Assert.True(proj.Active, "追踪弹幕在转向过程中意外失效");
            Assert.True(proj.Velocity.X > 0f,
                $"追踪未转向目标：速度仍朝左 (Vx={proj.Velocity.X:F2})");
        }
        float speedAfter = MathF.Sqrt(proj.Velocity.X * proj.Velocity.X + proj.Velocity.Y * proj.Velocity.Y);
        Assert.InRange(speedAfter, initSpeed - 0.01f, initSpeed + 0.01f); // 追踪只转向、不加减速
    }

    /// <summary>
    /// 反弹弹幕（type 81 Water Bolt，Bounces&gt;0 + TileCollide）：撞到实心墙应反弹（X 速度翻号），
    /// 不失效；反弹预算（BouncesLeft）随反弹递减。
    /// </summary>
    [Fact]
    public void Projectile_With_Bounce_ReflectsOff_Wall()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        int sy = world.SpawnTileY;
        int sx = world.SpawnTileX;
        int wallCol = sx + 24;

        // 清空墙左侧空域（测试区横带）
        for (int y = sy - 10; y < sy + 1; y++)
            for (int x = sx - 2; x < wallCol; x++)
            {
                ref var t = ref world.Tiles[x, y];
                t.Active = false; t.Type = 0;
            }
        // 竖起一面实心墙（从地表延伸到空域带）
        for (int y = sy - 12; y < sy + 20; y++)
        {
            ref var t = ref world.Tiles[wallCol, y];
            t.Active = true; t.Type = 0;
        }

        var proj = new ProjectileEntity
        {
            Key = -1, Owner = -1, Type = 81,
            Position = new Vector2((wallCol - 4) * 16f, (sy - 4) * 16f), // 墙左侧，朝右飞
            Velocity = new Vector2(6f, 0f),
            Damage = 15, TimeLeft = 180, Active = true,
        };
        lock (world.ProjectilesLock) world.Projectiles.Add(proj);

        for (int i = 0; i < 25; i++) sim.Tick(); // 让弹幕飞抵墙并反弹

        lock (world.ProjectilesLock)
        {
            Assert.True(proj.Active, "反弹弹幕应反射而非失效");
            Assert.True(proj.Velocity.X < 0f, $"撞墙后未反弹：Vx={proj.Velocity.X:F2}（应为负）");
            Assert.True(proj.BouncesLeft >= 0 && proj.BouncesLeft <= 20, $"反弹预算异常：{proj.BouncesLeft}");
        }
    }

    /// <summary>
    /// 弹幕碰撞盒按原版逐类型尺寸（ProjectileCapabilityTable.Sizes）：spawn 时从表取 width/height
    /// （如 type 20 = 4×4、type 101 = 6×6），未登记类型沿用 16×16 近似。
    /// </summary>
    [Fact]
    public void SpawnProjectile_Uses_PerType_HitBoxSize()
    {
        var world = new WorldState();
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true };
        var rng = new XoshiroRng(1);

        Assert.True(new SpawnProjectileCommand(1, 1, 1, 20, new Vector2(0, 0), new Vector2(0, 0), 10)
            .Apply(world, rng).Applied);
        Assert.True(new SpawnProjectileCommand(2, 1, 2, 101, new Vector2(0, 0), new Vector2(0, 0), 10)
            .Apply(world, rng).Applied);
        Assert.True(new SpawnProjectileCommand(3, 1, 3, 3999, new Vector2(0, 0), new Vector2(0, 0), 10)
            .Apply(world, rng).Applied);

        lock (world.ProjectilesLock)
        {
            var p20 = world.Projectiles.First(p => p.Key == 1);
            var p101 = world.Projectiles.First(p => p.Key == 2);
            var pFallback = world.Projectiles.First(p => p.Key == 3);
            Assert.Equal(4f, p20.Width);  Assert.Equal(4f, p20.Height);
            Assert.Equal(6f, p101.Width); Assert.Equal(6f, p101.Height);
            Assert.Equal(16f, pFallback.Width); Assert.Equal(16f, pFallback.Height);
        }
    }

    /// <summary>
    /// ① Boss 掉落改为原版数据库（眼魔样板）：经典腐化世界必掉 魔矿(56)30-90 / 邪箭(47)20-50 / 腐化种子(59)1-3，
    /// 并出现概率物品（眼面具 2112 = 1/7、望远镜 1299 = 1/40）可能为空；不进 3319 宝袋。
    /// </summary>
    [Fact]
    public void BossLoot_EyeOfCthulhu_ClassicCorrupt_DropsVanilla()
    {
        var world = new WorldState(); // 默认经典、腐化
        var rng = new XoshiroRng(9001);
        world.NotifyNpcKilled(4, 100, 100, rng);

        lock (world.ItemsLock)
        {
            Assert.Contains(world.Items, it => it.ItemId == 56 && it.Stack >= 30 && it.Stack <= 90);   // 魔矿必掉
            Assert.Contains(world.Items, it => it.ItemId == 47 && it.Stack >= 20 && it.Stack <= 50);   // 邪箭必掉
            Assert.Contains(world.Items, it => it.ItemId == 59 && it.Stack >= 1 && it.Stack <= 3);     // 腐化种子必掉
            Assert.DoesNotContain(world.Items, it => it.ItemId == 3319);                              // 经典无宝袋
            Assert.DoesNotContain(world.Items, it => it.ItemId == 880);                               // 腐化世界不掉猩红矿
        }
    }

    /// <summary>眼魔经典猩红世界：掉猩红矿(880)30-90 + 猩红种子(2171)1-3，不掉魔矿(56) / 腐化种子(59)。</summary>
    [Fact]
    public void BossLoot_EyeOfCthulhu_ClassicCrimson_DropsCrimtane()
    {
        var world = new WorldState();
        world.Progress.Crimson = true;
        var rng = new XoshiroRng(3005);
        world.NotifyNpcKilled(4, 100, 100, rng);

        lock (world.ItemsLock)
        {
            Assert.Contains(world.Items, it => it.ItemId == 880 && it.Stack >= 30 && it.Stack <= 90);
            Assert.Contains(world.Items, it => it.ItemId == 2171 && it.Stack >= 1 && it.Stack <= 3);
            Assert.DoesNotContain(world.Items, it => it.ItemId == 56);
            Assert.DoesNotContain(world.Items, it => it.ItemId == 59);
        }
    }

    /// <summary>眼魔专家模式（GameMode=1）：只掉宝袋(3319)，且不再直接掉矿 / 种子（原版 NotExpert 条件）。</summary>
    [Fact]
    public void BossLoot_EyeOfCthulhu_Expert_DropsTreasureBag()
    {
        var world = new WorldState { GameMode = 1 }; // 专家
        var rng = new XoshiroRng(7001);
        world.NotifyNpcKilled(4, 100, 100, rng);

        lock (world.ItemsLock)
        {
            Assert.Contains(world.Items, it => it.ItemId == 3319);                                    // 宝袋必掉
            Assert.DoesNotContain(world.Items, it => it.ItemId == 56);                                // 矿在袋内，不直接掉
            Assert.DoesNotContain(world.Items, it => it.ItemId == 59);
        }
    }

    // ========================================================================
    // 二十、原版刷怪规则（生物群系 / 昼夜细分 / 事件触发）
    // ========================================================================

    /// <summary>负 netID → 基础类型：原版 <c>NPCID.FromNetId</c>（NetIdMap 65 项）。</summary>
    [Theory]
    [InlineData(-1, 81)]    // Slimeling
    [InlineData(-3, 1)]     // Green Slime
    [InlineData(-4, 1)]     // Pinky
    [InlineData(-7, 1)]     // Purple Slime
    [InlineData(-10, 1)]    // Jungle Slime
    [InlineData(-13, 31)]   // Short Bones
    [InlineData(-26, 3)]    // Small Zombie
    [InlineData(-27, 3)]    // Big Zombie
    [InlineData(-43, 2)]    // Demon Eye 2
    [InlineData(-46, 21)]   // Small Skeleton
    [InlineData(-56, 231)]  // Little Hornet Fatty
    [InlineData(-65, 235)]  // Big Hornet Stingy
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4558, 4558)]
    public void NetIdMap_Resolves_BaseType(int netId, int expected)
        => Assert.Equal(expected, NpcNetIdMap.FromNetId(netId));

    /// <summary>
    /// 挖砖掉落（原版 <c>WorldGen.KillTile_DropItems</c> → <c>Item.NewItem</c>）：被挖掉的图格按
    /// <see cref="TileDropTable"/> 生成掉落物。真机反馈：挖掉树木 / 地块后没有掉落木块与泥土。
    /// </summary>
    [Theory]
    [InlineData(0, 2)]    // 泥土 → 泥土块
    [InlineData(2, 2)]    // 草地 → 泥土块
    [InlineData(1, 3)]    // 石头 → 石块
    [InlineData(5, 9)]    // 树干 → 木材
    [InlineData(30, 9)]   // 木板 → 木材
    [InlineData(53, 169)] // 沙 → 沙块
    public void TileBreakCommand_Drops_TileItem(int tileType, int expectedItem)
    {
        var world = NewTileWorld(tileType, out int tx, out int ty);

        var result = new TileBreakCommand(1, 1, tx, ty, 0, 0).Apply(world, new XoshiroRng(7));

        Assert.True(result.Applied);
        Assert.False(world.Tiles[tx, ty].Active);
        lock (world.ItemsLock)
            Assert.Contains(world.Items, i => i.ItemId == expectedItem && !i.NewNotified);   // 待补发包 21/22
    }

    /// <summary>动作 4（KillTileNoItem）与 fail 标志（第 5 字段非 0 = 仅命中）都不得生成掉落物。</summary>
    [Fact]
    public void TileBreakCommand_NoItem_And_HitOnly_DoNotDrop()
    {
        var noItem = NewTileWorld(0, out int nx, out int ny);
        new TileBreakCommand(1, 1, nx, ny, 4, 0).Apply(noItem, new XoshiroRng(7));
        Assert.False(noItem.Tiles[nx, ny].Active);
        Assert.Empty(noItem.Items);

        var hitOnly = NewTileWorld(0, out int hx, out int hy);
        new TileBreakCommand(1, 1, hx, hy, 0, 1).Apply(hitOnly, new XoshiroRng(7));
        Assert.True(hitOnly.Tiles[hx, hy].Active, "fail=1（仅命中）不应改动世界");
        Assert.Empty(hitOnly.Items);
    }

    [Fact]
    public void TileBreakCommand_BreaksChestObjectOnce_FromAnyPart()
    {
        var world = NewContainerWorld(21, style: 0, width: 2, out int anchorX, out int anchorY);
        var chest = new Chest { Index = 0, X = anchorX, Y = anchorY, Items = Array.Empty<ChestItem>() };
        world.Chests.Add(chest);
        world.OpenChestSession(1, 0);

        var result = new TileBreakCommand(1, 1, anchorX + 1, anchorY + 1, 0, 0).Apply(world, new XoshiroRng(7));

        Assert.True(result.Applied);
        for (int x = anchorX; x < anchorX + 2; x++)
        for (int y = anchorY; y < anchorY + 2; y++)
            Assert.False(world.Tiles[x, y].Active);
        lock (world.ItemsLock)
        {
            var drop = Assert.Single(world.Items);
            Assert.Equal(48, drop.ItemId);
            Assert.Equal(1, drop.Stack);
        }
        Assert.Null(world.FindChestAt(anchorX, anchorY));
        Assert.Null(world.FindChestByIndex(0));
        Assert.False(world.HasChestSession(1, 0));
        Assert.Equal(new[] { 0 }, world.DrainDeletedPersistChests(10));
        Assert.Equal(4, world.DrainTileUpdates(10).Count);
    }

    [Fact]
    public void TileBreakCommand_UsesSecondChestAndDresserStyleDrops()
    {
        var secondChest = NewContainerWorld(467, style: 2, width: 2, out int chestX, out int chestY);
        new TileBreakCommand(1, 1, chestX, chestY, 0, 0).Apply(secondChest, new XoshiroRng(7));
        lock (secondChest.ItemsLock)
            Assert.Equal(3939, Assert.Single(secondChest.Items).ItemId);

        var dresser = NewContainerWorld(88, style: 4, width: 3, out int dresserX, out int dresserY);
        new TileBreakCommand(1, 1, dresserX + 2, dresserY + 1, 0, 0).Apply(dresser, new XoshiroRng(7));
        for (int x = dresserX; x < dresserX + 3; x++)
        for (int y = dresserY; y < dresserY + 2; y++)
            Assert.False(dresser.Tiles[x, y].Active);
        lock (dresser.ItemsLock)
            Assert.Equal(918, Assert.Single(dresser.Items).ItemId);
    }

    [Fact]
    public void TileBreakCommand_BreaksContainerWithoutWorldChestRecord()
    {
        var world = NewContainerWorld(21, style: 0, width: 2, out int anchorX, out int anchorY);

        var result = new TileBreakCommand(1, 1, anchorX, anchorY, 0, 0).Apply(world, new XoshiroRng(7));

        Assert.True(result.Applied);
        for (int x = anchorX; x < anchorX + 2; x++)
        for (int y = anchorY; y < anchorY + 2; y++)
            Assert.False(world.Tiles[x, y].Active);
        lock (world.ItemsLock)
            Assert.Equal(48, Assert.Single(world.Items).ItemId);
        Assert.Empty(world.DrainDeletedPersistChests(10));
    }

    [Fact]
    public void TileBreakCommand_RejectsNonEmptyOrIncompleteContainerObject()
    {
        var nonEmpty = NewContainerWorld(21, style: 0, width: 2, out int anchorX, out int anchorY);
        nonEmpty.Chests.Add(new Chest
        {
            Index = 0,
            X = anchorX,
            Y = anchorY,
            Items = new[] { new ChestItem { Type = 1, Stack = 1 } },
        });
        nonEmpty.OpenChestSession(1, 0);

        var rejected = new TileBreakCommand(1, 1, anchorX, anchorY, 0, 0).Apply(nonEmpty, new XoshiroRng(7));

        Assert.False(rejected.Applied);
        Assert.All(Enumerable.Range(anchorX, 2), x =>
            Assert.All(Enumerable.Range(anchorY, 2), y => Assert.True(nonEmpty.Tiles[x, y].Active)));
        Assert.Empty(nonEmpty.Items);
        Assert.NotNull(nonEmpty.FindChestByIndex(0));
        Assert.True(nonEmpty.HasChestSession(1, 0));
        Assert.Empty(nonEmpty.DrainDeletedPersistChests(10));

        var incomplete = NewContainerWorld(88, style: 0, width: 3, out int dresserX, out int dresserY);
        incomplete.Tiles[dresserX + 2, dresserY + 1].Active = false;
        var incompleteResult = new TileBreakCommand(1, 1, dresserX, dresserY, 0, 0).Apply(incomplete, new XoshiroRng(7));
        Assert.False(incompleteResult.Applied);
        Assert.True(incomplete.Tiles[dresserX, dresserY].Active);
        Assert.Empty(incomplete.Items);
    }

    private static WorldState NewContainerWorld(int type, int style, int width, out int anchorX, out int anchorY)
    {
        var world = new WorldState { MaxTilesX = 10, MaxTilesY = 10, Tiles = new TileMap(10, 10) };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(8, 8) };

        anchorX = 3;
        anchorY = 3;
        int styleFrameWidth = width * 18;
        for (int localX = 0; localX < width; localX++)
        for (int localY = 0; localY < 2; localY++)
        {
            world.Tiles[anchorX + localX, anchorY + localY] = new Tile
            {
                Active = true,
                Type = (ushort)type,
                FrameX = (short)(style * styleFrameWidth + localX * 18),
                FrameY = (short)(localY * 18),
            };
        }
        return world;
    }

    [Theory]
    [InlineData(406, 3, 3, 3365)]
    [InlineData(102, 3, 4, 355)]
    [InlineData(106, 3, 2, 363)]
    public void TileBreakCommand_BreaksStatelessMultiTileObjectOnce_FromAnyPart(
        int type, int width, int height, int expectedItem)
    {
        var world = NewStatelessMultiTileWorld(type, width, height, styleX: 2, styleY: 1,
            out int anchorX, out int anchorY);

        var result = new TileBreakCommand(1, 1, anchorX + width - 1, anchorY + height - 1, 0, 0)
            .Apply(world, new XoshiroRng(7));

        Assert.True(result.Applied);
        for (int x = anchorX; x < anchorX + width; x++)
        for (int y = anchorY; y < anchorY + height; y++)
            Assert.False(world.Tiles[x, y].Active);
        lock (world.ItemsLock)
        {
            var drop = Assert.Single(world.Items);
            Assert.Equal(expectedItem, drop.ItemId);
            Assert.Equal(1, drop.Stack);
        }
        Assert.Equal(width * height, world.DrainTileUpdates(100).Count);
    }

    [Theory]
    [InlineData(406, 3, 3)]
    [InlineData(102, 3, 4)]
    [InlineData(106, 3, 2)]
    public void TileBreakCommand_RejectsIncompleteStatelessMultiTileObject(int type, int width, int height)
    {
        var world = NewStatelessMultiTileWorld(type, width, height, styleX: 0, styleY: 0,
            out int anchorX, out int anchorY);
        world.Tiles[anchorX + width - 1, anchorY + height - 1].Active = false;

        var result = new TileBreakCommand(1, 1, anchorX + 1, anchorY, 0, 0)
            .Apply(world, new XoshiroRng(7));

        Assert.False(result.Applied);
        Assert.True(world.Tiles[anchorX + 1, anchorY].Active);
        Assert.Empty(world.Items);
    }

    private static WorldState NewStatelessMultiTileWorld(int type, int width, int height, int styleX, int styleY,
        out int anchorX, out int anchorY)
    {
        var world = new WorldState { MaxTilesX = 12, MaxTilesY = 12, Tiles = new TileMap(12, 12) };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(8, 8) };

        anchorX = 3;
        anchorY = 3;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            world.Tiles[anchorX + x, anchorY + y] = new Tile
            {
                Active = true,
                Type = (ushort)type,
                FrameX = (short)(styleX * width * 18 + x * 18),
                FrameY = (short)(styleY * height * 18 + y * 18),
            };
        }
        return world;
    }

    [Theory]
    [InlineData(395, 2, 2, 1, 3270)]
    [InlineData(471, 3, 3, 4, 2699)]
    [InlineData(520, 1, 1, 6, 4326)]
    [InlineData(698, 1, 2, 8, 5472)]
    public void TileBreakCommand_DisplayItemEntity_ReturnsPayloadThenBreaksShell(
        int tileType, int width, int height, byte entityType, int shellItem)
    {
        var world = NewDisplayTileEntityWorld(tileType, width, height, entityType, out int anchorX, out int anchorY,
            out var entity);
        entity.Item = new TileEntityItem { Type = 123, Prefix = 17, Stack = 4 };
        world.DrainDirtyTileEntities(10);

        var first = new TileBreakCommand(1, 1, anchorX + width - 1, anchorY + height - 1, 0, 0)
            .Apply(world, new XoshiroRng(7));

        Assert.True(first.Applied);
        Assert.All(Enumerable.Range(anchorX, width), x =>
            Assert.All(Enumerable.Range(anchorY, height), y => Assert.True(world.Tiles[x, y].Active)));
        Assert.True(entity.Item.IsAir);
        Assert.Equal(new[] { entity.Id }, world.DrainDirtyTileEntities(10));
        lock (world.ItemsLock)
        {
            var payload = Assert.Single(world.Items);
            Assert.Equal(123, payload.ItemId);
            Assert.Equal(4, payload.Stack);
            Assert.Equal(17, payload.Prefix);
        }

        var second = new TileBreakCommand(2, 1, anchorX + width - 1, anchorY + height - 1, 0, 0)
            .Apply(world, new XoshiroRng(7));

        Assert.True(second.Applied);
        Assert.All(Enumerable.Range(anchorX, width), x =>
            Assert.All(Enumerable.Range(anchorY, height), y => Assert.False(world.Tiles[x, y].Active)));
        lock (world.ItemsLock)
        {
            Assert.Equal(2, world.Items.Count);
            Assert.Contains(world.Items, item => item.ItemId == shellItem && item.Stack == 1);
        }
        Assert.False(world.TryGetTileEntity(entity.Id, out _));
        Assert.Equal(new[] { entity.Id }, world.DrainDeletedTileEntities(10).Select(static deletion => deletion.RuntimeId));
        Assert.Equal(width * height, world.DrainTileUpdates(100).Count);
    }

    [Theory]
    [InlineData(470, 2, 3, 3, 498)]
    [InlineData(475, 3, 4, 5, 3977)]
    public void TileBreakCommand_DisplayDollAndHatRack_ReturnAllPayloadThenBreakWhenEmpty(
        int tileType, int width, int height, byte entityType, int shellItem)
    {
        var world = NewDisplayTileEntityWorld(tileType, width, height, entityType, out int anchorX, out int anchorY,
            out var entity);
        if (tileType == 470)
        {
            entity.DisplayDollItems[0] = new TileEntityItem { Type = 121, Prefix = 3, Stack = 2 };
            entity.DisplayDollDyes[8] = new TileEntityItem { Type = 122, Prefix = 4, Stack = 3 };
            entity.DisplayDollMisc = new TileEntityItem { Type = 123, Prefix = 5, Stack = 4 };
        }
        else
        {
            entity.HatRackHats[0] = new TileEntityItem { Type = 121, Prefix = 3, Stack = 2 };
            entity.HatRackHats[1] = new TileEntityItem { Type = 122, Prefix = 4, Stack = 3 };
            entity.HatRackDyes[0] = new TileEntityItem { Type = 123, Prefix = 5, Stack = 4 };
        }
        world.DrainDirtyTileEntities(10);

        var first = new TileBreakCommand(1, 1, anchorX + width - 1, anchorY + height - 1, 0, 0)
            .Apply(world, new XoshiroRng(7));

        Assert.True(first.Applied);
        Assert.All(Enumerable.Range(anchorX, width), x =>
            Assert.All(Enumerable.Range(anchorY, height), y => Assert.True(world.Tiles[x, y].Active)));
        Assert.True(world.TryGetTileEntity(entity.Id, out _));
        Assert.Equal(new[] { entity.Id }, world.DrainDirtyTileEntities(10));
        lock (world.ItemsLock)
        {
            Assert.Equal(3, world.Items.Count);
            Assert.Contains(world.Items, item => item.ItemId == 121 && item.Stack == 2 && item.Prefix == 3);
            Assert.Contains(world.Items, item => item.ItemId == 122 && item.Stack == 3 && item.Prefix == 4);
            Assert.Contains(world.Items, item => item.ItemId == 123 && item.Stack == 4 && item.Prefix == 5);
        }

        var broken = new TileBreakCommand(2, 1, anchorX + width - 1, anchorY + height - 1, 0, 0)
            .Apply(world, new XoshiroRng(7));

        Assert.True(broken.Applied);
        Assert.All(Enumerable.Range(anchorX, width), x =>
            Assert.All(Enumerable.Range(anchorY, height), y => Assert.False(world.Tiles[x, y].Active)));
        lock (world.ItemsLock)
            Assert.Contains(world.Items, item => item.ItemId == shellItem && item.Stack == 1);
        Assert.Equal(new[] { entity.Id }, world.DrainDeletedTileEntities(10).Select(static deletion => deletion.RuntimeId));
    }

    [Theory]
    [InlineData(395, 2, 2)]
    [InlineData(471, 3, 3)]
    [InlineData(520, 1, 1)]
    [InlineData(698, 1, 2)]
    [InlineData(470, 2, 3)]
    [InlineData(475, 3, 4)]
    public void TileBreakCommand_DisplayTileEntityWithoutRecord_IsRejected(int tileType, int width, int height)
    {
        var world = NewDisplayTileEntityWorld(tileType, width, height, entityType: 0, out int anchorX, out int anchorY,
            out _);

        var result = new TileBreakCommand(1, 1, anchorX + width - 1, anchorY + height - 1, 0, 0)
            .Apply(world, new XoshiroRng(7));

        Assert.False(result.Applied);
        Assert.All(Enumerable.Range(anchorX, width), x =>
            Assert.All(Enumerable.Range(anchorY, height), y => Assert.True(world.Tiles[x, y].Active)));
        Assert.Empty(world.Items);
    }

    [Theory]
    [InlineData(395, 2, 2, 1)]
    [InlineData(471, 3, 3, 4)]
    [InlineData(520, 1, 1, 6)]
    [InlineData(698, 1, 2, 8)]
    [InlineData(470, 2, 3, 3)]
    [InlineData(475, 3, 4, 5)]
    public void TileBreakCommand_DisplayTileEntityWithWrongTypeOrIncompleteFrame_IsRejected(
        int tileType, int width, int height, byte entityType)
    {
        var wrongTypeWorld = NewDisplayTileEntityWorld(tileType, width, height, entityType, out int anchorX,
            out int anchorY, out var wrongTypeEntity);
        wrongTypeEntity.Type = 0;
        var wrongType = new TileBreakCommand(1, 1, anchorX + width - 1, anchorY + height - 1, 0, 0)
            .Apply(wrongTypeWorld, new XoshiroRng(7));
        Assert.False(wrongType.Applied);
        Assert.True(wrongTypeWorld.Tiles[anchorX + width - 1, anchorY + height - 1].Active);
        Assert.Empty(wrongTypeWorld.Items);

        var incompleteWorld = NewDisplayTileEntityWorld(tileType, width, height, entityType, out anchorX,
            out anchorY, out _);
        incompleteWorld.Tiles[anchorX + width - 1, anchorY + height - 1].FrameX++;
        var incomplete = new TileBreakCommand(1, 1, anchorX, anchorY, 0, 0)
            .Apply(incompleteWorld, new XoshiroRng(7));
        Assert.False(incomplete.Applied);
        Assert.True(incompleteWorld.Tiles[anchorX, anchorY].Active);
        Assert.Empty(incompleteWorld.Items);
    }

    [Fact]
    public void TileBreakCommand_DisplayDoll_UsesVanillaFemaleStyleShellDrop()
    {
        var world = NewDisplayTileEntityWorld(470, 2, 3, 3, out int anchorX, out int anchorY, out _);
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 3; y++)
            world.Tiles[anchorX + x, anchorY + y].FrameX += 72;

        var result = new TileBreakCommand(1, 1, anchorX + 1, anchorY + 2, 0, 0)
            .Apply(world, new XoshiroRng(7));

        Assert.True(result.Applied);
        lock (world.ItemsLock)
            Assert.Equal(1989, Assert.Single(world.Items).ItemId);
    }

    private static WorldState NewDisplayTileEntityWorld(int tileType, int width, int height, byte entityType,
        out int anchorX, out int anchorY, out TileEntity entity)
    {
        var world = new WorldState { MaxTilesX = 12, MaxTilesY = 12, Tiles = new TileMap(12, 12) };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(8, 8) };

        anchorX = 3;
        anchorY = 3;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            world.Tiles[anchorX + x, anchorY + y] = new Tile
            {
                Active = true,
                Type = (ushort)tileType,
                FrameX = (short)(x * 18),
                FrameY = (short)(y * 18),
            };
        }

        entity = new TileEntity { Type = entityType, X = (short)anchorX, Y = (short)anchorY };
        if (entityType != 0) world.InsertTileEntity(entity);
        return world;
    }

    [Theory]
    [InlineData(423, 1, 1, 2, 0, 0, 0, 0, 3613)]
    [InlineData(423, 1, 1, 2, 0, 0, 0, 6, 3729)]
    [InlineData(378, 2, 3, 0, 1, 2, 0, 0, 3202)]
    [InlineData(597, 3, 4, 7, 2, 3, 0, 0, 4876)]
    [InlineData(597, 3, 4, 7, 2, 3, 10, 0, 5653)]
    public void TileBreakCommand_RuntimeTileEntityShell_BreaksValidatedFootprint(
        int tileType, int width, int height, byte entityType, int hitX, int hitY, int styleX, int styleY,
        int expectedItem)
    {
        var world = NewRuntimeTileEntityWorld(tileType, width, height, entityType, styleX, styleY,
            out int anchorX, out int anchorY, out var entity);

        var result = new TileBreakCommand(1, 1, anchorX + hitX, anchorY + hitY, 0, 0)
            .Apply(world, new XoshiroRng(7));

        Assert.True(result.Applied);
        Assert.All(Enumerable.Range(anchorX, width), x =>
            Assert.All(Enumerable.Range(anchorY, height), y => Assert.False(world.Tiles[x, y].Active)));
        lock (world.ItemsLock)
        {
            var drop = Assert.Single(world.Items);
            Assert.Equal(expectedItem, drop.ItemId);
            Assert.Equal(1, drop.Stack);
        }
        Assert.False(world.TryGetTileEntity(entity.Id, out _));
        Assert.Equal(new[] { entity.Id }, world.DrainDeletedTileEntities(10).Select(static deletion => deletion.RuntimeId));
        Assert.Equal(width * height, world.DrainTileUpdates(100).Count);
    }

    [Theory]
    [InlineData(723, 9)]
    [InlineData(724, 10)]
    public void TileBreakCommand_LeashedAnchor_ReturnsTypeOnlyPayloadWithoutShell(int tileType, byte entityType)
    {
        var world = NewRuntimeTileEntityWorld(tileType, 1, 1, entityType, 0, 0,
            out int anchorX, out int anchorY, out var entity);
        entity.Item = new TileEntityItem { Type = 123, Stack = 4, Prefix = 17 };

        var result = new TileBreakCommand(1, 1, anchorX, anchorY, 0, 0).Apply(world, new XoshiroRng(7));

        Assert.True(result.Applied);
        Assert.False(world.Tiles[anchorX, anchorY].Active);
        lock (world.ItemsLock)
        {
            var drop = Assert.Single(world.Items);
            Assert.Equal(123, drop.ItemId);
            Assert.Equal(1, drop.Stack);
            Assert.Equal(0, drop.Prefix);
        }
        Assert.False(world.TryGetTileEntity(entity.Id, out _));
        Assert.Equal(new[] { entity.Id }, world.DrainDeletedTileEntities(10).Select(static deletion => deletion.RuntimeId));
        Assert.Single(world.DrainTileUpdates(10));
    }

    [Theory]
    [InlineData(423, 1, 1, 2)]
    [InlineData(378, 2, 3, 0)]
    [InlineData(597, 3, 4, 7)]
    [InlineData(723, 1, 1, 9)]
    [InlineData(724, 1, 1, 10)]
    public void TileBreakCommand_RuntimeTileEntityWithoutRecord_IsRejected(int tileType, int width, int height,
        byte entityType)
    {
        var world = NewRuntimeTileEntityWorld(tileType, width, height, entityType, 0, 0,
            out int anchorX, out int anchorY, out var entity);
        world.RemoveTileEntity(entity.Id);

        var result = new TileBreakCommand(1, 1, anchorX + width - 1, anchorY + height - 1, 0, 0)
            .Apply(world, new XoshiroRng(7));

        Assert.False(result.Applied);
        Assert.All(Enumerable.Range(anchorX, width), x =>
            Assert.All(Enumerable.Range(anchorY, height), y => Assert.True(world.Tiles[x, y].Active)));
        Assert.Empty(world.Items);
    }

    [Theory]
    [InlineData(423, 1, 1, 2)]
    [InlineData(378, 2, 3, 0)]
    [InlineData(597, 3, 4, 7)]
    [InlineData(723, 1, 1, 9)]
    [InlineData(724, 1, 1, 10)]
    public void TileBreakCommand_RuntimeTileEntityWithWrongTypeOrIncompleteFootprint_IsRejected(
        int tileType, int width, int height, byte entityType)
    {
        var wrongTypeWorld = NewRuntimeTileEntityWorld(tileType, width, height, entityType, 0, 0,
            out int anchorX, out int anchorY, out var wrongTypeEntity);
        wrongTypeEntity.Type = (byte)(entityType == 0 ? 1 : 0);
        var wrongType = new TileBreakCommand(1, 1, anchorX + width - 1, anchorY + height - 1, 0, 0)
            .Apply(wrongTypeWorld, new XoshiroRng(7));
        Assert.False(wrongType.Applied);
        Assert.True(wrongTypeWorld.Tiles[anchorX + width - 1, anchorY + height - 1].Active);
        Assert.Empty(wrongTypeWorld.Items);

        var incompleteWorld = NewRuntimeTileEntityWorld(tileType, width, height, entityType, 0, 0,
            out anchorX, out anchorY, out _);
        incompleteWorld.Tiles[anchorX + width - 1, anchorY + height - 1].FrameX++;
        var incomplete = new TileBreakCommand(1, 1, anchorX, anchorY, 0, 0)
            .Apply(incompleteWorld, new XoshiroRng(7));
        Assert.False(incomplete.Applied);
        Assert.True(incompleteWorld.Tiles[anchorX, anchorY].Active);
        Assert.Empty(incompleteWorld.Items);
    }

    private static WorldState NewRuntimeTileEntityWorld(int tileType, int width, int height, byte entityType,
        int styleX, int styleY, out int anchorX, out int anchorY, out TileEntity entity)
    {
        var world = new WorldState { MaxTilesX = 12, MaxTilesY = 12, Tiles = new TileMap(12, 12) };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(8, 8) };

        anchorX = 3;
        anchorY = 3;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            world.Tiles[anchorX + x, anchorY + y] = new Tile
            {
                Active = true,
                Type = (ushort)tileType,
                FrameX = (short)((styleX * width + x) * 18),
                FrameY = (short)((styleY * height + y) * 18),
            };
        }

        entity = world.InsertTileEntity(new TileEntity { Type = entityType, X = (short)anchorX, Y = (short)anchorY });
        world.DrainDirtyTileEntities(10);
        return world;
    }

    /// <summary>构造成 8×8 世界：玩家在 (1,1)、(3,3) 为指定类型的实心图格。</summary>
    private static WorldState NewTileWorld(int tileType, out int tx, out int ty)
    {
        var world = new WorldState { MaxTilesX = 8, MaxTilesY = 8, Tiles = new TileMap(8, 8) };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(8, 8) };

        tx = 3;
        ty = 3;
        world.Tiles[tx, ty] = new Tile { Active = true, Type = (ushort)tileType };
        return world;
    }

    /// <summary>
    /// 砍树整棵倒下：砍掉任意一格树干 → 连通树干（含枝条）全部清除，木材数量 = 清掉的树干格数。
    /// 真机反馈：挖掉一棵树只给 1 个木块。
    /// </summary>
    [Fact]
    public void TileBreakCommand_Fells_Whole_Tree_And_Drops_Wood_Per_Tile()
    {
        const int bx = 8, baseY = 10, height = 6;
        var world = new WorldState { MaxTilesX = 16, MaxTilesY = 16, Tiles = new TileMap(16, 16) };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(8, 8) };

        for (int y = baseY; y > baseY - height; y--)                              // 树干
            world.Tiles[bx, y] = new Tile { Active = true, Type = 5 };
        world.Tiles[bx - 1, baseY - 2] = new Tile { Active = true, Type = 5 };     // 枝条（4 邻接）
        world.Tiles[bx, baseY + 1] = new Tile { Active = true, Type = 0 };         // 树下泥土：不得被清掉

        new TileBreakCommand(1, 1, bx, baseY, 0, 0).Apply(world, new FixedRng(34, 2));

        for (int y = baseY; y > baseY - height; y--)
            Assert.False(world.Tiles[bx, y].Active, $"树干 ({bx},{y}) 未随整棵倒下清除");
        Assert.False(world.Tiles[bx - 1, baseY - 2].Active, "枝条未随整棵倒下清除");
        Assert.True(world.Tiles[bx, baseY + 1].Active, "树下地面不应被清除");

        lock (world.ItemsLock)
        {
            var wood = Assert.Single(world.Items);
            Assert.Equal(9, wood.ItemId);                     // 木材
            Assert.Equal(height + 1, wood.Stack);             // 6 格树干 + 1 格枝
        }
    }

    [Fact]
    public void TileBreakCommand_Marks_All_Stateless_MultiTile_Cells_For_Persistence()
    {
        var world = new WorldState { MaxTilesX = 16, MaxTilesY = 16, Tiles = new TileMap(16, 16) };
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, Position = new Vector2(8, 8) };

        const int anchorX = 4;
        const int anchorY = 4;
        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
            world.Tiles[anchorX + x, anchorY + y] = new Tile
            {
                Active = true,
                Type = 406,
                FrameX = (short)(x * 18),
                FrameY = (short)(y * 18),
            };

        var result = new TileBreakCommand(1, 1, anchorX, anchorY, 0, 0).Apply(world, new FixedRng());

        Assert.True(result.Applied);
        var persisted = world.DrainPersistTiles(WorldState.PersistBatchSize);
        Assert.Equal(9, persisted.Count);
        Assert.All(persisted, coordinate =>
            Assert.InRange(coordinate.X, anchorX, anchorX + 2));
        Assert.All(persisted, coordinate =>
            Assert.InRange(coordinate.Y, anchorY, anchorY + 2));
    }

    [Fact]
    public void TileBreakCommand_Drops_Mature_And_Flowering_Herbs()
    {
        var mature = NewTileWorld(83, out int matureX, out int matureY);
        mature.Tiles[matureX, matureY].FrameX = 18 * 6;

        Assert.True(new TileBreakCommand(1, 1, matureX, matureY, 0, 0).Apply(mature, new FixedRng()).Applied);
        var matureDrop = Assert.Single(mature.Items);
        Assert.Equal(2358, matureDrop.ItemId);
        Assert.Equal(1, matureDrop.Stack);

        var flowering = NewTileWorld(84, out int floweringX, out int floweringY);
        flowering.Tiles[floweringX, floweringY].FrameX = 18 * 6;

        Assert.True(new TileBreakCommand(1, 1, floweringX, floweringY, 0, 0).Apply(flowering, new FixedRng(0, 0, 2)).Applied);
        Assert.Collection(flowering.Items.OrderBy(i => i.ItemId),
            herb => { Assert.Equal(2357, herb.ItemId); Assert.Equal(3, herb.Stack); },
            seed => { Assert.Equal(2358, seed.ItemId); Assert.Equal(1, seed.Stack); });
    }

    [Theory]
    [InlineData(102, 0, 0, 355)]
    [InlineData(106, 0, 0, 363)]
    [InlineData(212, 0, 0, 951)]
    [InlineData(219, 0, 0, 997)]
    [InlineData(220, 0, 0, 998)]
    [InlineData(228, 0, 0, 1120)]
    [InlineData(243, 0, 0, 1430)]
    [InlineData(247, 0, 0, 1551)]
    [InlineData(283, 0, 0, 2172)]
    [InlineData(300, 0, 0, 2192)]
    [InlineData(303, 0, 0, 2195)]
    [InlineData(306, 0, 0, 2198)]
    [InlineData(307, 0, 0, 2203)]
    [InlineData(308, 0, 0, 2204)]
    [InlineData(354, 0, 0, 2999)]
    [InlineData(355, 0, 0, 3000)]
    [InlineData(487, 0, 0, 4064)]
    [InlineData(487, 72, 0, 4064)]
    [InlineData(487, 504, 0, 4064)]
    [InlineData(493, 0, 0, 4083)]
    [InlineData(493, 90, 0, 4088)]
    [InlineData(4, 0, 0, 8)]
    [InlineData(4, 0, 176, 523)]
    [InlineData(4, 0, 484, 5293)]
    [InlineData(4, 0, 506, 5353)]
    [InlineData(3, 144, 0, 5)]
    [InlineData(24, 144, 0, 60)]
    [InlineData(110, 144, 0, 5)]
    [InlineData(201, 270, 0, 2887)]
    [InlineData(14, 0, 0, 32)]
    [InlineData(14, 54, 0, 638)]
    [InlineData(14, 1836, 0, 3154)]
    [InlineData(14, 1890, 0, 32)]
    [InlineData(469, 0, 0, 3920)]
    [InlineData(469, 486, 0, 5165)]
    [InlineData(469, 1674, 0, 6128)]
    [InlineData(469, 1728, 0, 3920)]
    [InlineData(441, 0, 0, 3665)]
    [InlineData(441, 252, 0, 3668)]
    [InlineData(441, 1836, 0, 3704)]
    [InlineData(441, 1872, 0, 3665)]
    [InlineData(468, 0, 0, 3886)]
    [InlineData(468, 180, 0, 4164)]
    [InlineData(468, 1332, 0, 6131)]
    [InlineData(468, 1368, 0, 3886)]
    [InlineData(15, 0, 0, 34)]
    [InlineData(15, 0, 720, 1703)]
    [InlineData(15, 0, 2680, 6116)]
    [InlineData(15, 0, 2720, 34)]
    [InlineData(18, 0, 0, 36)]
    [InlineData(18, 648, 0, 2229)]
    [InlineData(18, 2304, 0, 6130)]
    [InlineData(18, 2340, 0, 36)]
    [InlineData(34, 0, 0, 106)]
    [InlineData(34, 108, 0, 3894)]
    [InlineData(34, 108, 1782, 6117)]
    [InlineData(34, 108, 1836, 106)]
    [InlineData(42, 0, 0, 136)]
    [InlineData(42, 0, 1188, 2820)]
    [InlineData(42, 0, 2520, 6123)]
    [InlineData(42, 0, 2556, 136)]
    [InlineData(79, 0, 0, 224)]
    [InlineData(79, 0, 972, 2811)]
    [InlineData(79, 0, 2304, 6112)]
    [InlineData(79, 0, 2340, 224)]
    [InlineData(87, 0, 0, 333)]
    [InlineData(87, 2106, 0, 4579)]
    [InlineData(87, 3456, 0, 6124)]
    [InlineData(87, 3510, 0, 333)]
    [InlineData(89, 0, 0, 335)]
    [InlineData(89, 2268, 0, 4582)]
    [InlineData(89, 3672, 0, 6127)]
    [InlineData(89, 3726, 0, 335)]
    [InlineData(90, 0, 0, 336)]
    [InlineData(90, 0, 1404, 4566)]
    [InlineData(90, 0, 2304, 6111)]
    [InlineData(90, 0, 2340, 336)]
    [InlineData(93, 0, 0, 342)]
    [InlineData(93, 0, 2106, 4577)]
    [InlineData(93, 0, 3456, 6122)]
    [InlineData(93, 0, 3510, 342)]
    [InlineData(100, 0, 0, 349)]
    [InlineData(100, 0, 1404, 4570)]
    [InlineData(100, 0, 2304, 6114)]
    [InlineData(100, 0, 2340, 349)]
    [InlineData(101, 0, 0, 354)]
    [InlineData(101, 2160, 0, 4568)]
    [InlineData(101, 3456, 0, 6113)]
    [InlineData(101, 3510, 0, 354)]
    [InlineData(104, 0, 0, 359)]
    [InlineData(104, 1440, 0, 4575)]
    [InlineData(104, 2340, 0, 6119)]
    [InlineData(104, 2376, 0, 359)]
    [InlineData(139, 0, 0, 562)]
    [InlineData(139, 0, 1656, 4237)]
    [InlineData(139, 0, 3600, 6146)]
    [InlineData(139, 0, 3636, 576)]
    [InlineData(172, 0, 0, 2827)]
    [InlineData(172, 0, 1520, 4581)]
    [InlineData(172, 0, 2470, 6126)]
    [InlineData(172, 0, 2508, 2827)]
    [InlineData(497, 0, 0, 4096)]
    [InlineData(497, 0, 1240, 4127)]
    [InlineData(497, 0, 1280, 4141)]
    [InlineData(497, 0, 1560, 4731)]
    [InlineData(497, 0, 2560, 6129)]
    [InlineData(497, 0, 2600, 4096)]
    [InlineData(33, 0, 0, 105)]
    [InlineData(33, 0, 22, 1405)]
    [InlineData(33, 0, 88, 2045)]
    [InlineData(33, 0, 286, 2054)]
    [InlineData(33, 0, 308, 2153)]
    [InlineData(33, 0, 352, 2155)]
    [InlineData(33, 0, 374, 2236)]
    [InlineData(33, 0, 1386, 6115)]
    [InlineData(33, 0, 1408, 105)]
    [InlineData(19, 0, 0, 94)]
    [InlineData(19, 0, 18, 631)]
    [InlineData(19, 0, 540, 3903)]
    [InlineData(19, 0, 630, 3908)]
    [InlineData(19, 0, 648, 3945)]
    [InlineData(19, 0, 1242, 6125)]
    [InlineData(19, 0, 1260, 94)]
    [InlineData(13, 0, 0, 31)]
    [InlineData(13, 18, 0, 28)]
    [InlineData(13, 144, 0, 2258)]
    [InlineData(13, 162, 0, 31)]
    [InlineData(227, 0, 0, 1107)]
    [InlineData(227, 238, 0, 1114)]
    [InlineData(227, 272, 0, 3385)]
    [InlineData(227, 374, 0, 3388)]
    [InlineData(227, 408, 0, 1119)]
    [InlineData(178, 0, 0, 181)]
    [InlineData(178, 108, 0, 999)]
    [InlineData(703, 0, 0, 195)]
    [InlineData(703, 108, 0, 208)]
    [InlineData(703, 126, 0, 208)]
    [InlineData(703, 144, 0, 331)]
    [InlineData(703, 162, 0, 223)]
    [InlineData(703, 180, 0, 195)]
    [InlineData(135, 0, 108, 1151)]
    [InlineData(137, 0, 90, 5135)]
    [InlineData(144, 72, 0, 4485)]
    [InlineData(239, 0, 0, 20)]
    [InlineData(239, 126, 0, 706)]
    [InlineData(239, 396, 0, 3467)]
    [InlineData(324, 0, 88, 4071)]
    [InlineData(380, 0, 36, 3217)]
    [InlineData(419, 36, 0, 3663)]
    [InlineData(420, 0, 90, 3608)]
    [InlineData(423, 0, 108, 3729)]
    [InlineData(428, 0, 54, 3626)]
    [InlineData(650, 504, 0, 9)]
    [InlineData(650, 1350, 0, 276)]
    [InlineData(50, 0, 0, 149)]
    [InlineData(50, 90, 0, 165)]
    [InlineData(707, 90, 0, 165)]
    [InlineData(129, 0, 0, 502)]
    [InlineData(129, 324, 0, 4988)]
    [InlineData(149, 0, 0, 596)]
    [InlineData(149, 18, 0, 597)]
    [InlineData(149, 36, 0, 598)]
    [InlineData(149, 54, 0, 596)]
    public void TileDropTable_Uses_Frames_For_StyleSpecific_Drops(
        int tileType, short frameX, short frameY, int expectedItem)
    {
        Assert.True(TileDropTable.TryGet(tileType, frameX, frameY, out int itemId, out int stack));
        Assert.Equal(expectedItem, itemId);
        Assert.Equal(1, stack);
    }

    [Fact]
    public void TileDropTable_Rejects_Unknown_Bathtub_Frame()
    {
        Assert.False(TileDropTable.TryGet(149, 108, 0, out _, out _));
    }

    [Theory]
    [InlineData(3, 126, 0)]
    [InlineData(24, 162, 0)]
    [InlineData(110, 126, 0)]
    [InlineData(201, 252, 0)]
    [InlineData(178, 126, 0)]
    [InlineData(135, 0, 126)]
    [InlineData(137, 0, 108)]
    [InlineData(144, 90, 0)]
    [InlineData(239, 414, 0)]
    [InlineData(324, 0, 110)]
    [InlineData(419, 54, 0)]
    [InlineData(650, 1476, 0)]
    [InlineData(468, 144, 0)]
    [InlineData(493, 108, 0)]
    public void TileDropTable_Rejects_Unknown_Style_Frame(int tileType, short frameX, short frameY)
    {
        Assert.False(TileDropTable.TryGet(tileType, frameX, frameY, out _, out _));
    }

    [Fact]
    public void TileBreakCommand_Tree_AxePower_Adds_One_Wood_Per_Tree()
    {
        var world = NewTileWorld(5, out int tx, out int ty);
        lock (world.PlayersLock)
        {
            var player = world.Players[1];
            player.SelectedSlot = 0;
            player.Items[0] = 10;
        }

        Assert.True(new TileBreakCommand(1, 1, tx, ty, 0, 0).Apply(world, new FixedRng(9)).Applied);
        var wood = Assert.Single(world.Items);
        Assert.Equal(9, wood.ItemId);
        Assert.Equal(2, wood.Stack);
        Assert.Equal(9, AxePowerTable.AxePowerOf(10));
        Assert.Equal(0, AxePowerTable.AxePowerOf(99999));
    }

    /// <summary>
    /// SSC 玩家档案编解码往返：背包（含前缀）+ 生命 / 法力逐字段还原；空 / 截断存档按「无档案」处理；
    /// 存档里生命为 0（断线时已死亡）→ 重进按满血复活。
    /// </summary>
    [Fact]
    public void PlayerProfileCodec_RoundTrips_Inventory_And_Vitals()
    {
        var source = new PlayerRuntime { Hp = 73, HpMax = 120, Mp = 45, MpMax = 60 };
        source.Items[3] = 122;        // 熔岩镐
        source.ItemStacks[3] = 12;
        source.ItemPrefixes[3] = 5;
        source.Items[58] = 3319;      // 眼魔宝袋
        source.ItemStacks[58] = 1;

        var restored = new PlayerRuntime();
        Assert.True(PlayerProfileCodec.TryApply(PlayerProfileCodec.Encode(source), restored));

        Assert.Equal(73, restored.Hp);
        Assert.Equal(120, restored.HpMax);
        Assert.Equal(45, restored.Mp);
        Assert.Equal(60, restored.MpMax);
        Assert.Equal(122, restored.Items[3]);
        Assert.Equal(12, restored.ItemStacks[3]);
        Assert.Equal(5, restored.ItemPrefixes[3]);
        Assert.Equal(3319, restored.Items[58]);
        Assert.Equal(1, restored.ItemStacks[58]);

        Assert.False(PlayerProfileCodec.TryApply(null, new PlayerRuntime()));
        Assert.False(PlayerProfileCodec.TryApply(new byte[] { 9, 1, 2 }, new PlayerRuntime())); // 版本不符/截断

        var dead = new PlayerRuntime { Hp = 0, HpMax = 100 };
        var revived = new PlayerRuntime();
        Assert.True(PlayerProfileCodec.TryApply(PlayerProfileCodec.Encode(dead), revived));
        Assert.Equal(100, revived.Hp);
    }

    [Fact]
    public void PlayerProfileCodec_LegacyVersionOne_RestoresNonEmptyItemsWithOneStack()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)1);
            writer.Write(100);
            writer.Write(100);
            writer.Write(20);
            writer.Write(20);
            for (int i = 0; i < PlayerRuntime.InventorySlotCount; i++)
            {
                writer.Write((short)(i == 3 ? 122 : 0));
                writer.Write((byte)(i == 3 ? 5 : 0));
            }
        }

        var restored = new PlayerRuntime();
        Assert.True(PlayerProfileCodec.TryApply(stream.ToArray(), restored));
        Assert.Equal(122, restored.Items[3]);
        Assert.Equal(1, restored.ItemStacks[3]);
        Assert.Equal(5, restored.ItemPrefixes[3]);
        Assert.Equal(0, restored.ItemStacks[4]);
    }

    /// <summary>
    /// 负 netID 变体的**生命上限**必须与客户端一致。包 23 不下发 lifeMax，客户端血条 =
    /// 「服务端发的当前生命 ÷ 客户端按 netID 自己算的 lifeMax」，而客户端对变体走
    /// <c>SetDefaultsFromNetId</c>（末尾 <c>lifeMax = life</c>，即变体上限）。
    /// 服务端沿用基础类型上限会让「当前生命 &gt; 客户端上限」→ 血条被截断为满
    /// （真机：绿史莱姆掉一半血后血条回到满）。
    /// </summary>
    [Theory]
    [InlineData(-3, 1, 14)]    // 绿史莱姆（基础蓝史莱姆 25）
    [InlineData(-4, 1, 150)]   // Pinky
    [InlineData(-6, 1, 45)]    // 黑史莱姆
    [InlineData(-7, 1, 40)]    // 紫史莱姆
    [InlineData(-10, 1, 60)]   // 丛林史莱姆
    [InlineData(-13, 31, 72)]  // AngryBones 小体型
    [InlineData(-56, 231, 42)] // HornetFatty 小体型
    [InlineData(1, 1, 25)]     // 正值 netID：基础类型上限
    [InlineData(5, 5, 8)]      // 克苏鲁之仆（客户端上限 8）
    [InlineData(211, 211, 10)] // 小蜜蜂
    public void NpcStats_OfNetId_Uses_Variant_LifeMax(int netId, int expectedType, int expectedLifeMax)
    {
        var stats = NpcStatsTable.OfNetId(netId);
        Assert.Equal(expectedType, NpcNetIdMap.FromNetId(netId));
        Assert.Equal(expectedLifeMax, stats.LifeMax);
    }

    /// <summary>
    /// 生物群系度量：阈值取原版（腐化 300 / 神圣 125），且**神圣与邪恶互相抵消**；
    /// 深度带按地表 / 岩层 / 地狱分层。
    /// </summary>
    [Fact]
    public void BiomeScanner_Applies_Vanilla_Thresholds_And_DepthBands()
    {
        var world = new WorldState
        {
            MaxTilesX = 400,
            MaxTilesY = 500,
            Tiles = new TileMap(400, 500),
            WorldSurface = 100,
            RockLayer = 200,
        };

        var scanner = new BiomeScanner();

        // 无群系图格 → 全部 false；y=150 落在泥土层
        var clean = scanner.Scan(world, 200, 150);
        Assert.False(clean.Corrupt);
        Assert.False(clean.Crimson);
        Assert.False(clean.Hallow);
        Assert.False(clean.Desert);
        Assert.True(clean.BelowSurface);
        Assert.Equal(DepthBand.Dirt, clean.Depth);

        // 铺 300 格黑檀石（23）→ 腐化达标
        for (int i = 0; i < 300; i++)
        {
            ref var t = ref world.Tiles[200 + (i % 80), 150 + (i / 80)];
            t.Active = true;
            t.Type = 23;
        }
        var corrupt = scanner.Scan(world, 200, 150);
        Assert.True(corrupt.Corrupt);
        Assert.False(corrupt.Hallow);

        // 再叠 125 格珍珠石（109）→ 神圣与邪恶互相抵消：holy(125) 被 evil(300) 抹平、
        // evil 也被 holyRaw(125) 扣减到 175 < 300 → 两者都不成立（原版 AggregateTileCounts 行为）
        for (int i = 0; i < 125; i++)
        {
            ref var t = ref world.Tiles[200 + (i % 80), 160 + (i / 80)];
            t.Active = true;
            t.Type = 109;
        }
        var mixed = scanner.Scan(world, 200, 150);
        Assert.False(mixed.Corrupt);
        Assert.False(mixed.Hallow);

        // 深度带：天空 ≤ 0.35×地表、地表 ≤ 地表、泥土 ≤ 岩层、岩层 ≤ (世界高−200)、其余为地狱
        Assert.Equal(DepthBand.Sky, scanner.Scan(world, 200, 30).Depth);
        Assert.Equal(DepthBand.Overworld, scanner.Scan(world, 200, 50).Depth);
        Assert.Equal(DepthBand.Dirt, scanner.Scan(world, 200, 150).Depth);
        Assert.Equal(DepthBand.Rock, scanner.Scan(world, 200, 250).Depth);
        Assert.Equal(DepthBand.Underworld, scanner.Scan(world, 200, 350).Depth);
    }

    /// <summary>
    /// 刷怪速率（原版 <c>GetSpawnRate</c>）：白天基准 600/5；夜晚 ×0.6/×1.3；血月再 ×0.3/×1.8；
    /// 日食 ×0.2/×1.9；困难模式基准 540/6；最后叠「附近怪少 → 刷得快」的阶梯。
    /// </summary>
    [Fact]
    public void SpawnRate_Applies_DayNight_And_Event_Modifiers()
    {
        var zones = new SceneZones(false, false, false, false, false, false, false, false,
            false, false, false, false, DepthBand.Overworld);

        SpawnRateContext Ctx(bool day, bool blood, bool eclipse, bool hard) => new()
        {
            HardMode = hard,
            PlayerCenterY = 101 * 16f,
            WorldSurface = 100,
            RockLayer = 200,
            UnderworldLayer = 300,
            DayTime = day,
            BloodMoon = blood,
            Eclipse = eclipse,
            Zones = zones,
            TownNpcs = 0,
            NearbyActiveNpcs = 0,
            Invaders = false,
            ActivePlayers = 1,
        };

        Assert.Equal((360, 5), SpawnRate.Compute(Ctx(day: true, blood: false, eclipse: false, hard: false)));
        Assert.Equal((216, 6), SpawnRate.Compute(Ctx(day: false, blood: false, eclipse: false, hard: false)));
        Assert.Equal((64, 10), SpawnRate.Compute(Ctx(day: false, blood: true, eclipse: false, hard: false)));
        Assert.Equal((72, 9), SpawnRate.Compute(Ctx(day: true, blood: false, eclipse: true, hard: false)));
        Assert.Equal((324, 6), SpawnRate.Compute(Ctx(day: true, blood: false, eclipse: false, hard: true)));

        // 入侵：固定 spawnRate = 20，上限按人数放宽（5 × (2 + 0.3 × 1) = 11）
        var invasion = SpawnRate.Compute(new SpawnRateContext
        {
            HardMode = false,
            PlayerCenterY = 101 * 16f,
            WorldSurface = 100,
            RockLayer = 200,
            UnderworldLayer = 300,
            DayTime = true,
            BloodMoon = false,
            Eclipse = false,
            Zones = zones,
            TownNpcs = 0,
            NearbyActiveNpcs = 0,
            Invaders = true,
            ActivePlayers = 1,
        });
        Assert.Equal(20, invasion.SpawnRate);
        Assert.Equal(11, invasion.MaxSpawns);
    }

    /// <summary>构造一个「地表 / 泥土层」通用上下文；<paramref name="day"/> 决定昼夜分支。</summary>
    private static EnemySpawnContext SurfaceCtx(bool day) => new()
    {
        Zones = default,
        Progress = new WorldProgress(),
        Npcs = new List<WorldNpc>(),
        CavernMonsterTypes = EnemySpawnPool.BuildCavernMonsterTypes(1),
        DayTime = day,
        HardMode = false,
        ExpertMode = false,
        Raining = false,
        Invaders = false,
        InvasionType = 0,
        WaterTile = false,
        NoWorms = false,
        SkyMob = false,
        MoonPhase = 1,
        TimeOfDay = 27000,     // 正午（原版鸟类只在 time < 18000 的清晨刷）
        WindSpeedTarget = 0f,
        SpawnTileX = 200,
        SpawnTileY = 100,
        GroundTileType = 2, // 草地
        WallType = 0,
        MaxTilesX = 4200,
        MaxTilesY = 1200,
        WorldSpawnTileX = 2100,
        WorldSurface = 100,
        RockLayer = 300,
        ActivePlayers = 1,
        PlayerHasStartingHealth = true,
    };

    /// <summary>
    /// 地表白天：原版这一段以**小动物**为主（1/15 的分支），其余是史莱姆家族兜底。
    /// 小动物含飞行型（鸟）。原版鸟群只在清晨（<c>Main.time &lt; 18000</c>）且距出生点近时出现。
    /// </summary>
    [Fact]
    public void SpawnPool_SurfaceDay_SpawnsCrittersAndSlimes()
    {
        var allowed = new HashSet<int>
        {
            // 小动物：兔 / 企鹅 / 松鼠 / 蝴蝶 / 金蝴蝶 / 瓢虫 / 椿象 / 鸟（含黄金变体）
            46, 148, 149, 299, 356, 444, 538, 539, 443, 604, 605, 669, 74, 297, 298, 442,
            // 史莱姆家族兜底（原始 netID；-3 绿 / -7 紫 解析后同为 1）
            -3, -7, 1,
        };
        var seen = new HashSet<int>();
        var rng = new XoshiroRng(2024);
        var ctx = SurfaceCtx(day: true);
        ctx.WorldSpawnTileX = ctx.SpawnTileX;   // 距出生点近 → 鸟群分支可达
        ctx.TimeOfDay = 6000;                   // 清晨

        for (int i = 0; i < 3000; i++)
        {
            int netId = EnemySpawnPool.Pick(rng, ctx);
            Assert.Contains(netId, allowed);
            seen.Add(netId);
        }
        Assert.Contains(-3, seen);  // 绿史莱姆（出生点附近非专家时原版必定给绿史莱姆，解析后为 1）
        Assert.Contains(46, seen);  // 兔（贴地型小动物）
        Assert.Contains(74, seen);  // 鸟（飞行型小动物）
    }

    /// <summary>地表夜晚：僵尸家族（含 <c>zombieStyle</c> 换皮）、恶魔眼、萤火虫与 1/5 概率的火把僵尸。</summary>
    [Fact]
    public void SpawnPool_SurfaceNight_IsZombieFamily()
    {
        var allowed = new HashSet<int> { 3, 132, 186, 187, 188, 189, 200, 590, 355, 358, 2 };
        var rng = new XoshiroRng(777);
        var ctx = SurfaceCtx(day: false);

        for (int i = 0; i < 600; i++)
            Assert.Contains(NpcNetIdMap.FromNetId(EnemySpawnPool.Pick(rng, ctx)), allowed);
    }

    /// <summary>血月（非困难）：僵尸照常，但 2/5 概率换成血僵尸 / 滴血者。</summary>
    [Fact]
    public void SpawnPool_BloodMoon_SpawnsBloodZombieAndDrippler()
    {
        var allowed = new HashSet<int>
        {
            2, 3, 53, 132, 186, 187, 188, 189, 200, 355, 358, 489, 490, 536, 590,
        };
        var seen = new HashSet<int>();
        var rng = new XoshiroRng(999);
        var ctx = SurfaceCtx(day: false);
        ctx.BloodMoon = true;

        for (int i = 0; i < 800; i++)
        {
            int type = NpcNetIdMap.FromNetId(EnemySpawnPool.Pick(rng, ctx));
            Assert.Contains(type, allowed);
            seen.Add(type);
        }
        Assert.Contains(489, seen); // BloodZombie
        Assert.Contains(490, seen); // Drippler
    }

    /// <summary>日食（未击败世纪之花）：只应出现日食专属池的成员。</summary>
    [Fact]
    public void SpawnPool_Eclipse_UsesEclipsePool()
    {
        var allowed = new HashSet<int> { 251, 159, 469, 162, 461, 462, 166 };
        var rng = new XoshiroRng(31337);
        var ctx = SurfaceCtx(day: true);
        ctx.Eclipse = true;

        for (int i = 0; i < 400; i++)
            Assert.Contains(NpcNetIdMap.FromNetId(EnemySpawnPool.Pick(rng, ctx)), allowed);
    }

    /// <summary>高空刷怪：飞龙需困难模式，故普通难度只出哈比（与 1/25 的紫史莱姆解锁位）。</summary>
    [Fact]
    public void SpawnPool_Sky_UsesSkyPool()
    {
        var allowed = new HashSet<int> { 48, 686 };
        var rng = new XoshiroRng(4242);
        var ctx = SurfaceCtx(day: true);
        ctx.SkyMob = true;

        for (int i = 0; i < 200; i++)
            Assert.Contains(NpcNetIdMap.FromNetId(EnemySpawnPool.Pick(rng, ctx)), allowed);
    }

    /// <summary>地狱层（y &gt; 世界高 − 190）：恶魔 / 岩浆史莱姆 / 骨蛇 / 火焰小鬼。</summary>
    [Fact]
    public void SpawnPool_Underworld_UsesHellPool()
    {
        var allowed = new HashSet<int> { 24, 39, 59, 60, 62, 66 };
        var rng = new XoshiroRng(555);
        var ctx = SurfaceCtx(day: true);
        ctx.SpawnTileY = ctx.MaxTilesY - 100;

        for (int i = 0; i < 400; i++)
            Assert.Contains(NpcNetIdMap.FromNetId(EnemySpawnPool.Pick(rng, ctx)), allowed);
    }

    /// <summary>入侵（哥布林）：只应出现原版 <c>invasionType == 1</c> 分支的成员。</summary>
    [Fact]
    public void SpawnPool_GoblinInvasion_UsesGoblinPool()
    {
        var allowed = new HashSet<int> { 26, 27, 28, 29, 111 };
        var seen = new HashSet<int>();
        var rng = new XoshiroRng(808);
        var ctx = SurfaceCtx(day: true);
        ctx.Invaders = true;
        ctx.InvasionType = 1;

        for (int i = 0; i < 400; i++)
        {
            int type = NpcNetIdMap.FromNetId(EnemySpawnPool.Pick(rng, ctx));
            Assert.Contains(type, allowed);
            seen.Add(type);
        }
        Assert.Contains(26, seen); // GoblinPeon
        Assert.Contains(28, seen); // GoblinWarrior
    }

    /// <summary>
    /// 岩层 + 丛林群系：丛林史莱姆（netID −10）与丛林蝙蝠（51）都应出现（原版的群系专属分支）。
    /// 注意断言用**原始 netID**：负向变体的解析类型可能与其它变体相同（−6 / −10 都解析为 1）。
    /// </summary>
    [Fact]
    public void SpawnPool_JungleCavern_SpawnsJungleMonsters()
    {
        var allowed = new HashSet<int>
        {
            10, 16, 21, 44, 51, 195, 201, 202, 203, 217, 218, 453, // 洞穴主力（含洞穴甲虫）
            -6, -10, -46, -47, -48, -49, -50, -51, -52, -53,
            494, 495, 496, 497, 498, 499, 500, 501, 502, 503, 504, 505, 506,
        };
        var seen = new HashSet<int>();
        var rng = new XoshiroRng(60606);
        var ctx = SurfaceCtx(day: true);
        ctx.SpawnTileY = 400;          // 岩层（rockLayer = 300）
        ctx.Zones = ctx.Zones with { Jungle = true };

        for (int i = 0; i < 400; i++)
        {
            int netId = EnemySpawnPool.Pick(rng, ctx);
            Assert.Contains(netId, allowed);
            seen.Add(netId);
        }
        Assert.Contains(-10, seen); // JungleSlime（丛林群系专属）
        Assert.Contains(51, seen);  // JungleBat（丛林群系专属）
    }

    /// <summary>
    /// 群系图格：原版「生成阶段就该有」的群系必须真的产出，且能被服务端场景度量判出来。
    /// 判据直接走 <see cref="BiomeScanner"/> —— 与刷怪读的是同一条链路（所以这同时验证了
    /// 群系刷怪池在生成世界里「可达」）。
    /// </summary>
    [Fact]
    public void Generate_Produces_Biomes_DetectedBySceneMetrics()
    {
        var world = WorldGenerator.GenerateSmall();
        var scanner = new BiomeScanner();
        int w = world.MaxTilesX;
        int surface = (int)world.WorldSurface;
        int mushroomY = Math.Min((int)world.RockLayer + 120, world.MaxTilesY - 200 - 120);

        // 邪恶群系（腐化 / 猩红二选一，与原版一致），且 Progress.Crimson 与图格一致
        var evil = scanner.Scan(world, w * 20 / 100, surface + 20);
        Assert.True(evil.Corrupt ^ evil.Crimson, "邪恶群系未产出（应恰有腐化或猩红之一）");
        Assert.Equal(world.Progress.Crimson, evil.Crimson);

        // 雪原
        Assert.True(scanner.Scan(world, w * 30 / 100, surface + 20).Snow, "雪原未产出");

        // 沙漠 + 地下沙漠（后者要求沙岩 / 硬化沙墙）
        var desert = scanner.Scan(world, w * 63 / 100, surface + 40);
        Assert.True(desert.Desert, "沙漠未产出");
        Assert.True(desert.UndergroundDesert, "地下沙漠未产出（缺沙岩 / 硬化沙墙）");

        // 丛林
        Assert.True(scanner.Scan(world, w * 80 / 100, surface + 20).Jungle, "丛林未产出");

        // 发光蘑菇地
        Assert.True(scanner.Scan(world, w * 41 / 100, mushroomY).Glowshroom, "发光蘑菇地未产出");

        // 地牢（地牢砖 + 地牢墙），并写下了包 7 的 DungeonX/Y
        Assert.True(scanner.Scan(world, world.DungeonX, world.DungeonY).Dungeon, "地牢未产出");
        Assert.True(world.DungeonX > 420, "地牢 X 未写入 / 落点异常");
        Assert.True(world.DungeonY > surface, "地牢 Y 应在地表之下");
    }

    // ========================================================================
    // 二十二、困难模式转换 与 陨石
    // ========================================================================

    /// <summary>
    /// 地下生命水晶（原版 <c>WorldGen.AddLifeCrystal</c>）：2×2 摆放、帧 (0/18, 0/18)、
    /// 坐在两块实心砖上；数量与世界宽度成比例。
    /// </summary>
    [Fact]
    public void Generate_Places_Life_Crystals()
    {
        var world = WorldGenerator.GenerateSmall();

        int hearts = CountTiles(world, t => t == 12);
        Assert.True(hearts >= 16, $"生命水晶过少：{hearts} 格（应 ≥ 4 颗）");
        Assert.Equal(0, hearts % 4);   // 2×2 摆放 ⇒ 图格数必为 4 的倍数

        Assert.True(TryFindTile(world, t => t == 12, out int hx, out int hy), "未找到生命水晶");
        Assert.Equal((short)0, world.Tiles[hx, hy].FrameX);
        Assert.Equal((short)0, world.Tiles[hx, hy].FrameY);
        Assert.Equal(12, world.Tiles[hx + 1, hy].Type);
        Assert.Equal(12, world.Tiles[hx, hy + 1].Type);
        Assert.Equal((short)18, world.Tiles[hx + 1, hy + 1].FrameX);
    }

    /// <summary>血肉墙（113）被击杀 → 世界进度置为困难模式（原版 <c>WorldGen.StartHardmode</c>）。</summary>
    [Fact]
    public void WallOfFlesh_Kill_Sets_HardMode()
    {
        var world = new WorldState();
        Assert.False(world.Progress.HardMode);

        world.NotifyNpcKilled(113, 100, 100, new XoshiroRng(1));

        Assert.True(world.Progress.HardMode);
        Assert.True(world.ProgressDirty); // 包 7 需要重新下发
    }

    /// <summary>
    /// 困难模式地形转换（原版 <c>initializeHardMode</c>）：进入困难模式后
    /// 世界应凭空多出**神圣带**（珍珠石 / 神圣草 / 珍珠沙…），且能被子群系度量判为 <c>ZoneHallow</c>
    /// —— 这正是「神圣池可达」的前提。
    /// </summary>
    [Fact]
    public void Hardmode_Conversion_Creates_Hallow_Band()
    {
        var world = WorldGenerator.GenerateSmall();
        int hallowBefore = CountTiles(world, t => t is 117 or 109 or 116 or 164);
        Assert.Equal(0, hallowBefore); // 生成阶段不产出神圣（与原版一致）

        int evilBefore = CountTiles(world, t => t is 23 or 25 or 199 or 203);

        // 走真实路径：服务端先跑起来 → 血肉墙被击杀（进度置位）→ 下一次 tick 执行转换
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, HpMax = 100, Hp = 100 };
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        world.NotifyNpcKilled(113, 100, 100, new XoshiroRng(3));
        sim.Tick();

        int hallowAfter = CountTiles(world, t => t is 117 or 109 or 116 or 164);
        Assert.True(hallowAfter >= 300, $"神圣带未产出（神圣图格仅 {hallowAfter}）");
        Assert.True(CountTiles(world, t => t is 23 or 25 or 199 or 203) > evilBefore, "邪恶带未刷新");

        // 在神圣带内找一个点，场景度量必须判出 Hallow
        var scanner = new BiomeScanner();
        Assert.True(TryFindTile(world, t => t == 117, out int hx, out int hy), "未找到珍珠石");
        Assert.True(scanner.Scan(world, hx, hy).Hallow, "神圣带内未判出 ZoneHallow");
    }

    /// <summary>困难模式转换只执行一次（原版 <c>StartHardmode</c>：已是困难模式则直接返回）。</summary>
    [Fact]
    public void Hardmode_Conversion_Runs_Only_Once()
    {
        var world = WorldGenerator.GenerateSmall();
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, Active = true, HpMax = 100, Hp = 100 };
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());

        world.NotifyNpcKilled(113, 100, 100, new XoshiroRng(3));
        sim.Tick();
        int after = CountTiles(world, t => t is 117 or 109 or 116 or 164);
        Assert.True(after > 0, "首次 tick 未执行困难模式转换");

        for (int i = 0; i < 5; i++) sim.Tick();
        Assert.Equal(after, CountTiles(world, t => t is 117 or 109 or 116 or 164));
    }

    /// <summary>
    /// 陨石坠落（原版 <c>WorldGen.meteor</c>）：在指定落点生成陨石坑，
    /// 陨石图格数量足以让场景度量判出 <c>ZoneMeteor</c>（阈值 75）—— 即陨石池可达。
    /// </summary>
    [Fact]
    public void Meteor_Creates_Crater_And_Unlocks_MeteorZone()
    {
        var world = WorldGenerator.GenerateSmall();   // 无玩家：落点检查不因玩家屏幕拒绝

        int x = world.SpawnTileX + 700;               // 远离向导（NPC 附近会被拒绝）
        int y = (int)world.WorldSurface + 60;
        Assert.True(WorldGenerator.TryPlaceMeteor(world, new XoshiroRng(2024), x, y), "陨石落点被拒绝");

        int meteorite = CountTiles(world, t => t == 37);
        Assert.True(meteorite > 200, $"陨石图格过少：{meteorite}");
        Assert.True(new BiomeScanner().Scan(world, x, y).Meteor, "陨石坑内未判出 ZoneMeteor");
    }

    /// <summary>统计全世界满足条件的图格数。</summary>
    private static int CountTiles(WorldState world, Func<ushort, bool> match)
    {
        int n = 0;
        for (int x = 0; x < world.MaxTilesX; x++)
            for (int y = 0; y < world.MaxTilesY; y++)
            {
                ref var tile = ref world.Tiles[x, y];
                if (tile.Active && match(tile.Type)) n++;
            }
        return n;
    }

    /// <summary>找第一个满足条件的图格坐标（自左向右、自上向下）。</summary>
    private static bool TryFindTile(WorldState world, Func<ushort, bool> match, out int tx, out int ty)
    {
        for (int x = 1; x < world.MaxTilesX - 1; x++)
            for (int y = 1; y < world.MaxTilesY - 1; y++)
            {
                ref var tile = ref world.Tiles[x, y];
                if (tile.Active && match(tile.Type)) { tx = x; ty = y; return true; }
            }
        tx = 0;
        ty = 0;
        return false;
    }

    /// <summary>群系锚点必须避开出生点周围（否则出生点会被群系包住，初始刷怪与出生保护都受影响）。</summary>
    [Fact]
    public void Generate_Keeps_SpawnArea_Free_Of_Biomes()
    {
        var world = WorldGenerator.GenerateSmall();
        var scanner = new BiomeScanner();
        int surface = (int)world.WorldSurface;

        var at = scanner.Scan(world, world.SpawnTileX, surface - 2); // 出生点所在的地表
        Assert.False(at.Corrupt);
        Assert.False(at.Crimson);
        Assert.False(at.Hallow);
        Assert.False(at.Jungle);
        Assert.False(at.Snow);
        Assert.False(at.Desert);
        Assert.False(at.Glowshroom);
        Assert.False(at.Meteor);
        Assert.False(at.Dungeon);
        Assert.False(at.Graveyard);
    }

    // ========================================================================
    // 二十一、小动物（critter）分支
    // ========================================================================

    /// <summary>小动物集合取原版 <c>NPCID.Sets.CountsAsCritter</c>；飞行 / 贴地按原版 aiStyle 分派。</summary>
    [Theory]
    [InlineData(46, false)]    // Bunny（aiStyle 7 → 贴地）
    [InlineData(148, false)]   // Penguin（7）
    [InlineData(299, false)]   // Squirrel（7）
    [InlineData(377, false)]   // Grasshopper（1）
    [InlineData(357, false)]   // Worm（66）
    [InlineData(359, false)]   // Snail（67）
    [InlineData(74, true)]     // Bird（24）
    [InlineData(442, true)]    // GoldBird（24）
    [InlineData(355, true)]    // Firefly（64）
    [InlineData(356, true)]    // Butterfly（65）
    [InlineData(583, true)]    // FairyCritter（112）
    [InlineData(596, true)]    // Dragonfly（114）
    public void CritterSet_Classifies_Flyers_ByVanillaAiStyle(int type, bool flyer)
    {
        Assert.True(NpcCritterSet.Is(type));
        Assert.Equal(flyer, NpcCritterSet.IsFlyer(type));
    }

    /// <summary>敌对 NPC 不得被误判为小动物（否则会被套上非敌对行为）。</summary>
    [Theory]
    [InlineData(1)]    // BlueSlime
    [InlineData(3)]    // Zombie
    [InlineData(26)]   // GoblinPeon
    [InlineData(222)]  // QueenBee
    public void CritterSet_Excludes_Hostiles(int type) => Assert.False(NpcCritterSet.Is(type));

    /// <summary>
    /// 小动物**不追玩家**：贴在玩家旁边的兔子会背向逃离（原版小动物受惊逃走），
    /// 而不是像僵尸一样扑上来 —— 这正是「小动物分支修复」要保证的行为。
    /// </summary>
    [Fact]
    public void Critter_FleesFromPlayer_InsteadOfChasing()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        int sx = world.SpawnTileX, sy = world.SpawnTileY;
        float playerX = sx * 16f + 8f;

        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Hp = 100,
                HpMax = 100,
                Position = new Vector2(playerX, sy * 16f - 42f),
                AimPosition = new Vector2(playerX, sy * 16f - 42f),
            };

        // 兔子放在玩家右侧 2 格（受惊距离 8 格内）
        var bunny = new WorldNpc
        {
            Type = 46,
            NetId = 46,
            Active = true,
            Life = 5,
            LifeMax = 5,
            X = playerX + 32f,
            Y = sy * 16f - 20f,
        };
        lock (world.NpcsLock) world.Npcs.Add(bunny);

        float startDistance = MathF.Abs(bunny.X - playerX);
        for (int i = 0; i < 60; i++) sim.Tick();
        float endDistance = MathF.Abs(bunny.X - playerX);

        Assert.True(endDistance > startDistance,
            $"小动物未远离玩家（{startDistance:F0} → {endDistance:F0}），疑似套用了敌对追击");
        Assert.True(bunny.Direction > 0, "玩家在左侧，兔子应向右逃离（Direction > 0）");
    }

    /// <summary>小动物 type → 原版 aiStyle 的映射必须逐个正确（分派错就会套上敌对 AI）。</summary>
    [Theory]
    [InlineData(46, 7)]    // Bunny
    [InlineData(303, 7)]   // 万圣节兔（非 TownCritter）
    [InlineData(377, 1)]   // Grasshopper（走 AI_001 的蚂蚱分支）
    [InlineData(446, 1)]   // GoldGrasshopper
    [InlineData(55, 16)]   // Goldfish
    [InlineData(688, 16)]  // 青蛙（大）
    [InlineData(74, 24)]   // Bird
    [InlineData(611, 24)]  // Seagull
    [InlineData(355, 64)]  // Firefly
    [InlineData(677, 64)]  // LightningBug
    [InlineData(356, 65)]  // Butterfly
    [InlineData(357, 66)]  // Worm
    [InlineData(374, 66)]  // MagmaSnail 变体
    [InlineData(359, 67)]  // Snail
    [InlineData(360, 67)]  // GlowingSnail
    [InlineData(363, 68)]  // Duck
    [InlineData(609, 68)]  // MallardDuck
    [InlineData(583, 112)] // FairyCritter
    [InlineData(596, 114)] // Dragonfly
    [InlineData(604, 115)] // Ladybug
    [InlineData(669, 115)] // GoldLadybug
    [InlineData(612, 116)] // WaterStrider
    [InlineData(626, 118)] // Seahorse
    public void CritterSet_Maps_To_Vanilla_AiStyle(int type, int style)
    {
        Assert.True(NpcCritterSet.Is(type));
        Assert.Equal(style, NpcCritterSet.AiStyleOf(type));
    }

    /// <summary>
    /// 鸟（aiStyle 24，贴地态）在玩家靠近时起飞：`ai[0]` 由 0 变 1，并向上获得初速。
    /// </summary>
    [Fact]
    public void Critter_Bird_TakesOff_WhenPlayerApproaches()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        int sx = world.SpawnTileX, sy = world.SpawnTileY;
        float playerX = sx * 16f + 8f;

        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Hp = 100,
                HpMax = 100,
                Position = new Vector2(playerX, sy * 16f - 42f),
                AimPosition = new Vector2(playerX, sy * 16f - 42f),
            };

        // 鸟（14×14）放在玩家右侧 2 格，脚底贴地
        var bird = new WorldNpc
        {
            Type = 74,
            NetId = 74,
            Active = true,
            Life = 5,
            LifeMax = 5,
            X = playerX + 32f,
            Y = sy * 16f - 14f,
        };
        lock (world.NpcsLock) world.Npcs.Add(bird);

        float startY = bird.Y;
        for (int i = 0; i < 30; i++) sim.Tick();

        Assert.Equal(1f, bird.Ai[0]);
        Assert.True(bird.Y < startY - 8f, $"鸟未起飞（Y {startY:F0} → {bird.Y:F0}）");
    }

    /// <summary>
    /// 蚯蚓（aiStyle 66）在「爬行态」（ai[0] == 1）会水平位移，且贴地不下坠。
    /// </summary>
    [Fact]
    public void Critter_Worm_Crawls_When_Awake()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        int sx = world.SpawnTileX, sy = world.SpawnTileY;

        var worm = new WorldNpc
        {
            Type = 357,
            NetId = 357,
            Active = true,
            Life = 5,
            LifeMax = 5,
            X = sx * 16f,
            Y = sy * 16f - 4f,      // 蚯蚓 10×4，脚底贴地
        };
        worm.Ai[0] = 1f;            // 直接进入爬行态
        worm.LocalAi[1] = 600f;     // 远离下次状态切换
        lock (world.NpcsLock) world.Npcs.Add(worm);

        float startX = worm.X;
        for (int i = 0; i < 60; i++) sim.Tick();

        Assert.True(MathF.Abs(worm.X - startX) > 3f,
            $"蚯蚓在爬行态未水平移动（X {startX:F0} → {worm.X:F0}）");
        Assert.True(worm.Y < sy * 16f, "蚯蚓不应穿地");
    }

    /// <summary>
    /// 蜗牛（aiStyle 67）是 noGravity + 需图格碰撞的典型：爬行 / 爬墙都不能穿透地面。
    /// 这条同时覆盖「NoGravity 与 NoTileCollide 解耦」的物理步改动。
    /// </summary>
    [Fact]
    public void Critter_Snail_DoesNotSinkThroughGround()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        int sx = world.SpawnTileX, sy = world.SpawnTileY;

        var snail = new WorldNpc
        {
            Type = 359,
            NetId = 359,
            Active = true,
            Life = 5,
            LifeMax = 5,
            X = sx * 16f,
            Y = sy * 16f - 12f,   // 蜗牛 12×12
        };
        lock (world.NpcsLock) world.Npcs.Add(snail);

        float startY = snail.Y;
        for (int i = 0; i < 120; i++) sim.Tick();

        Assert.True(snail.Y < startY + 64f,
            $"蜗牛穿透地面下坠（Y {startY:F0} → {snail.Y:F0}），疑似 noTileCollide 与 noGravity 未解耦");
        Assert.True(MathF.Abs(snail.VelocityY) <= 1f, "蜗牛垂直速度不应失控");
    }

    /// <summary>
    /// 蚂蚱（aiStyle 1 的蚂蚱分支）在玩家逼近时**背向跳跃**（而不是像史莱姆一样扑向玩家）。
    /// </summary>
    [Fact]
    public void Critter_Grasshopper_HopsAway_FromPlayer()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        int sx = world.SpawnTileX, sy = world.SpawnTileY;
        float playerX = sx * 16f + 8f;

        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime
            {
                Id = 1,
                Active = true,
                Hp = 100,
                HpMax = 100,
                Position = new Vector2(playerX, sy * 16f - 42f),
                AimPosition = new Vector2(playerX, sy * 16f - 42f),
            };

        // 蚂蚱（14×10）置于玩家右侧
        var grasshopper = new WorldNpc
        {
            Type = 377,
            NetId = 377,
            Active = true,
            Life = 5,
            LifeMax = 5,
            X = playerX + 32f,
            Y = sy * 16f - 10f,
        };
        lock (world.NpcsLock) world.Npcs.Add(grasshopper);

        float startDistance = MathF.Abs(grasshopper.X - playerX);
        for (int i = 0; i < 60; i++) sim.Tick();
        float endDistance = MathF.Abs(grasshopper.X - playerX);

        Assert.True(endDistance > startDistance,
            $"蚂蚱未背向玩家跳开（{startDistance:F0} → {endDistance:F0}），疑似套用了史莱姆追击");
        Assert.True(grasshopper.Direction > 0, "玩家在左侧，蚂蚱应向右跳开（Direction > 0）");
    }

    /// <summary>
    /// 物理步口径：<c>NoGravity</c> 只管重力，<c>NoTileCollide</c> 才跳图格碰撞。
    /// 只设 <c>NoGravity = true</c> 的 NPC 仍必须落地并置位 <c>CollideY</c> / <c>Grounded</c>
    /// （旧实现把两者混在一起，导致蜗牛 / 仙灵 / 蜻蜓永远拿不到碰撞标志）。
    /// </summary>
    [Fact]
    public void Physics_NoGravity_Still_Collides_WithTiles()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        int sx = world.SpawnTileX, sy = world.SpawnTileY;

        var npc = new WorldNpc
        {
            Type = 9999,          // 未登记类型 → AiFallback（不写 NoGravity / NoTileCollide）
            NetId = 9999,
            Active = true,
            Life = 100,
            LifeMax = 100,
            X = sx * 16f,
            Y = sy * 16f - 32f - 40f,
            VelocityY = 4f,
            NoGravity = true,     // 无重力
            NoTileCollide = false, // 但要图格碰撞
        };
        lock (world.NpcsLock) world.Npcs.Add(npc);

        for (int i = 0; i < 30; i++) sim.Tick();

        Assert.True(npc.Grounded, "NoGravity 的 NPC 仍应落地（Grounded）");
        // NoGravity 下重力不会改写 velocity.Y，因此速度归零只可能来自图格碰撞 —— 这正是解耦的判据。
        Assert.Equal(0f, npc.VelocityY);
        Assert.Equal(0f, (npc.Y + 32f) % 16f);   // 脚底精确贴到图格上沿
    }

    /// <summary>
    /// 换型（原版 <c>NPC.Transform</c>）必须同时同步 <c>netID</c>：客户端只在包 23 的 netID
    /// 与本机不一致时才 <c>SetDefaults</c> 重建外观，只改 <c>Type</c> 会让客户端一直画旧形态
    /// （海鸥 603 ↔ 602 这类形态往返正是靠它）。
    /// 同时校验原版 Transform 的两个不变量：**脚底不动**（Y += 旧高 − 新高）与 ai 覆写。
    /// </summary>
    [Fact]
    public void NpcTransform_SyncsNetId_And_KeepsFootAnchored()
    {
        var world = WorldGenerator.GenerateSmall();
        var sim = new WorldSimulator(world, new CommandQueue(), new EventRecorder(), new SnapshotStore());
        int sx = world.SpawnTileX, sy = world.SpawnTileY;

        // 游动形态海鸥（603，28×22）停在陆地；ai[0]=1 飞行态 + ai[1]=300 → 本轮落地换型回 602（22×26）
        var gull = new WorldNpc
        {
            Type = 603,
            NetId = 603,
            Active = true,
            Life = 5,
            LifeMax = 5,
            X = sx * 16f,
            Y = sy * 16f - 22f,
        };
        gull.Ai[0] = 1f;
        gull.Ai[1] = 300f;
        lock (world.NpcsLock) world.Npcs.Add(gull);

        float footBefore = gull.Y + 22f;   // 脚底 = Y + 旧高
        sim.Tick();

        Assert.Equal(602, gull.Type);
        Assert.Equal((short)602, gull.NetId);          // ★ netID 必须跟着换
        Assert.True(gull.SyncForced, "换型后必须强制补发一次包 23");
        Assert.Equal(0f, gull.Ai[0]);
        Assert.InRange(gull.Ai[1], 200f, 399f);        // 原版 Transform(type-1, 0f, 200 + rand(200))
        Assert.Equal(footBefore, gull.Y + 26f, 3);     // 脚底不动（新高 26）
    }

    // ========================================================================
    // 二十二、地表树木（原版 GrowTree 的树干部分）
    // ========================================================================

    /// <summary>
    /// 世界生成必须长出地表树木（TileID.Trees = 5），且帧值必须落在原版树干 / 枝条 / 树顶帧表内
    /// —— 帧值写错会让客户端画出碎片。
    /// </summary>
    [Fact]
    public void WorldGenerator_Places_Surface_Trees_WithVanillaFrames()
    {
        var world = WorldGenerator.GenerateSmall();
        short[] validFrameX = { 0, 22, 44, 66, 88, 110 };
        short[] validFrameY = { 0, 22, 44, 66, 88, 110, 132, 154, 176, 198, 220, 242 };

        int treeTiles = 0;
        for (int x = 0; x < world.MaxTilesX; x++)
        {
            for (int y = 0; y < world.MaxTilesY; y++)
            {
                ref var tile = ref world.Tiles[x, y];
                if (!tile.Active || tile.Type != 5) continue;

                treeTiles++;
                Assert.Contains(tile.FrameX, validFrameX);
                Assert.Contains(tile.FrameY, validFrameY);
            }
        }

        Assert.True(treeTiles > 100, $"地表树木过少（{treeTiles} 格），疑似未生成");
    }

    /// <summary>树的根部必须落在地表「可长树」图格上（草地 / 腐化 / 猩红 / 丛林 / 神圣 / 蘑菇草 / 雪块）。</summary>
    [Fact]
    public void WorldGenerator_Tree_Trunks_Are_Rooted_OnTreeGround()
    {
        var world = WorldGenerator.GenerateSmall();
        int rooted = 0;

        for (int x = 0; x < world.MaxTilesX; x++)
        {
            for (int y = 1; y < world.MaxTilesY - 1; y++)
            {
                ref var tile = ref world.Tiles[x, y];
                if (!tile.Active || tile.Type != 5) continue;

                ref var below = ref world.Tiles[x, y + 1];
                if (!below.Active) continue;
                if (below.Type is 2 or 23 or 199 or 60 or 70 or 109 or 147) rooted++;
            }
        }

        Assert.True(rooted > 20, $"落在可长树地表上的树干过少（{rooted} 格）");
    }

    /// <summary>出生点保护半径内不长树（否则树会挡在出生点正上方）。</summary>
    [Fact]
    public void WorldGenerator_Keeps_SpawnArea_Free_OfTrees()
    {
        var world = WorldGenerator.GenerateSmall();

        for (int x = world.SpawnTileX - 10; x <= world.SpawnTileX + 10; x++)
            for (int y = 1; y < world.MaxTilesY; y++)
                Assert.False(world.Tiles[x, y].Active && world.Tiles[x, y].Type == 5,
                    $"出生点范围内出现树木图格（{x}, {y}）");
    }
}
