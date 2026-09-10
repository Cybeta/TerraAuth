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
        var enforcers = new AuthorityEnforcers(rate, audit);
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
        Assert.Equal(AuthorityDecision.Correct, correct.Decision);
        Assert.Equal("snap", correct.Reason);
    }

    [Fact]
    public void MovementAuthority_Accepts_NormalMove_Rejects_Teleport()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(),
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
            new RateLimits(), new NoOpAuditLogger(),
            new MovementLimits(MaxSpeed: 8.0f, TeleportTolerance: 4.0f));
        var move = enforcers.Movement;
        var commands = new CommandQueue();

        move.Validate(new PlayerPositionPacket(1, new Vector2(0, 0)), 1, commands);
        move.Validate(new PlayerPositionPacket(1, new Vector2(1000f, 0f)), 1, commands); // 被拒

        // 基准仍为 (0,0)：若被瞬移污染成 1000，此包会被判超速
        Assert.Equal(AuthorityDecision.Accept,
            move.Validate(new PlayerPositionPacket(1, new Vector2(0.1f, 0f)), 1, commands).Decision);
    }

    [Fact]
    public void MovementAuthority_Validates_PlayerControls_Packet13()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(),
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
            new RateLimits(), new NoOpAuditLogger(),
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
    public void MovementAuthority_SharedBaseline_Across_Packet13_And_InternalPosition()
    {
        var enforcers = new AuthorityEnforcers(
            new RateLimits(), new NoOpAuditLogger(),
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
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger());
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
            new RateLimits(), new NoOpAuditLogger(),
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
            new RateLimits(), new NoOpAuditLogger(),
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
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger());
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
        var enforcers = new AuthorityEnforcers(rate, audit);

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
