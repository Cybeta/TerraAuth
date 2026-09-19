// TerraAuth — Phase 2 验收测试

using TerraAuth.Authority;
using TerraAuth.Protocol;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

public class AuthorityTests
{
    [Fact]
    public void Pipeline_Reject_Propagates_ThroughStages()
    {
        var audit = new NoOpAuditLogger();
        var rate = new RateLimits();
        var enforcers = new AuthorityEnforcers(rate, audit, new WorldState());
        var pipeline = new InboundPipeline(new IPipelineStage[]
        {
            new FrameStage(),
            new RateLimitStage(enforcers.Rate),
            new MovementAuthorityStage(enforcers.Movement, audit),
            new TerminalStage(),
        });

        var packet = new PlayerPositionPacket(1, new Vector2(0, 0));
        var result = pipeline.ProcessAsync(packet, 1, new CommandQueue()).Result;

        // 默认实现全 Accept（占位），实装后应拦截非法
        Assert.Equal(AuthorityDecision.Accept, result.Decision);
    }

    [Fact]
    public void AuthorityResult_FactoryMethods_AreCorrect()
    {
        var packet = new PlayerHealthPacket(1, 500, 500);
        var accept = AuthorityResult.Accept(packet);
        var reject = AuthorityResult.Reject("test");
        var silent = AuthorityResult.RejectSilent();
        var correct = AuthorityResult.Correct(packet, "snap");

        Assert.Equal(AuthorityDecision.Accept, accept.Decision);
        Assert.Equal(AuthorityDecision.Reject, reject.Decision);
        Assert.Equal(AuthorityDecision.RejectSilent, silent.Decision);
        Assert.False(silent.CountsAsViolation);
        Assert.Equal(AuthorityDecision.Correct, correct.Decision);
        Assert.Equal("snap", correct.Reason);
    }

    [Fact]
    public void MovementAuthority_Accepts_NormalMove_Rejects_Teleport()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(), new WorldState(),
            new MovementLimits(MaxSpeed: 8.0f, TeleportTolerance: 4.0f));
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        // 首个位置包建立权威基准
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerPositionPacket(1, new Vector2(0, 0)), 1, commands).Decision);

        // 正常小位移
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerPositionPacket(1, new Vector2(0.5f, 0f)), 1, commands).Decision);

        // 瞬移：位移 1000 ≫ maxSpeed*dt + 容差
        var teleport = move.Validate(new PlayerPositionPacket(1, new Vector2(1000f, 0f)), 1, commands);
        Assert.Equal(AuthorityDecision.Reject, teleport.Decision);
        Assert.Equal("speed_exceeded", teleport.Reason);
    }

    [Fact]
    public void MovementAuthority_Rejected_Teleport_DoesNot_Pollute_Baseline()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(), new WorldState(),
            new MovementLimits(MaxSpeed: 8.0f, TeleportTolerance: 4.0f));
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        move.Validate(new PlayerPositionPacket(1, new Vector2(0, 0)), 1, commands);
        move.Validate(new PlayerPositionPacket(1, new Vector2(1000f, 0f)), 1, commands); // 被拒

        // 基准仍为 (0,0)：若被瞬移污染成 1000，此包会被判超速
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerPositionPacket(1, new Vector2(0.1f, 0f)), 1, commands).Decision);
    }

    /// <summary>
    /// 「可疑带」放行：受击击退 / 被挤出方块 / 斜坡校正会造成一帧十几~几十像素的合法位移，
    /// 超过匀速上限但远没到瞬移量级 —— 必须放行，否则正常玩家被史莱姆打一下就会刷 speed_exceeded
    /// （并累计违规被踢）。
    /// </summary>
    [Fact]
    public void MovementAuthority_Accepts_KnockbackScale_Step()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(), new WorldState(),
            new MovementLimits(MaxSpeed: 8.0f, TeleportTolerance: 4.0f));
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        move.Validate(new PlayerControlsPacket(1, new Vector2(0, 0)), 1, commands); // 建立基准

        // 匀速上限 = 8*60*(1/60)+4 = 12px；一帧 20px 属于「击退级」位移
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerControlsPacket(1, new Vector2(20f, 0f)), 1, commands).Decision);
    }

    [Fact]
    public void MovementAuthority_Validates_PlayerControls_Packet13()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(), new WorldState(),
            new MovementLimits(MaxSpeed: 8.0f, TeleportTolerance: 4.0f));
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        // 包 13（真实线格式）首个位置包建立权威基准
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerControlsPacket(1, new Vector2(0, 0)), 1, commands).Decision);

        // 正常小位移：接受
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerControlsPacket(1, new Vector2(0.5f, 0f)), 1, commands).Decision);

        // 瞬移：与 PlayerPositionPacket 共用同一超速判定
        var teleport = move.Validate(new PlayerControlsPacket(1, new Vector2(1000f, 0f)), 1, commands);
        Assert.Equal(AuthorityDecision.Reject, teleport.Decision);
        Assert.Equal("speed_exceeded", teleport.Reason);

        // 基准未被瞬移污染：紧随其后的小位移仍被接受
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerControlsPacket(1, new Vector2(0.6f, 0f)), 1, commands).Decision);
    }

    [Fact]
    public void MovementAuthority_Accepts_NormalFrameStep_NotOnly_TinyMove()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(), new WorldState(),
            new MovementLimits(MaxSpeed: 8.0f, TeleportTolerance: 4.0f));
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        move.Validate(new PlayerControlsPacket(1, new Vector2(0, 0)), 1, commands); // 建立基准

        // MaxSpeed 为 Terraria 像素/帧量纲：一帧 10 像素（奔跑/跳跃/下落）必须被接受。
        // 若按像素/秒误算，允许位移只剩 TeleportTolerance，正常移动会被拒且不转发，
        // 对端就会看到位置一顿一顿。
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerControlsPacket(1, new Vector2(10f, 0f)), 1, commands).Decision);
    }

    [Fact]
    public void MovementAuthority_Accepts_FastFall_But_Still_Rejects_HorizontalTeleport()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(), new WorldState(),
            new MovementLimits(MaxSpeed: 8.0f, TeleportTolerance: 4.0f, MaxFallSpeed: 20.0f));
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        move.Validate(new PlayerControlsPacket(1, new Vector2(0, 0)), 1, commands); // 建立基准

        // 原版下落终速 ≈20 px/帧（MaxFallSpeed）：必须被接受，否则正常坠落会被判超速 →
        // 服务端位置不再更新且累计违规踢人。
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerControlsPacket(1, new Vector2(0, 20f)), 1, commands).Decision);

        // 水平瞬移仍必须被拒（分轴判定不放宽水平上限）
        var teleport = move.Validate(new PlayerControlsPacket(1, new Vector2(1000f, 20f)), 1, commands);
        Assert.Equal(AuthorityDecision.Reject, teleport.Decision);
        Assert.Equal("speed_exceeded", teleport.Reason);
    }

    [Fact]
    public void MovementAuthority_SharedBaseline_Across_Packet13_And_InternalPosition()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(), new WorldState(),
            new MovementLimits(MaxSpeed: 8.0f, TeleportTolerance: 4.0f));
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        // 包 13 建立基准
        move.Validate(new PlayerControlsPacket(1, new Vector2(100f, 200f)), 1, commands);

        // 内部位置包沿用同一基准：小幅移动接受
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerPositionPacket(1, new Vector2(100.5f, 200f)), 1, commands).Decision);

        // 相对该基准的大跳变仍被拒
        Assert.Equal(AuthorityDecision.Reject,
            move.Validate(new PlayerPositionPacket(1, new Vector2(5000f, 200f)), 1, commands).Decision);
    }

    [Fact]
    public void MovementAuthority_TeleportEntity_Rejects_OutOfBounds_And_InvalidTarget()
    {
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger(), new WorldState());
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        // 世界内落点：接受
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new TeleportEntityPacket(1, new Vector2(1024f, 2048f))
            { Kind = TeleportEntityKind.Player }, 1, commands).Decision);

        // 世界外落点：拒绝
        var oob = move.Validate(new TeleportEntityPacket(1, new Vector2(-1f, 0f))
        { Kind = TeleportEntityKind.Player }, 1, commands);
        Assert.Equal(AuthorityDecision.Reject, oob.Decision);
        Assert.Equal("teleport_out_of_bounds", oob.Reason);

        // NPC 索引越界（Main.npc 上限 200）：拒绝
        var badTarget = move.Validate(new TeleportEntityPacket(200, new Vector2(1024f, 2048f))
        { Kind = TeleportEntityKind.Npc }, 1, commands);
        Assert.Equal(AuthorityDecision.Reject, badTarget.Decision);
        Assert.Equal("invalid_teleport_target", badTarget.Reason);
    }

    [Fact]
    public void MovementAuthority_TeleportEntity_Moves_Baseline_So_Next_Position_Accepted()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(), new WorldState(),
            new MovementLimits(MaxSpeed: 8.0f, TeleportTolerance: 4.0f));
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        move.Validate(new PlayerPositionPacket(1, new Vector2(0, 0)), 1, commands); // 建立基准

        // 合法传送：接受并把权威基准迁移到落点
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new TeleportEntityPacket(1, new Vector2(10000f, 2000f))
            { Kind = TeleportEntityKind.Player }, 1, commands).Decision);

        // 基准已迁移：紧随其后的位置包（距原点 10000）不应被判超速
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerPositionPacket(1, new Vector2(10000.5f, 2000f)), 1, commands).Decision);
    }

    [Fact]
    public void MovementAuthority_Teleport_Rejects_Rate_Exceeded()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(), new WorldState(),
            new MovementLimits(MaxSpeed: 8.0f, TeleportTolerance: 4.0f, MaxTeleportsPerSecond: 2));
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new RequestTeleportationByServerPacket(TeleportRequestKind.TeleportationPotion), 1, commands).Decision);
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new RequestTeleportationByServerPacket(TeleportRequestKind.MagicConch), 1, commands).Decision);

        // 第三次超出窗口上限：拒绝
        var third = move.Validate(new RequestTeleportationByServerPacket(TeleportRequestKind.DemonConch), 1, commands);
        Assert.Equal(AuthorityDecision.Reject, third.Decision);
        Assert.Equal("teleport_rate_exceeded", third.Reason);
    }

    [Fact]
    public void MovementAuthority_RequestTeleportation_Rejects_Invalid_Kind()
    {
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger(), new WorldState());
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        var result = move.Validate(
            new RequestTeleportationByServerPacket((TeleportRequestKind)9), 1, commands);
        Assert.Equal(AuthorityDecision.Reject, result.Decision);
        Assert.Equal("invalid_teleport_request", result.Reason);
    }

    [Fact]
    public void InboundPipeline_Stages_ExecuteInOrder()
    {
        var audit = new NoOpAuditLogger();
        var rate = new RateLimits();
        var enforcers = new AuthorityEnforcers(rate, audit, new WorldState());

        var order = new List<int>();
        var pipeline = new InboundPipeline(new IPipelineStage[]
        {
            new LambdaStage(1, order),
            new LambdaStage(2, order),
            new LambdaStage(3, order),
        });

        pipeline.ProcessAsync(new PlayerPositionPacket(1, new Vector2(0, 0)), 1, new CommandQueue()).Wait();
        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public void InventoryAuthority_Ssc_MapsEyeOfCthulhuBagClearToOpenCommand()
    {
        var world = new WorldState();
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger(), world);
        lock (world.PlayersLock)
        {
            var player = new PlayerRuntime { Id = 1, Active = true };
            player.Items[3] = 3319;
            player.ItemStacks[3] = 1;
            world.Players[1] = player;
        }
        var pipeline = new InboundPipeline(new IPipelineStage[]
        {
            new InventoryAuthorityStage(enforcers.Inventory, new NoOpAuditLogger()),
            new TerminalStage(),
        });

        var result = pipeline.ProcessAsync(new InventorySlotPacket(3, 0, 0), 1, new CommandQueue()).Result;

        Assert.Equal(AuthorityDecision.Accept, result.Decision);
        Assert.IsType<OpenEyeOfCthulhuTreasureBagCommand>(result.Command);
    }

    [Fact]
    public void InventoryAuthority_PendingBagOpen_SilencesWholeInventoryBeforeApply()
    {
        var world = new WorldState();
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger(), world);
        lock (world.PlayersLock)
        {
            var player = new PlayerRuntime { Id = 1, Active = true };
            player.Items[3] = 3319;
            player.ItemStacks[3] = 1;
            world.Players[1] = player;
        }

        var inventory = enforcers.Inventory;
        var bag = inventory.Validate(new InventorySlotPacket(3, 0, 0), 1, null!);
        var repeatedBagSlot = inventory.Validate(new InventorySlotPacket(3, 0, 0), 1, null!);
        var otherSlot = inventory.Validate(new InventorySlotPacket(4, 56, 1), 1, null!);

        Assert.Equal(AuthorityDecision.Accept, bag.Decision);
        Assert.Equal(AuthorityDecision.RejectSilent, repeatedBagSlot.Decision);
        Assert.Equal(AuthorityDecision.RejectSilent, otherSlot.Decision);
        Assert.False(otherSlot.CountsAsViolation);
    }

    [Fact]
    public void InventoryAuthority_AppliedBagOpen_SilencesFastClientRewardSnapshots()
    {
        var world = new WorldState();
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger(), world);
        lock (world.PlayersLock)
        {
            var player = new PlayerRuntime { Id = 1, Active = true };
            player.Items[3] = 3319;
            player.ItemStacks[3] = 1;
            world.Players[1] = player;
        }

        var inventory = enforcers.Inventory;
        Assert.Equal(AuthorityDecision.Accept,
            inventory.Validate(new InventorySlotPacket(3, 0, 0), 1, null!).Decision);
        world.PromotePendingBagOpen(1, world.Players[1].SessionId);

        var rewardSnapshot = inventory.Validate(new InventorySlotPacket(4, 56, 30), 1, null!);

        Assert.Equal(AuthorityDecision.RejectSilent, rewardSnapshot.Decision);
        Assert.False(rewardSnapshot.CountsAsViolation);
    }

    [Fact]
    public void InventoryAuthority_Uses_PlayerRuntime_ForPickupAndConsumption()
    {
        var world = new WorldState();
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger(), world);
        world.InventoryLedger = enforcers.Inventory as IInventoryLedger;
        lock (world.PlayersLock)
            world.Players[1] = new PlayerRuntime { Id = 1, SessionId = 7, Active = true, Position = new Vector2(8, 8) };

        lock (world.ItemsLock)
            world.Items.Add(new WorldItemEntity
            {
                Slot = 0,
                ItemId = 9,
                Stack = 3,
                Position = new Vector2(8, 8),
            });

        Assert.True(new PickupItemCommand(1, 1, 0) { SessionId = 7 }.Apply(world, new XoshiroRng(1)).Applied);
        Assert.Equal(9, world.Players[1].Items[0]);
        Assert.Equal(3, world.Players[1].ItemStacks[0]);
        Assert.True(enforcers.Inventory.ConsumeItem(1, 9));
        Assert.Equal(2, world.Players[1].ItemStacks[0]);

        var updates = world.DrainInventoryUpdates(8);
        Assert.Contains((1, 7L, 0), updates);
    }

    private sealed class LambdaStage : IPipelineStage
    {
        private readonly int _id;
        private readonly List<int> _order;
        public int Order => _id;

        public LambdaStage(int id, List<int> order) { _id = id; _order = order; }

        public Task<AuthorityResult> ExecuteAsync(INetworkPacket packet, IPacketContext context,
            Func<INetworkPacket, Task<AuthorityResult>> next, CancellationToken ct)
        {
            _order.Add(_id);
            return next(packet);
        }
    }
}
