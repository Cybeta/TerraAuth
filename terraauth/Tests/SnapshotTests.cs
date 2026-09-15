// TerraAuth — Phase 4 验收测试

using TerraAuth.Net.Snapshots;
using TerraAuth.Net.Transport;
using TerraAuth.Protocol;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

public class SnapshotTests
{
    [Fact]
    public void SnapshotFrame_Tick_IsMonotonic()
    {
        var a = SnapshotFrame.Create(1, System.Array.Empty<EntityState>(), System.Array.Empty<RemovedEntity>());
        var b = SnapshotFrame.Create(2, System.Array.Empty<EntityState>(), System.Array.Empty<RemovedEntity>());
        Assert.True(b.Tick > a.Tick);
    }

    [Fact]
    public void SnapshotBuilder_Build_Produces_Delta_WithBaseTick()
    {
        var world = new WorldState();
        var prev = SnapshotFrame.Create(10, System.Array.Empty<EntityState>(), System.Array.Empty<RemovedEntity>());
        var builder = new SnapshotBuilder(world, prev);

        var frame = builder.Build();

        Assert.Equal((uint)world.Tick, frame.Tick);
        Assert.Equal(10u, frame.BaseTick); // 增量基址 = 上一帧
    }

    [Fact]
    public void SnapshotBuilder_Build_Extracts_PlayerEntities()
    {
        var world = new WorldState();
        world.Players[1] = new PlayerRuntime { Id = 1, Position = new Vector2(10, 20), Velocity = new Vector2(1, 2) };
        world.Players[2] = new PlayerRuntime { Id = 2, Position = new Vector2(30, 40), Active = false };

        // 无基线 → 全量：所有玩家都进 Entities
        var frame = new SnapshotBuilder(world).Build();

        Assert.Equal(0u, frame.BaseTick); // 全量快照
        Assert.Equal(2, frame.Entities.Count);
        Assert.Empty(frame.Removed);

        var p1 = frame.Entities.Single(e => e.Id == 1);
        Assert.Equal(new Vector2(10, 20), p1.Position);
        Assert.Equal(new Vector2(1, 2), p1.Velocity);
        Assert.Equal(EntityStateType.Active, p1.State);

        // 离线玩家映射为 Hidden（而非消失）
        var p2 = frame.Entities.Single(e => e.Id == 2);
        Assert.Equal(EntityStateType.Hidden, p2.State);
    }

    [Fact]
    public void SnapshotBuilder_Build_Delta_Only_Includes_Changed_And_Reports_Removed()
    {
        var world = new WorldState();
        world.Players[1] = new PlayerRuntime { Id = 1, Position = new Vector2(11, 20), Velocity = new Vector2(1, 0) }; // 相对上一帧已移动
        world.Players[2] = new PlayerRuntime { Id = 2, Position = new Vector2(5, 5) }; // 未变化

        var previous = SnapshotFrame.Create(
            7,
            new[]
            {
                new EntityState(1, new Vector2(10, 20), new Vector2(1, 0), EntityStateType.Active),
                new EntityState(2, new Vector2(5, 5), new Vector2(0, 0), EntityStateType.Active),
                new EntityState(3, new Vector2(1, 1), new Vector2(0, 0), EntityStateType.Active),
            },
            System.Array.Empty<RemovedEntity>());

        var frame = new SnapshotBuilder(world, previous).Build();

        Assert.Equal(7u, frame.BaseTick); // 增量基址 = 上一帧
        var changed = Assert.Single(frame.Entities); // 只有变化的玩家 1
        Assert.Equal(1, changed.Id);
        Assert.Equal(new Vector2(11, 20), changed.Position);

        var removed = Assert.Single(frame.Removed); // 上一帧存在、本帧消失的玩家 3
        Assert.Equal(3, removed.Id);
        Assert.Equal(RemoveReason.Despawn, removed.Reason);
    }

    [Fact]
    public void SnapshotBuilder_Build_Extracts_NpcEntities_WithNamespacedIds()
    {
        var world = new WorldState();
        world.Players[1] = new PlayerRuntime { Id = 1, Position = new Vector2(10, 20) };
        world.Npcs.Add(new WorldNpc { Type = 22, X = 100, Y = 200, IsTownNpc = true });
        world.Npcs.Add(new WorldNpc { Type = 17, X = 300, Y = 400 });

        var frame = new SnapshotBuilder(world).Build(); // 无基线 → 全量

        Assert.Equal(3, frame.Entities.Count); // 1 玩家 + 2 NPC

        // NPC 使用负 Id 命名空间（-1 - 索引），与玩家正整数 Id 不冲突
        var npc0 = frame.Entities.Single(e => e.Id == SnapshotFrame.NpcEntityId(0));
        Assert.Equal(new Vector2(100, 200), npc0.Position);
        Assert.Equal(new Vector2(0, 0), npc0.Velocity); // WorldNpc 无速度字段
        Assert.Equal(EntityStateType.Active, npc0.State);

        Assert.Contains(frame.Entities, e => e.Id == SnapshotFrame.NpcEntityId(1));
        Assert.Contains(frame.Entities, e => e.Id == 1); // 玩家 Id 不受命名空间影响
    }

    [Fact]
    public void SnapshotBuilder_Build_Delta_Includes_Only_Moved_Npcs()
    {
        var world = new WorldState();
        world.Npcs.Add(new WorldNpc { Type = 22, X = 100, Y = 200 });
        world.Npcs.Add(new WorldNpc { Type = 17, X = 300, Y = 400 });

        var previous = new SnapshotBuilder(world).Build(); // 全量基线

        world.Npcs[1].X = 350; // 仅 NPC 1 移动
        var delta = new SnapshotBuilder(world, previous).Build();

        var changed = Assert.Single(delta.Entities); // 静态 NPC 被 SameState 省略
        Assert.Equal(SnapshotFrame.NpcEntityId(1), changed.Id);
        Assert.Equal(new Vector2(350, 400), changed.Position);
        Assert.Empty(delta.Removed);
    }

    [Fact]
    public void Broadcaster_SubmitInputs_Generates_MoveCommand()
    {
        var world = new WorldState();
        var commands = new CommandQueue();
        var broadcaster = new SnapshotBroadcaster(
            world,
            commands,
            new SnapshotConfig(),
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current)); // 不测试下发

        var inputs = new[] { new ClientInput(1, new Vector2(1, 0), false, false, 1) };
        broadcaster.SubmitInputs(1, inputs);

        // 输入经影子预测重放后生成 MoveCommand，目标 tick = 当前 tick + 1
        Assert.Equal(1, commands.Count);
        Assert.True(commands.TryPeek(out var command));
        var move = Assert.IsType<MoveCommand>(command);
        Assert.Equal(1, move.PlayerId);
        Assert.Equal(1L, move.Tick);            // _tick 初始为 0 → 下一 tick 为 1
        Assert.Equal(new Vector2(1, 0), move.Position); // 基准 (0,0) + 位移 (1,0)*1
    }

    [Fact]
    public void Broadcaster_SubmitInputs_Clamps_OverSpeed_Command()
    {
        var world = new WorldState();
        var commands = new CommandQueue();
        var broadcaster = new SnapshotBroadcaster(
            world,
            commands,
            new SnapshotConfig { ShadowPredictionMaxSpeedPerTick = 4f },
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current));

        // 客户端声明单 tick 位移 100px（远超服务端允许的 4px）
        broadcaster.SubmitInputs(1, new[] { new ClientInput(1, new Vector2(100, 0), false, false, 1) });

        Assert.True(commands.TryPeek(out var command));
        var move = Assert.IsType<MoveCommand>(command);
        Assert.Equal(4f, move.Position.X, 3); // 被钳制到速度上限
    }

    [Fact]
    public void Broadcaster_OnAck_TriggersCatchUp()
    {
        var world = new WorldState();
        var store = new SnapshotStore();
        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig(),
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current));

        // 填充几帧
        for (uint i = 1; i <= 3; i++)
        {
            var frame = SnapshotFrame.Create(i, System.Array.Empty<EntityState>(), System.Array.Empty<RemovedEntity>());
            broadcaster.Enqueue(i, frame);
        }

        broadcaster.OnAck(playerId: 1, ackedTick: 2);

        var catchUp = broadcaster.GetFullSnapshotFor(1);
        Assert.Contains(catchUp, f => f.Tick == 3); // 只返回 ackedTick 之后
    }

    [Fact]
    public void Broadcaster_BuildFrameFor_UsesPerPlayerBaseTick()
    {
        var world = new WorldState();
        world.Players[1] = new PlayerRuntime { Id = 1, Position = new Vector2(0, 0) };
        world.Players[2] = new PlayerRuntime { Id = 2, Position = new Vector2(0, 0) };

        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig(),
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current));

        for (uint i = 1; i <= 3; i++)
        {
            broadcaster.Enqueue(i, SnapshotFrame.Create(
                i,
                new[] { new EntityState((int)i, new Vector2(0, 0), new Vector2(0, 0), EntityStateType.Active) },
                System.Array.Empty<RemovedEntity>()));
        }

        broadcaster.OnAck(playerId: 1, ackedTick: 2);

        // 每玩家分桶：玩家 1 以自己确认的 tick 为增量基址，玩家 2 从未确认 → 全量（BaseTick = 0）
        Assert.Equal(2u, broadcaster.BuildFrameFor(1).BaseTick);
        Assert.Equal(0u, broadcaster.BuildFrameFor(2).BaseTick);
    }

    [Fact]
    public void Broadcaster_BuildFrameFor_Culls_OutOfViewport()
    {
        var world = new WorldState();
        world.Players[1] = new PlayerRuntime { Id = 1, Position = new Vector2(0, 0) };

        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig { ViewportRadius = 100f },
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current));

        broadcaster.Enqueue(1, SnapshotFrame.Create(
            1,
            new[]
            {
                new EntityState(1, new Vector2(0, 0), new Vector2(0, 0), EntityStateType.Active),    // 视野中心
                new EntityState(2, new Vector2(50, 0), new Vector2(0, 0), EntityStateType.Active),   // 范围内
                new EntityState(3, new Vector2(500, 0), new Vector2(0, 0), EntityStateType.Active),  // 范围外
            },
            System.Array.Empty<RemovedEntity>()));

        var frame = broadcaster.BuildFrameFor(1);

        // 范围内实体保留，范围外实体转为 OutOfRange 移除通知
        Assert.Collection(
            frame.Entities.OrderBy(static e => e.Id),
            e => Assert.Equal(1, e.Id),
            e => Assert.Equal(2, e.Id));

        var culled = Assert.Single(frame.Removed);
        Assert.Equal(3, culled.Id);
        Assert.Equal(RemoveReason.OutOfRange, culled.Reason);
    }

    [Fact]
    public void Broadcaster_BuildFrameFor_ViewportZero_Disables_Culling()
    {
        var world = new WorldState();
        world.Players[1] = new PlayerRuntime { Id = 1, Position = new Vector2(0, 0) };

        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig { ViewportRadius = 0f }, // 关闭裁剪
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current));

        broadcaster.Enqueue(1, SnapshotFrame.Create(
            1,
            new[] { new EntityState(3, new Vector2(500, 0), new Vector2(0, 0), EntityStateType.Active) },
            System.Array.Empty<RemovedEntity>()));

        var frame = broadcaster.BuildFrameFor(1);

        Assert.Contains(frame.Entities, e => e.Id == 3); // 远处实体仍下发
        Assert.Empty(frame.Removed);
    }

    [Fact]
    public void Broadcaster_MergeSince_IncludesTickZero_WhenBaseTickIsZero()
    {
        var world = new WorldState();
        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig { ViewportRadius = 0f }, // 关闭裁剪，聚焦归并逻辑
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current));

        // Tick 0 是全量基线帧：客户端无基线（baseTick = 0）时必须纳入归并，不能被过滤掉
        broadcaster.Enqueue(0, SnapshotFrame.Create(
            0,
            new[] { new EntityState(10, new Vector2(1, 1), new Vector2(0, 0), EntityStateType.Active) },
            System.Array.Empty<RemovedEntity>()));
        broadcaster.Enqueue(1, SnapshotFrame.Create(
            1,
            new[] { new EntityState(11, new Vector2(2, 2), new Vector2(0, 0), EntityStateType.Active) },
            System.Array.Empty<RemovedEntity>()));

        var frame = broadcaster.BuildFrameFor(1); // 从未确认 → baseTick = 0（全量语义）

        Assert.Equal(0u, frame.BaseTick);
        Assert.Equal(1u, frame.Tick);
        Assert.Contains(frame.Entities, e => e.Id == 10); // Tick 0 帧的实体
        Assert.Contains(frame.Entities, e => e.Id == 11); // Tick 1 帧的实体
    }

    [Fact]
    public void Broadcaster_BuildFrameFor_DedupesRemovedEntries()
    {
        var world = new WorldState();
        world.Players[1] = new PlayerRuntime { Id = 1, Position = new Vector2(0, 0) };

        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig { ViewportRadius = 100f },
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current));

        broadcaster.Enqueue(1, SnapshotFrame.Create(
            1,
            new[]
            {
                new EntityState(1, new Vector2(0, 0), new Vector2(0, 0), EntityStateType.Active),    // 视野内
                new EntityState(2, new Vector2(500, 0), new Vector2(0, 0), EntityStateType.Active),  // 视野外
            },
            new[]
            {
                new RemovedEntity(3, RemoveReason.Despawn),
                new RemovedEntity(3, RemoveReason.Despawn), // 重复条目
            }));

        var frame = broadcaster.BuildFrameFor(1);

        var kept = Assert.Single(frame.Entities);
        Assert.Equal(1, kept.Id);

        // 视野外实体只记一次 OutOfRange；帧内重复的移除项被去重
        Assert.Equal(2, frame.Removed.Count);
        Assert.Single(frame.Removed, r => r.Id == 2 && r.Reason == RemoveReason.OutOfRange);
        Assert.Single(frame.Removed, r => r.Id == 3 && r.Reason == RemoveReason.Despawn);
    }

    [Fact]
    public void Broadcaster_BuildFrameFor_FallsBackToFullSnapshot_WhenHistoryTrimmed()
    {
        var world = new WorldState();
        world.Players[2] = new PlayerRuntime { Id = 2, Position = new Vector2(0, 0) };

        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig { ViewportRadius = 0f },
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current));

        for (uint i = 1; i <= 5; i++)
        {
            broadcaster.Enqueue(i, SnapshotFrame.Create(
                i,
                new[] { new EntityState((int)i, new Vector2(0, 0), new Vector2(0, 0), EntityStateType.Active) },
                System.Array.Empty<RemovedEntity>()));
        }

        // 另一玩家确认到 tick 5 → 历史被裁剪，仅剩 tick 5
        broadcaster.OnAck(playerId: 99, ackedTick: 5);

        // 玩家 2 从未确认（baseTick = 0），窗口已不含 tick 1 → 必须回退全量而非残缺 delta
        var frame = broadcaster.BuildFrameFor(2);

        Assert.Equal(0u, frame.BaseTick);                       // 全量语义
        Assert.Contains(frame.Entities, e => e.Id == 2);        // 直接取自 WorldState
        Assert.DoesNotContain(frame.Entities, e => e.Id == 5);  // 不是被裁剪后的残缺合并
    }

    [Fact]
    public void Broadcaster_GetFullSnapshotFor_FallsBackToFullSnapshot_WhenHistoryTrimmed()
    {
        var world = new WorldState();
        world.Players[2] = new PlayerRuntime { Id = 2, Position = new Vector2(0, 0) };

        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig { ViewportRadius = 0f },
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current));

        for (uint i = 1; i <= 5; i++)
        {
            broadcaster.Enqueue(i, SnapshotFrame.Create(
                i,
                new[] { new EntityState((int)i, new Vector2(0, 0), new Vector2(0, 0), EntityStateType.Active) },
                System.Array.Empty<RemovedEntity>()));
        }

        broadcaster.OnAck(playerId: 99, ackedTick: 5); // 裁剪历史

        var frames = broadcaster.GetFullSnapshotFor(2).ToList();

        var single = Assert.Single(frames);              // 窗口不足 → 单帧全量，而非残缺序列
        Assert.Equal(0u, single.BaseTick);
        Assert.Contains(single.Entities, e => e.Id == 2);
    }

    [Fact]
    public void Broadcaster_OnAck_IgnoresAck_BeyondCurrentTick()
    {
        var world = new WorldState();
        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig(),
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current));

        for (uint i = 1; i <= 3; i++)
        {
            broadcaster.Enqueue(i, SnapshotFrame.Create(
                i,
                System.Array.Empty<EntityState>(),
                System.Array.Empty<RemovedEntity>()));
        }

        broadcaster.OnAck(playerId: 1, ackedTick: 999); // 确认尚未下发的 tick → 应被忽略

        // 非法 ack 既未抬高基址，也未触发历史误裁剪：仍能合并出 tick 3 的完整 delta
        var frame = broadcaster.BuildFrameFor(1);
        Assert.Equal(0u, frame.BaseTick);
        Assert.Equal(3u, frame.Tick);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(2)]
    public void SplitFrame_Respects_MaxEntitiesPerPacket(int maxPer)
    {
        // 构造 7 个实体 + 2 个移除项
        var entities = Enumerable.Range(0, 7)
            .Select(i => new EntityState(i, new Vector2(i, 0), default, EntityStateType.Active))
            .ToList();
        var removed = new List<RemovedEntity>
        {
            new(100, RemoveReason.Despawn),
            new(101, RemoveReason.OutOfRange),
        };
        var frame = SnapshotFrame.Create(tick: 9, entities, removed, baseTick: 3);

        var parts = SnapshotBroadcaster.SplitFrame(frame, maxPer);

        // 实体总量守恒
        int total = parts.Sum(p => p.Entities.Count);
        Assert.Equal(7, total);

        // 每份实体数不超上限
        foreach (var part in parts)
        {
            Assert.True(part.Entities.Count <= maxPer, $"子帧实体数 {part.Entities.Count} 超过上限 {maxPer}");
            Assert.Equal(3u, part.BaseTick);
            Assert.Equal(9u, part.Tick);
        }

        // 移除项并入首份；其余子帧无移除
        Assert.Equal(2, parts[0].Removed.Count);
        for (int i = 1; i < parts.Count; i++)
            Assert.Empty(parts[i].Removed);

        // 实体子集与原帧一致（顺序保持）
        var flatten = parts.SelectMany(p => p.Entities).Select(e => e.Id).ToList();
        Assert.Equal(Enumerable.Range(0, 7), flatten);
    }

    [Fact]
    public void SplitFrame_UnderLimit_ReturnsSingleFrame()
    {
        var entities = new List<EntityState>
        {
            new(1, default, default, EntityStateType.Active),
            new(2, default, default, EntityStateType.Active),
        };
        var frame = SnapshotFrame.Create(tick: 5, entities, System.Array.Empty<RemovedEntity>());
        var parts = SnapshotBroadcaster.SplitFrame(frame, maxPerPacket: 256);
        Assert.Single(parts);
        Assert.Same(frame, parts[0]);
    }

    [Fact]
    public void Broadcaster_EmptyStore_FallsBackToFullSnapshot_WithoutThrowing()
    {
        var world = new WorldState();
        world.Players[1] = new PlayerRuntime { Id = 1, Position = new Vector2(5, 5) };

        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig { ViewportRadius = 0f },
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current));

        // 尚未 Enqueue 任何快照：BuildFrameFor / GetFullSnapshotFor 应回退全量而非返回空帧
        var frame = broadcaster.BuildFrameFor(1);
        Assert.Equal(0u, frame.BaseTick);
        Assert.Contains(frame.Entities, e => e.Id == 1);

        var single = Assert.Single(broadcaster.GetFullSnapshotFor(1).ToList());
        Assert.Equal(0u, single.BaseTick);
        Assert.Contains(single.Entities, e => e.Id == 1);
    }

    [Fact]
    public async Task SnapshotStore_ConcurrentAddTrimAndSnapshot_DoesNotThrow()
    {
        var store = new SnapshotStore(capacity: 64);

        // 仿真线程：持续写入 + 裁剪
        var writer = Task.Run(() =>
        {
            for (uint tick = 1; tick <= 5000; tick++)
            {
                store.Add(SnapshotFrame.Create(
                    tick, System.Array.Empty<EntityState>(), System.Array.Empty<RemovedEntity>()));

                if (tick > 32 && tick % 8 == 0)
                    store.TrimBefore(tick - 32);
            }
        });

        // 网络线程：持续读取一致视图（旧实现下会因 List 非线程安全抛越界 / 枚举被修改）
        var reader = Task.Run(() =>
        {
            for (int i = 0; i < 5000; i++)
            {
                Assert.InRange(store.Snapshot().Length, 0, 64);
                _ = store.Count;
                _ = store.LatestOrDefault;
            }
        });

        await Task.WhenAll(writer, reader);
    }

    [Fact]
    public void SnapshotStore_Ring_WrapAndTrim_PreserveAscendingOrder()
    {
        // capacity = 4，写入 6 帧触发绕环覆写
        var store = new SnapshotStore(capacity: 4);
        foreach (uint t in new uint[] { 1, 2, 3, 4, 5, 6 })
            store.Add(SnapshotFrame.Create(t, System.Array.Empty<EntityState>(), System.Array.Empty<RemovedEntity>()));

        // 满后覆写最旧 → 保留 tick 3..6
        Assert.Equal(4, store.Count);
        Assert.Equal(new uint[] { 3, 4, 5, 6 }, store.Snapshot().Select(f => f.Tick));

        // TrimBefore(5) → 移除 3、4，head 前移
        store.TrimBefore(5);
        Assert.Equal(new uint[] { 5, 6 }, store.Snapshot().Select(f => f.Tick));
        Assert.Equal(6u, store.LatestOrDefault!.Tick);

        // 继续写入 7、8、9 → 覆盖 5、6、7，保留 8、9
        foreach (uint t in new uint[] { 7, 8, 9 })
            store.Add(SnapshotFrame.Create(t, System.Array.Empty<EntityState>(), System.Array.Empty<RemovedEntity>()));
        Assert.Equal(4, store.Count);
        Assert.Equal(new uint[] { 6, 7, 8, 9 }, store.Snapshot().Select(f => f.Tick));

        store.Clear();
        Assert.Equal(0, store.Count);
        Assert.Same(Array.Empty<SnapshotFrame>(), store.Snapshot());
    }

    [Fact]
    public void ShadowPredictor_DetectsDeviation()
    {
        var predictor = new ShadowPredictor();
        var reported = new Vector2(100, 100);
        var predicted = new Vector2(0, 0); // 差距巨大

        var significant = predictor.IsDeviationSignificant(
            playerId: 1,
            reported,
            predicted,
            out var deviation);

        Assert.True(significant);
        Assert.True(deviation > 8f);
    }

    [Fact]
    public void ShadowPredictor_ReplaysInputs_OntoAuthoritativePosition()
    {
        var predictor = new ShadowPredictor(
            new SnapshotConfig { ShadowPredictionMaxSpeedPerTick = 10f });

        predictor.RecordInput(1, new ClientInput(1, new Vector2(5, 0), false, false, 1));
        predictor.RecordInput(1, new ClientInput(2, new Vector2(0, 3), false, false, 2));

        var predicted = predictor.Predict(1, new Vector2(100, 100));

        Assert.Equal(105f, predicted.X, 3); // 100 + 5*1
        Assert.Equal(106f, predicted.Y, 3); // 100 + 3*2
    }

    [Fact]
    public void ShadowPredictor_Clamps_OverSpeed_Input()
    {
        var predictor = new ShadowPredictor(
            new SnapshotConfig { ShadowPredictionMaxSpeedPerTick = 4f });

        // 客户端声明单 tick 位移 100px（远超服务端允许的 4px）
        predictor.RecordInput(1, new ClientInput(1, new Vector2(100, 0), false, false, 1));

        var predicted = predictor.Predict(1, new Vector2(0, 0));

        Assert.Equal(4f, predicted.X, 3);
        Assert.True(predictor.IsDeviationSignificant(1, new Vector2(100, 0), predicted, out var deviation));
        Assert.True(deviation > 8f);
    }

    [Fact]
    public void ShadowPredictor_Predict_ConsumesPendingInputs()
    {
        var predictor = new ShadowPredictor(
            new SnapshotConfig { ShadowPredictionMaxSpeedPerTick = 10f });

        predictor.RecordInput(1, new ClientInput(1, new Vector2(5, 0), false, false, 1));

        var first = predictor.Predict(1, new Vector2(0, 0));
        var second = predictor.Predict(1, new Vector2(0, 0)); // 输入已消费

        Assert.Equal(5f, first.X, 3);
        Assert.Equal(new Vector2(0, 0), second);
    }

    [Fact]
    public void ShadowPredictor_DropsOldest_WhenQueueOverflows()
    {
        var predictor = new ShadowPredictor(new SnapshotConfig
        {
            ShadowPredictionMaxSpeedPerTick = 10f,
            ShadowPredictionMaxPendingInputs = 2,
        });

        for (uint i = 1; i <= 5; i++)
            predictor.RecordInput(1, new ClientInput(i, new Vector2(1, 0), false, false, 1));

        var predicted = predictor.Predict(1, new Vector2(0, 0));

        Assert.Equal(2f, predicted.X, 3); // 只保留最后 2 个输入
    }

    [Fact]
    public void ShadowPredictor_UsesConfiguredDeviationThreshold()
    {
        var strict = new ShadowPredictor(new SnapshotConfig { ShadowPredictionMaxDeviation = 1f });
        var loose = new ShadowPredictor(new SnapshotConfig { ShadowPredictionMaxDeviation = 100f });
        var reported = new Vector2(10, 0);
        var predicted = new Vector2(0, 0);

        Assert.True(strict.IsDeviationSignificant(1, reported, predicted, out _));
        Assert.False(loose.IsDeviationSignificant(1, reported, predicted, out _));
    }

    [Fact]
    public void Broadcaster_FullSnapshot_UsesPublishedView_NotLiveWorldState()
    {
        // 仿真线程的活动 WorldState：玩家 1 已移动到 (999,999)
        var world = new WorldState();
        world.Players[1] = new PlayerRuntime { Id = 1, Position = new Vector2(999f, 999f) };

        // 已发布视图：tick 7 时玩家 1 在 (100,100) —— 广播线程只应看到这一份
        var views = new StubViewProvider
        {
            View = WorldEntityView.Extract(SnapshotWorld(playerId: 1, x: 100f, y: 100f, tick: 7)),
        };

        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig { ViewportRadius = 0f },
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current),
            views: views);

        // store 为空 → 必然走全量回退路径
        var frame = broadcaster.BuildFrameFor(1);

        Assert.Equal(7u, frame.Tick); // tick 取自视图
        var entity = Assert.Single(frame.Entities, e => e.Id == 1);
        Assert.Equal(100f, entity.Position.X); // 视图值，而非活动的 999
    }

    [Fact]
    public void Broadcaster_CullCenter_ComesFromPublishedView()
    {
        // 活动位置在原点；若裁剪中心误取活动状态，玩家自身也会被判为超视野
        var world = new WorldState();
        world.Players[1] = new PlayerRuntime { Id = 1, Position = new Vector2(0f, 0f) };

        var views = new StubViewProvider
        {
            View = WorldEntityView.Extract(SnapshotWorld(playerId: 1, x: 1000f, y: 0f, tick: 7)),
        };

        var broadcaster = new SnapshotBroadcaster(
            world,
            new CommandQueue(),
            new SnapshotConfig { ViewportRadius = 100f },
            sender: null!,
            encoder: new PacketEncoder(ProtocolVersion.Current),
            views: views);

        broadcaster.Enqueue(1, SnapshotFrame.Create(1, new[]
        {
            new EntityState(1, new Vector2(1000f, 0f), new Vector2(0, 0), EntityStateType.Active),
            new EntityState(99, new Vector2(5000f, 0f), new Vector2(0, 0), EntityStateType.Active),
        }, System.Array.Empty<RemovedEntity>()));

        var frame = broadcaster.BuildFrameFor(1);

        // 以视图中心 (1000,0) 裁剪：玩家自身保留，远处实体 99 → OutOfRange
        Assert.Contains(frame.Entities, e => e.Id == 1);
        Assert.Single(frame.Removed, r => r.Id == 99 && r.Reason == RemoveReason.OutOfRange);
    }

    /// <summary>构造仅含一个玩家的 WorldState（供视图提取）。</summary>
    private static WorldState SnapshotWorld(int playerId, float x, float y, long tick)
    {
        var world = new WorldState { Tick = tick };
        world.Players[playerId] = new PlayerRuntime { Id = playerId, Position = new Vector2(x, y) };
        return world;
    }

    /// <summary>测试替身：手动控制"已发布实体视图"。</summary>
    private sealed class StubViewProvider : IWorldViewProvider
    {
        public WorldEntityView View { get; set; } = WorldEntityView.Empty;
        public WorldEntityView CurrentEntityView => View;
    }
}
